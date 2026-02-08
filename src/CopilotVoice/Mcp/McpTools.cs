using System.Text.Json;

namespace CopilotVoice.Mcp;

/// <summary>
/// Defines the MCP tools exposed by Copilot Voice.
/// </summary>
public static class McpToolDefinitions
{
    public static readonly object[] All = new object[]
    {
        new
        {
            name = "speak",
            description = "Speak text aloud using text-to-speech. Use this to give voice feedback to the user.",
            inputSchema = new
            {
                type = "object",
                properties = new Dictionary<string, object>
                {
                    ["text"] = new { type = "string", description = "The text to speak aloud" },
                    ["voice"] = new { type = "string", description = "Voice name (optional)" },
                },
                required = new[] { "text" },
            },
        },
        new
        {
            name = "listen",
            description = "Start listening for voice input via microphone. Returns the transcribed text when the user finishes speaking.",
            inputSchema = new
            {
                type = "object",
                properties = new Dictionary<string, object>
                {
                    ["duration_seconds"] = new { type = "number", description = "Max listen duration in seconds (default: 10)" },
                    ["language"] = new { type = "string", description = "Language code, e.g. en-US (optional)" },
                },
            },
        },
        new
        {
            name = "set_avatar",
            description = "Change the avatar's expression or state in the Copilot Voice UI.",
            inputSchema = new
            {
                type = "object",
                properties = new Dictionary<string, object>
                {
                    ["expression"] = new { type = "string", description = "Expression: normal, thinking, speaking, listening, focused, relaxed, sleeping" },
                },
                required = new[] { "expression" },
            },
        },
        new
        {
            name = "notify",
            description = "Show a notification message in the Copilot Voice avatar UI with optional voice.",
            inputSchema = new
            {
                type = "object",
                properties = new Dictionary<string, object>
                {
                    ["message"] = new { type = "string", description = "Notification message" },
                    ["speak"] = new { type = "boolean", description = "Also speak the message aloud (default: true)" },
                },
                required = new[] { "message" },
            },
        },
        new
        {
            name = "copilot-voice-listen",
            description = "Start listening for voice input via microphone. Returns the transcribed text when the user finishes speaking.",
            inputSchema = new
            {
                type = "object",
                properties = new Dictionary<string, object>
                {
                    ["duration_seconds"] = new { type = "number", description = "Max listen duration in seconds (default: 10)" },
                    ["language"] = new { type = "string", description = "Language code, e.g. en-US (optional)" },
                },
            },
        },
        new
        {
            name = "copilot-voice-speak",
            description = "Speak text aloud using text-to-speech. Use this to give voice feedback to the user.",
            inputSchema = new
            {
                type = "object",
                properties = new Dictionary<string, object>
                {
                    ["text"] = new { type = "string", description = "The text to speak aloud" },
                    ["voice"] = new { type = "string", description = "Voice name (optional)" },
                },
                required = new[] { "text" },
            },
        },
        new
        {
            name = "copilot-voice-set_avatar",
            description = "Change the avatar's expression or state in the Copilot Voice UI.",
            inputSchema = new
            {
                type = "object",
                properties = new Dictionary<string, object>
                {
                    ["expression"] = new { type = "string", description = "Expression: normal, thinking, speaking, listening, focused, relaxed, sleeping" },
                },
                required = new[] { "expression" },
            },
        },
        new
        {
            name = "copilot-voice-notify",
            description = "Show a notification message in the Copilot Voice avatar UI with optional voice.",
            inputSchema = new
            {
                type = "object",
                properties = new Dictionary<string, object>
                {
                    ["message"] = new { type = "string", description = "Notification message" },
                    ["speak"] = new { type = "boolean", description = "Also speak the message aloud (default: true)" },
                },
                required = new[] { "message" },
            },
        },
        new
        {
            name = "copilot-voice-window",
            description = "Control the Copilot Voice avatar window. Show, hide, move, or toggle always-on-top.",
            inputSchema = new
            {
                type = "object",
                properties = new Dictionary<string, object>
                {
                    ["action"] = new { type = "string", description = "Action: show, hide, toggle, topmost_on, topmost_off, move" },
                    ["x"] = new { type = "number", description = "X position (only for move action)" },
                    ["y"] = new { type = "number", description = "Y position (only for move action)" },
                    ["position"] = new { type = "string", description = "Named position: top-left, top-right, bottom-left, bottom-right, center (only for move action)" },
                },
                required = new[] { "action" },
            },
        },
    };
}

