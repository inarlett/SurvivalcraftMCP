using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Engine;
using Engine.Graphics;
using HarmonyLib;

namespace Game;

[HarmonyPatch(typeof(HelpScreen), MethodType.Constructor)]
internal static class HelpScreenPatch
{
    private const string TopicName = "McpBridge";

    [HarmonyPostfix]
    private static void Postfix(HelpScreen __instance)
    {
        if (__instance.m_topics.ContainsKey(TopicName))
            return;

        HelpTopic topic = new()
        {
            Index = 900f,
            Name = TopicName,
            Title = LanguageControl.Get(nameof(McpModLoader), "HelpTitle"),
            Text = LanguageControl.Get(nameof(McpModLoader), "HelpText")
        };
        __instance.m_topics.Add(topic.Name, topic);
        __instance.m_topicsList.m_items.Add(topic);
        __instance.m_topicsList.m_items.Sort((a, b) =>
            a is HelpTopic left && b is HelpTopic right ? left.Index.CompareTo(right.Index) : 0);
    }

}

[HarmonyPatch(typeof(HelpTopicScreen), nameof(HelpTopicScreen.Enter))]
internal static class HelpTopicScreenPatch
{
    private const string TopicName = "McpBridge";
    private const string GeneratedNamePrefix = "McpHelpContent:";

    [HarmonyPostfix]
    private static void BuildHelpContent(HelpTopicScreen __instance, object[] parameters)
    {
        HelpTopic topic = parameters?.FirstOrDefault() as HelpTopic;
        StackPanelWidget content = __instance.m_textLabel.ParentWidget as StackPanelWidget;
        if (content == null)
            return;

        foreach (Widget child in content.Children.Where(widget => widget.Name?.StartsWith(GeneratedNamePrefix, StringComparison.Ordinal) == true).ToArray())
            content.Children.Remove(child);

        if (topic?.Name != TopicName)
        {
            __instance.m_textLabel.IsVisible = true;
            return;
        }

        __instance.m_textLabel.IsVisible = false;
        int insertIndex = content.Children.IndexOf(__instance.m_textLabel);
        foreach (HelpContentPart part in Parse(topic.Text ?? string.Empty))
        {
            Widget widget = CreateWidget(part);
            if (widget == null)
                continue;
            widget.Name = GeneratedNamePrefix + insertIndex;
            content.Children.Insert(insertIndex++, widget);
        }
    }

    private static IEnumerable<HelpContentPart> Parse(string text)
    {
        const string markerStart = "[[";
        const string markerEnd = "]]";
        int position = 0;
        while (position < text.Length)
        {
            int start = text.IndexOf(markerStart, position, StringComparison.Ordinal);
            if (start < 0)
            {
                yield return HelpContentPart.TextPart(text[position..]);
                yield break;
            }
            if (start > position)
                yield return HelpContentPart.TextPart(text[position..start]);

            int end = text.IndexOf(markerEnd, start + markerStart.Length, StringComparison.Ordinal);
            if (end < 0)
            {
                yield return HelpContentPart.TextPart(text[start..]);
                yield break;
            }

            string marker = text[(start + markerStart.Length)..end];
            if (TryParseMarker(marker, out HelpContentPart part))
                yield return part;
            else
                yield return HelpContentPart.TextPart(text[start..(end + markerEnd.Length)]);
            position = end + markerEnd.Length;
        }
    }

