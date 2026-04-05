using System.Text.Json;
using CopilotVoice.Voice;

namespace CopilotVoice.Tests.Voice;

public class FunctionCallHandlerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FunctionCallHandler _sut;

    public FunctionCallHandlerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"voice-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
        _sut = new FunctionCallHandler(workspaceRoot: _tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // --- send_to_cli ---

    [Fact]
    public async Task HandleAsync_SendToCli_ReturnsQueuedWhenNoBridge()
    {
        var call = new FunctionCall("c1", "send_to_cli", """{"prompt":"git status"}""");

        var result = await _sut.HandleAsync(call);
        var doc = JsonDocument.Parse(result);

        Assert.Equal("queued", doc.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task HandleAsync_SendToCli_ReturnsErrorWhenMissingPrompt()
    {
        var call = new FunctionCall("c2", "send_to_cli", """{"prompt":""}""");

        var result = await _sut.HandleAsync(call);
        var doc = JsonDocument.Parse(result);

        Assert.True(doc.RootElement.TryGetProperty("error", out _));
    }

    [Fact]
    public async Task HandleAsync_SendToCli_SendsViaBridge()
    {
        var bridge = new FakeSessionBridge();
        var handler = new FunctionCallHandler(_tempDir, bridge);
        var call = new FunctionCall("c3", "send_to_cli", """{"prompt":"npm test"}""");

        var result = await handler.HandleAsync(call);
        var doc = JsonDocument.Parse(result);

        Assert.Equal("sent", doc.RootElement.GetProperty("status").GetString());
        Assert.Equal("npm test", bridge.LastCommand);
    }

    // --- get_session_context ---

    [Fact]
    public async Task HandleAsync_GetSessionContext_ReturnsDefaultWhenNoBridge()
    {
        var call = new FunctionCall("c4", "get_session_context", "{}");

        var result = await _sut.HandleAsync(call);
        var doc = JsonDocument.Parse(result);

        Assert.Equal("idle", doc.RootElement.GetProperty("status").GetString());
        Assert.Equal(_tempDir, doc.RootElement.GetProperty("workingDirectory").GetString());
    }

    [Fact]
    public async Task HandleAsync_GetSessionContext_ReturnsBridgeState()
    {
        var bridge = new FakeSessionBridge
        {
            State = new SessionBridgeState("busy", "grep", "/home/dev/project")
        };
        var handler = new FunctionCallHandler(_tempDir, bridge);
        var call = new FunctionCall("c5", "get_session_context", "{}");

        var result = await handler.HandleAsync(call);
        var doc = JsonDocument.Parse(result);

        Assert.Equal("busy", doc.RootElement.GetProperty("status").GetString());
        Assert.Equal("grep", doc.RootElement.GetProperty("currentTool").GetString());
    }

    // --- get_file_content ---

    [Fact]
    public async Task HandleAsync_GetFileContent_ReadsFile()
    {
        var filePath = Path.Combine(_tempDir, "readme.txt");
        File.WriteAllText(filePath, "Hello, world!");

        var call = new FunctionCall("c6", "get_file_content", """{"path":"readme.txt"}""");

        var result = await _sut.HandleAsync(call);
        var doc = JsonDocument.Parse(result);

        Assert.Equal("Hello, world!", doc.RootElement.GetProperty("content").GetString());
        Assert.False(doc.RootElement.GetProperty("truncated").GetBoolean());
    }

    [Fact]
    public async Task HandleAsync_GetFileContent_ReadsSubdirectoryFile()
    {
        var subDir = Path.Combine(_tempDir, "src");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(subDir, "main.cs"), "class Main {}");

        var call = new FunctionCall("c7", "get_file_content", """{"path":"src/main.cs"}""");

        var result = await _sut.HandleAsync(call);
        var doc = JsonDocument.Parse(result);

        Assert.Equal("class Main {}", doc.RootElement.GetProperty("content").GetString());
    }

    [Fact]
    public async Task HandleAsync_GetFileContent_TruncatesLargeFile()
    {
        var content = new string('x', 5000);
        File.WriteAllText(Path.Combine(_tempDir, "large.txt"), content);

        var call = new FunctionCall("c8", "get_file_content", """{"path":"large.txt"}""");

        var result = await _sut.HandleAsync(call);
        var doc = JsonDocument.Parse(result);

        Assert.True(doc.RootElement.GetProperty("truncated").GetBoolean());
        Assert.Equal(4096, doc.RootElement.GetProperty("content").GetString()!.Length);
    }

    [Fact]
    public async Task HandleAsync_GetFileContent_ReturnsErrorForMissingFile()
    {
        var call = new FunctionCall("c9", "get_file_content", """{"path":"nonexistent.txt"}""");

        var result = await _sut.HandleAsync(call);
        var doc = JsonDocument.Parse(result);

        Assert.Contains("not found", doc.RootElement.GetProperty("error").GetString());
    }

    // --- Path traversal security ---

    [Fact]
    public async Task HandleAsync_GetFileContent_RejectsPathTraversal()
    {
        var call = new FunctionCall("c10", "get_file_content", """{"path":"../../etc/passwd"}""");

        var result = await _sut.HandleAsync(call);
        var doc = JsonDocument.Parse(result);

        Assert.Contains("traversal", doc.RootElement.GetProperty("error").GetString()!,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HandleAsync_GetFileContent_RejectsAbsolutePath()
    {
        var call = new FunctionCall("c11", "get_file_content", """{"path":"/etc/passwd"}""");

        var result = await _sut.HandleAsync(call);
        var doc = JsonDocument.Parse(result);

        Assert.Contains("Absolute", doc.RootElement.GetProperty("error").GetString()!,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HandleAsync_GetFileContent_RejectsBackslashTraversal()
    {
        var call = new FunctionCall("c12", "get_file_content", """{"path":"..\\..\\etc\\passwd"}""");

        var result = await _sut.HandleAsync(call);
        var doc = JsonDocument.Parse(result);

        Assert.True(doc.RootElement.TryGetProperty("error", out _));
    }

    [Fact]
    public async Task HandleAsync_GetFileContent_RejectsMissingPath()
    {
        var call = new FunctionCall("c13", "get_file_content", """{}""");

        var result = await _sut.HandleAsync(call);
        var doc = JsonDocument.Parse(result);

        Assert.Contains("Missing", doc.RootElement.GetProperty("error").GetString());
    }

    // --- Path validation unit tests ---

    [Theory]
    [InlineData("../../etc/passwd", "traversal")]
    [InlineData("/etc/passwd", "Absolute")]
    [InlineData("", "empty")]
    public void ValidatePath_RejectsInsecurePaths(string path, string expectedErrorFragment)
    {
        var error = FunctionCallHandler.ValidatePath(path);
        Assert.NotNull(error);
        Assert.Contains(expectedErrorFragment, error!, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("readme.txt")]
    [InlineData("src/main.cs")]
    [InlineData("docs/api/v1/spec.yaml")]
    public void ValidatePath_AcceptsValidPaths(string path)
    {
        var error = FunctionCallHandler.ValidatePath(path);
        Assert.Null(error);
    }

    // --- set_avatar ---

    [Fact]
    public async Task HandleAsync_SetAvatar_ReturnsAcknowledgment()
    {
        var call = new FunctionCall("c14", "set_avatar", """{"expression":"thinking"}""");

        var result = await _sut.HandleAsync(call);
        var doc = JsonDocument.Parse(result);

        Assert.Equal("set", doc.RootElement.GetProperty("status").GetString());
        Assert.Equal("thinking", doc.RootElement.GetProperty("expression").GetString());
    }

    // --- Unknown function ---

    [Fact]
    public async Task HandleAsync_UnknownFunction_ReturnsError()
    {
        var call = new FunctionCall("c15", "unknown_func", "{}");

        var result = await _sut.HandleAsync(call);
        var doc = JsonDocument.Parse(result);

        Assert.Contains("Unknown function", doc.RootElement.GetProperty("error").GetString());
    }

    // --- Tool definitions ---

    [Fact]
    public void GetToolDefinitions_ReturnsFourTools()
    {
        var tools = FunctionCallHandler.GetToolDefinitions();

        Assert.Equal(4, tools.Count);
        var names = tools.Select(t => t.Name).ToList();
        Assert.Contains("send_to_cli", names);
        Assert.Contains("get_session_context", names);
        Assert.Contains("get_file_content", names);
        Assert.Contains("set_avatar", names);
    }

    [Fact]
    public void GetToolDefinitions_ParametersAreValidJson()
    {
        var tools = FunctionCallHandler.GetToolDefinitions();

        foreach (var tool in tools)
        {
            var ex = Record.Exception(() => JsonDocument.Parse(tool.ParametersJson));
            Assert.Null(ex);
        }
    }

    // --- Malformed arguments ---

    [Fact]
    public async Task HandleAsync_HandlesInvalidJsonArguments()
    {
        var call = new FunctionCall("c16", "send_to_cli", "not-json");

        var result = await _sut.HandleAsync(call);
        var doc = JsonDocument.Parse(result);

        // Should handle gracefully (either error or missing prompt)
        Assert.True(doc.RootElement.TryGetProperty("error", out _) ||
                    doc.RootElement.TryGetProperty("status", out _));
    }
}

// --- Test doubles ---

internal class FakeSessionBridge : ICliBridgeClient
{
    public string? LastCommand { get; private set; }
    public SessionBridgeState State { get; set; } = new("idle", null, "/test");

    public Task SendCommandAsync(string prompt, CancellationToken ct = default)
    {
        LastCommand = prompt;
        return Task.CompletedTask;
    }

    public SessionBridgeState GetState() => State;
}
