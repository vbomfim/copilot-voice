using CopilotVoice.Audio;

namespace CopilotVoice.Tests.Audio;

/// <summary>
/// Unit tests for real MicCapture implementation.
/// These tests validate the contract and state machine behavior
/// without requiring actual audio hardware (PortAudio is not initialized
/// in test context — hardware-dependent tests are marked with [Trait]).
/// </summary>
public class MicCaptureTests : IDisposable
{
    private readonly MicCapture _sut = new();

    public void Dispose() => _sut.Dispose();

    // --- AC1: Initial state ---

    [Fact]
    public void InitialState_IsNotCapturing()
    {
        Assert.False(_sut.IsCapturing);
    }

    // --- AC3: StopAsync idempotent ---

    [Fact]
    public async Task StopAsync_WhenNotCapturing_IsNoOp()
    {
        // Should not throw when calling stop while not capturing
        await _sut.StopAsync();
        Assert.False(_sut.IsCapturing);
    }

    [Fact]
    public async Task StopAsync_CalledTwice_IsIdempotent()
    {
        await _sut.StopAsync();
        await _sut.StopAsync();
        Assert.False(_sut.IsCapturing);
    }

    // --- Dispose behavior ---

    [Fact]
    public void Dispose_SetsNotCapturing()
    {
        _sut.Dispose();
        Assert.False(_sut.IsCapturing);
    }

    [Fact]
    public async Task StartAsync_AfterDispose_ThrowsObjectDisposed()
    {
        _sut.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => _sut.StartAsync());
    }

    [Fact]
    public void Dispose_CalledTwice_IsIdempotent()
    {
        _sut.Dispose();
        _sut.Dispose(); // should not throw
    }

    // --- AC1/AC2: Real mic tests (require hardware) ---

    [Fact]
    [Trait("Category", "Hardware")]
    public async Task StartAsync_WithMicrophone_SetsIsCapturing()
    {
        // This test requires a real microphone. Skip in CI.
        try
        {
            await _sut.StartAsync();
            Assert.True(_sut.IsCapturing);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("microphone") || ex.Message.Contains("PortAudio"))
        {
            // No mic available — skip gracefully
            return;
        }
        finally
        {
            await _sut.StopAsync();
        }
    }

    [Fact]
    [Trait("Category", "Hardware")]
    public async Task StartAsync_ThenStop_SetsNotCapturing()
    {
        try
        {
            await _sut.StartAsync();
            await _sut.StopAsync();
            Assert.False(_sut.IsCapturing);
        }
        catch (InvalidOperationException)
        {
            // No mic available — skip gracefully
        }
    }

    [Fact]
    [Trait("Category", "Hardware")]
    public async Task StartAsync_WhenAlreadyCapturing_IsIdempotent()
    {
        try
        {
            await _sut.StartAsync();
            await _sut.StartAsync(); // second call should be a no-op
            Assert.True(_sut.IsCapturing);
        }
        catch (InvalidOperationException)
        {
            return;
        }
        finally
        {
            await _sut.StopAsync();
        }
    }

    [Fact]
    [Trait("Category", "Hardware")]
    public async Task AudioCaptured_FiresWithData_WhenCapturing()
    {
        var receivedChunks = new List<ReadOnlyMemory<byte>>();
        _sut.AudioCaptured += chunk => receivedChunks.Add(chunk);

        try
        {
            await _sut.StartAsync();

            // Wait up to 500ms for at least one audio chunk
            var deadline = DateTime.UtcNow.AddMilliseconds(500);
            while (receivedChunks.Count == 0 && DateTime.UtcNow < deadline)
                await Task.Delay(10);

            // With a real mic, we should get at least one chunk in 500ms
            Assert.NotEmpty(receivedChunks);

            // Each chunk should contain PCM16 data (non-zero length, even byte count)
            foreach (var chunk in receivedChunks)
            {
                Assert.True(chunk.Length > 0, "Chunk should not be empty");
                Assert.Equal(0, chunk.Length % 2); // PCM16 = 2 bytes per sample
            }
        }
        catch (InvalidOperationException)
        {
            return; // No mic available
        }
        finally
        {
            await _sut.StopAsync();
        }
    }

    [Fact]
    [Trait("Category", "Hardware")]
    public async Task StopAsync_StopsAudioChunks()
    {
        var chunksAfterStop = new List<ReadOnlyMemory<byte>>();

        try
        {
            await _sut.StartAsync();
            await Task.Delay(100); // Let some audio flow
            await _sut.StopAsync();

            _sut.AudioCaptured += chunk => chunksAfterStop.Add(chunk);
            await Task.Delay(200); // Wait to confirm no more chunks

            Assert.Empty(chunksAfterStop);
        }
        catch (InvalidOperationException)
        {
            return;
        }
    }
}