    private static bool TryParseMarker(string marker, out HelpContentPart part)
    {
        part = default;
        string[] pieces = marker.Split('|');
        if (pieces.Length == 0)
            return false;

        if (pieces[0].StartsWith("image:", StringComparison.OrdinalIgnoreCase)
            && pieces.Length == 3
            && int.TryParse(pieces[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int width)
            && int.TryParse(pieces[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int height)
            && width > 0 && height > 0)
        {
            string path = pieces[0]["image:".Length..];
            if (!IsSafeAssetPath(path))
                return false;
            part = HelpContentPart.Image(path, width, height);
            return true;
        }

        if (pieces[0].StartsWith("link:", StringComparison.OrdinalIgnoreCase) && pieces.Length == 2)
        {
            string label = pieces[0]["link:".Length..];
            string url = pieces[1];
            if (string.IsNullOrWhiteSpace(label) || !Uri.TryCreate(url, UriKind.Absolute, out Uri parsed)
                || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
                return false;
            part = HelpContentPart.Link(label, url);
            return true;
        }
        return false;
    }

    private static Widget CreateWidget(HelpContentPart part)
    {
        if (part.Kind == HelpContentKind.Text)
        {
            return string.IsNullOrEmpty(part.Text) ? null : new LabelWidget
            {
                Text = part.Text,
                Color = new Color(128, 128, 128),
                HorizontalAlignment = WidgetAlignment.Center,
                TextAnchor = TextAnchor.HorizontalCenter,
                WordWrap = true
            };
        }

        if (part.Kind == HelpContentKind.Link)
        {
            return new LinkWidget
            {
                Text = part.Text,
                Url = part.Url,
                Color = new Color(64, 192, 64),
                HorizontalAlignment = WidgetAlignment.Center,
                TextAnchor = TextAnchor.HorizontalCenter,
                Margin = new Vector2(10f, 6f)
            };
        }

        if (!ModsManager.PackageNameToModEntity.TryGetValue(McpModLoader.PackageName, out ModEntity entity))
            return null;
        Texture2D texture = null;
        entity.GetFile(part.Text, stream => texture = Texture2D.Load(stream));
        return texture == null ? null : new RectangleWidget
        {
            Size = new Vector2(part.Width, part.Height),
            HorizontalAlignment = WidgetAlignment.Center,
            VerticalAlignment = WidgetAlignment.Near,
            Margin = new Vector2(10f, 10f),
            FillColor = Color.White,
            OutlineColor = Color.Transparent,
            Subtexture = new Subtexture(texture),
            TextureLinearFilter = true
        };
    }

    private static bool IsSafeAssetPath(string path) =>
        path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)
        && !path.Contains("..", StringComparison.Ordinal)
        && !path.Contains('\\');

    private enum HelpContentKind { Text, Image, Link }

    private readonly record struct HelpContentPart(HelpContentKind Kind, string Text, string Url, int Width, int Height)
    {
        public static HelpContentPart TextPart(string value) => new(HelpContentKind.Text, value, null, 0, 0);
        public static HelpContentPart Image(string path, int width, int height) => new(HelpContentKind.Image, path, null, width, height);
        public static HelpContentPart Link(string label, string url) => new(HelpContentKind.Link, label, url, 0, 0);
    }
}

[HarmonyPatch(typeof(ModSettingsScreen), nameof(ModSettingsScreen.Update))]
internal static class McpModSettingsNavigationPatch
{
    private const string HelpPageId = "BridgeHelp";

    [HarmonyPrefix]
    private static bool Prefix(ModSettingsScreen __instance)
    {
        var field = AccessTools.Field(typeof(ModSettingsScreen), "m_navButtons");
        if (field?.GetValue(__instance) is not Dictionary<BevelledButtonWidget, (string PackageName, string[] PageIds, string Title)> buttons)
            return true;

        foreach (var entry in buttons)
        {
            if (!entry.Key.IsClicked
                || entry.Value.PackageName != McpModLoader.PackageName
                || entry.Value.PageIds.Length != 1
                || entry.Value.PageIds[0] != HelpPageId)
                continue;

            HelpScreen helpScreen = ScreensManager.FindScreen<HelpScreen>("Help");
            if (helpScreen != null && helpScreen.m_topics.TryGetValue("McpBridge", out HelpTopic topic))
            {
                ScreensManager.SwitchScreen("HelpTopic", topic);
                return false;
            }
        }

        return true;
    }
}
