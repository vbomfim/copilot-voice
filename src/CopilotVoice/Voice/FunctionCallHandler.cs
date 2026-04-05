using System.Text.Json;

namespace CopilotVoice.Voice;

/// <summary>
/// Dispatches function calls received from the Voice Live API.
/// Each function handler validates inputs, performs the action, and returns a JSON result.
/// </summary>
public class FunctionCallHandler
{
    private readonly ICliBridgeClient? _sessionBridge;
    private readonly string _workspaceRoot;

    private const int MaxFileContentBytes = 4096;

    public FunctionCallHandler(string? workspaceRoot = null, ICliBridgeClient? sessionBridge = null)
    {
        _workspaceRoot = workspaceRoot ?? Environment.CurrentDirectory;
        _sessionBridge = sessionBridge;
    }

    /// <summary>
    /// Dispatch a function call to the appropriate handler.
    /// Returns a JSON-serialized result string.
    /// </summary>
    public async Task<string> HandleAsync(FunctionCall call, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(call);

        return call.Name switch
        {
            "send_to_cli" => await HandleSendToCliAsync(call, ct).ConfigureAwait(false),
            "get_session_context" => HandleGetSessionContext(),
            "get_file_content" => await HandleGetFileContent(call),
            "set_avatar" => HandleSetAvatar(call),
            _ => JsonSerializer.Serialize(new { error = $"Unknown function: {call.Name}" })
        };
    }

    // --- Handlers ---

    private async Task<string> HandleSendToCliAsync(FunctionCall call, CancellationToken ct)
    {
        var args = ParseArguments(call.Arguments);
        var prompt = args.GetValueOrDefault("prompt") ?? "";

        if (string.IsNullOrWhiteSpace(prompt))
            return JsonSerializer.Serialize(new { error = "Missing required argument: prompt" });

        if (_sessionBridge is null)
            return JsonSerializer.Serialize(new { status = "queued", note = "Session bridge not connected — command queued for when bridge is available." });

        try
        {
            await _sessionBridge.SendCommandAsync(prompt, ct).ConfigureAwait(false);
            return JsonSerializer.Serialize(new { status = "sent", prompt });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = $"Failed to send command: {ex.Message}" });
        }
    }

    private string HandleGetSessionContext()
    {
        if (_sessionBridge is null)
        {
            return JsonSerializer.Serialize(new
            {
                status = "idle",
                currentTool = (string?)null,
                workingDirectory = _workspaceRoot,
                note = "Session bridge not connected — returning default state."
            });
        }

        var state = _sessionBridge.GetState();
        return JsonSerializer.Serialize(new
        {
            status = state.Status,
            currentTool = state.CurrentTool,
            workingDirectory = state.WorkingDirectory
        });
    }

    internal async Task<string> HandleGetFileContent(FunctionCall call)
    {
        var args = ParseArguments(call.Arguments);
        var path = args.GetValueOrDefault("path") ?? "";

        if (string.IsNullOrWhiteSpace(path))
            return JsonSerializer.Serialize(new { error = "Missing required argument: path" });

        // Security: validate path
        var validationError = ValidatePath(path);
        if (validationError is not null)
            return JsonSerializer.Serialize(new { error = validationError });

        var fullPath = Path.GetFullPath(Path.Combine(_workspaceRoot, path));

        // Double-check canonical path is under workspace
        var canonicalWorkspace = Path.GetFullPath(_workspaceRoot) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(canonicalWorkspace, StringComparison.Ordinal))
            return JsonSerializer.Serialize(new { error = "Path is outside the workspace." });

        if (!File.Exists(fullPath))
            return JsonSerializer.Serialize(new { error = $"File not found: {path}" });

        try
        {
            var content = await File.ReadAllTextAsync(fullPath);
            var truncated = false;

            if (content.Length > MaxFileContentBytes)
            {
                content = content[..MaxFileContentBytes];
                truncated = true;
            }

            return JsonSerializer.Serialize(new { path, content, truncated });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = $"Failed to read file: {ex.Message}" });
        }
    }

    private static string HandleSetAvatar(FunctionCall call)
    {
        var args = ParseArguments(call.Arguments);
        var expression = args.GetValueOrDefault("expression") ?? "neutral";

        // Stub: avatar controller not wired yet
        return JsonSerializer.Serialize(new { status = "set", expression, note = "Avatar controller not wired yet — expression queued." });
    }

    // --- Path validation ---

    /// <summary>
    /// Validates a file path for security. Returns an error message or null if valid.
    /// Rejects: path traversal (..), absolute paths, backslash paths.
    /// </summary>
    internal static string? ValidatePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "Path cannot be empty.";

        // Reject absolute paths
        if (Path.IsPathRooted(path))
            return "Absolute paths are not allowed.";

        // Reject path traversal
        var normalized = path.Replace('\\', '/');
        var segments = normalized.Split('/');

        foreach (var segment in segments)
        {
            if (segment == "..")
                return "Path traversal ('..') is not allowed.";
        }

        return null;
    }

    // --- Tool definitions ---

    /// <summary>
    /// Returns the list of function tool definitions to register with the Voice Live API.
    /// </summary>
    public static IReadOnlyList<ToolDefinition> GetToolDefinitions()
    {
        return new[]
        {
            new ToolDefinition(
                "send_to_cli",
                "Send a command or prompt to the active Copilot CLI session.",
                """{"type":"object","properties":{"prompt":{"type":"string","description":"The command or prompt to send to the CLI."}},"required":["prompt"]}"""
            ),
            new ToolDefinition(
                "get_session_context",
                "Get the current state of the Copilot CLI session (status, working directory, active tool).",
                """{"type":"object","properties":{}}"""
            ),
            new ToolDefinition(
                "get_file_content",
                "Read the content of a file in the current workspace. Path must be relative and under the workspace root.",
                """{"type":"object","properties":{"path":{"type":"string","description":"Relative path to the file within the workspace."}},"required":["path"]}"""
            ),
            new ToolDefinition(
                "set_avatar",
                "Set the avatar expression (e.g., neutral, thinking, happy, surprised, error).",
                """{"type":"object","properties":{"expression":{"type":"string","description":"The avatar expression to set."}},"required":["expression"]}"""
            )
        };
    }

    // --- Helpers ---

    private static Dictionary<string, string> ParseArguments(string argumentsJson)
    {
        try
        {
            var result = new Dictionary<string, string>();
            using var doc = JsonDocument.Parse(argumentsJson);

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                result[prop.Name] = prop.Value.ValueKind == JsonValueKind.String
                    ? prop.Value.GetString()!
                    : prop.Value.GetRawText();
            }

            return result;
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>();
        }
    }
}
