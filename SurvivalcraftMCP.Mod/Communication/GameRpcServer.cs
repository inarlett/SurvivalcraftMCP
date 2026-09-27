using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Engine;
using Game.Mcp;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Game.Mcp.Communication;

public sealed class GameRpcServer : IDisposable
{
    private readonly int _port;
    private readonly ConcurrentQueue<(RpcRequest Request, TaskCompletionSource<RpcResponse> Completion)> _queue = new();
    private TcpListener _listener;
    private CancellationTokenSource _cts;
    private Task _acceptTask;

    public GameRpcServer(int port) => _port = port;

    public void Start()
    {
        if (_listener != null) return;
        _cts = new CancellationTokenSource();
        _listener = new TcpListener(IPAddress.Loopback, _port);
        _listener.Start();
        _acceptTask = Task.Run(AcceptLoopAsync);
        Log.Information($"Survivalcraft MCP: RPC listening on 127.0.0.1:{_port}");
    }

    public void Stop()
    {
        _cts?.Cancel();
        try { _listener?.Stop(); } catch { }
        _listener = null;
        while (_queue.TryDequeue(out var item))
            item.Completion.TrySetResult(new RpcResponse(item.Request.Id, Error: "game_rpc_stopped"));
    }

    public bool TryDequeue(out RpcRequest request, out TaskCompletionSource<RpcResponse> completion)
    {
        if (_queue.TryDequeue(out var item))
        {
            request = item.Request;
            completion = item.Completion;
            return true;
        }
        request = null;
        completion = null;
        return false;
    }

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                TcpClient client = await _listener.AcceptTcpClientAsync(_cts.Token);
                _ = Task.Run(() => HandleClientAsync(client), _cts.Token);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { Log.Error($"Survivalcraft MCP RPC accept error: {ex}"); }
        }
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        try
        {
            using (client)
            using (NetworkStream stream = client.GetStream())
            using (StreamReader reader = new(stream))
            using (StreamWriter writer = new(stream)
                {
                    AutoFlush = true
                })
            {
                while (true)
                {
                    string line = await reader.ReadLineAsync();

                    if (line == null)
                        break;

                    string responseJson = await ProcessRequestAsync(line);

                    await writer.WriteLineAsync(responseJson);
                }
            }
        }
        catch (Exception ex)
        {
            Engine.Log.Error($"MCP client error: {ex}");
        }
    }

    private async Task<string> ProcessRequestAsync(string line)
    {
        using JsonDocument document = JsonDocument.Parse(line);
        JsonElement root = document.RootElement;
        string id = root.TryGetProperty("id", out JsonElement idElement)
            ? idElement.ToString()
            : Guid.NewGuid().ToString("N");
        string method = root.GetProperty("method").GetString()
            ?? throw new InvalidDataException("RPC method must be a string.");
        JsonElement parameters = root.TryGetProperty("params", out JsonElement paramsElement)
            ? paramsElement.Clone()
            : default;
        var request = new RpcRequest(id, method, parameters);
        var completion = new TaskCompletionSource<RpcResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        _queue.Enqueue((request, completion));
        RpcResponse response = await completion.Task.ConfigureAwait(false);
        return JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    public void Dispose() => Stop();
}