/// <summary>
/// Handles MCP tool calls. Delegates to the actual app services.
/// </summary>
public static class McpToolHandler
{
    // These delegates are set by AppServices when the MCP server starts
    public static Func<string, string?, Task>? OnSpeak { get; set; }
    public static Func<int, string?, Task<string>>? OnListen { get; set; }
    public static Action<string>? OnSetAvatar { get; set; }
    public static Func<string, bool, Task>? OnNotify { get; set; }
    public static Func<string, int?, int?, string?, Task<string>>? OnWindowControl { get; set; }

    public static async Task<object> HandleAsync(string toolName, JsonElement? args)
    {
        return toolName switch
        {
            "speak" or "copilot-voice-speak" => await HandleSpeakAsync(args),
            "listen" or "copilot-voice-listen" => await HandleListenAsync(args),
            "set_avatar" or "copilot-voice-set_avatar" => HandleSetAvatar(args),
            "notify" or "copilot-voice-notify" => await HandleNotifyAsync(args),
            "copilot-voice-window" => await HandleWindowAsync(args),
            _ => new { content = new[] { new { type = "text", text = $"Unknown tool: {toolName}" } }, isError = true },
        };
    }

    private static async Task<object> HandleSpeakAsync(JsonElement? args)
    {
        var text = args?.GetProperty("text").GetString() ?? "";
        var voice = args?.TryGetProperty("voice", out var v) == true ? v.GetString() : null;

        if (OnSpeak != null)
            await OnSpeak(text, voice);

        return new { content = new[] { new { type = "text", text = $"Spoke: \"{text}\"" } } };
    }

    private static async Task<object> HandleListenAsync(JsonElement? args)
    {
        var duration = args?.TryGetProperty("duration_seconds", out var d) == true ? d.GetInt32() : 10;
        var language = args?.TryGetProperty("language", out var l) == true ? l.GetString() : null;

        if (OnListen != null)
        {
            var transcription = await OnListen(duration, language);
            return new { content = new[] { new { type = "text", text = transcription } } };
        }

        return new { content = new[] { new { type = "text", text = "Listening not available" } }, isError = true };
    }

    private static object HandleSetAvatar(JsonElement? args)
    {
        var expression = args?.GetProperty("expression").GetString() ?? "normal";
        OnSetAvatar?.Invoke(expression);
        return new { content = new[] { new { type = "text", text = $"Avatar set to: {expression}" } } };
    }

    private static async Task<object> HandleNotifyAsync(JsonElement? args)
    {
        var message = args?.GetProperty("message").GetString() ?? "";
        var speak = args?.TryGetProperty("speak", out var s) != true || s.GetBoolean();

        if (OnNotify != null)
            await OnNotify(message, speak);

        return new { content = new[] { new { type = "text", text = $"Notified: \"{message}\"" } } };
    }

    private static async Task<object> HandleWindowAsync(JsonElement? args)
    {
        var action = args?.GetProperty("action").GetString() ?? "";
        int? x = args?.TryGetProperty("x", out var xv) == true ? xv.GetInt32() : null;
        int? y = args?.TryGetProperty("y", out var yv) == true ? yv.GetInt32() : null;
        var position = args?.TryGetProperty("position", out var pv) == true ? pv.GetString() : null;

        if (OnWindowControl != null)
        {
            var result = await OnWindowControl(action, x, y, position);
            return new { content = new[] { new { type = "text", text = result } } };
        }

        return new { content = new[] { new { type = "text", text = "Window control not available" } }, isError = true };
    }
}
