using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace SurvivalcraftMCP.Server.Communication;

public sealed class GameClient
{
    private readonly string _host;
    private readonly int _port;

    public GameClient(string host = "127.0.0.1", int port = 28899)
    {
        _host = host;
        _port = port;
    }

    public async Task<JsonElement> CallAsync(string method, object parameters, CancellationToken cancellationToken)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(_host, _port, cancellationToken);
        await using NetworkStream stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.UTF8, false, 4096, true);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, true) { AutoFlush = true };

        var request = new
        {
            id = Guid.NewGuid().ToString("N"),
            method,
            @params = parameters
        };
        await writer.WriteLineAsync(JsonSerializer.Serialize(request));
        string? line = await reader.ReadLineAsync(cancellationToken);
        if (line is null) throw new IOException("Survivalcraft closed the RPC connection.");

        using JsonDocument doc = JsonDocument.Parse(line);
        JsonElement root = doc.RootElement;
        if (root.TryGetProperty("error", out var error) && error.ValueKind != JsonValueKind.Null)
            throw new InvalidOperationException(error.TryGetProperty("message", out var message)
                ? message.GetString()
                : error.GetRawText());
        return root.GetProperty("result").Clone();
    }
}
