using System.Diagnostics;

namespace CopilotVoice.Audio;

/// <summary>
/// Captures microphone audio in PCM16 format using sox/rec (macOS/Linux)
/// or ffmpeg as fallback. Outputs 24kHz mono PCM16 for the Voice Live API.
/// </summary>
public sealed class MicCapture : IMicCapture
{
    /// <summary>API expects 24kHz PCM16 mono.</summary>
    private const int SampleRate = 24000;
    private const int Channels = 1;
    private const int BytesPerSample = 2;
    /// <summary>Read ~100ms chunks from the process.</summary>
    private const int ChunkBytes = SampleRate * BytesPerSample * Channels / 10; // 4800 bytes

    private readonly object _lock = new();
    private Process? _process;
    private volatile bool _isCapturing;
    private bool _disposed;
    private CancellationTokenSource? _readCts;
    private Task? _readTask;

    public bool IsCapturing => _isCapturing;
    public event Action<ReadOnlyMemory<byte>>? AudioCaptured;

    public Task StartAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_isCapturing) return Task.CompletedTask;

        lock (_lock)
        {
            if (_isCapturing) return Task.CompletedTask;

            // Use sox/rec to capture raw PCM16 audio from default mic
            var psi = new ProcessStartInfo
            {
                FileName = "rec",
                // Output raw PCM16, 24kHz, mono, 16-bit to stdout
                Arguments = "-q -r 24000 -c 1 -b 16 -e signed-integer -t raw -",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            try
            {
                _process = Process.Start(psi);
            }
            catch (Exception)
            {
                // Try ffmpeg as fallback
                psi.FileName = "ffmpeg";
                psi.Arguments = "-f avfoundation -i :default -ar 24000 -ac 1 -f s16le -loglevel quiet -";
                try
                {
                    _process = Process.Start(psi);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        "No audio capture tool found. Install sox (brew install sox) or ffmpeg.", ex);
                }
            }

            if (_process is null)
                throw new InvalidOperationException("Failed to start audio capture process.");

            _isCapturing = true;
            _readCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            // Read audio data from stdout in a background task
            _readTask = Task.Run(() => ReadAudioLoop(_process, _readCts.Token));

            Console.Error.WriteLine($"[MicCapture] Started — pid={_process.Id}, rate={SampleRate}Hz, format=PCM16 mono");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        if (!_isCapturing) return Task.CompletedTask;

        lock (_lock)
        {
            if (!_isCapturing) return Task.CompletedTask;
            _isCapturing = false;
            KillProcess();
            Console.Error.WriteLine("[MicCapture] Stopped");
        }

        return Task.CompletedTask;
    }

    private async Task ReadAudioLoop(Process proc, CancellationToken ct)
    {
        var buffer = new byte[ChunkBytes];
        var stream = proc.StandardOutput.BaseStream;

        try
        {
            while (!ct.IsCancellationRequested && _isCapturing)
            {
                int totalRead = 0;
                while (totalRead < ChunkBytes)
                {
                    int read = await stream.ReadAsync(
                        buffer.AsMemory(totalRead, ChunkBytes - totalRead), ct);
                    if (read == 0) break; // process ended
                    totalRead += read;
                }

                if (totalRead > 0 && _isCapturing)
                {
                    var chunk = buffer.AsMemory(0, totalRead);
                    AudioCaptured?.Invoke(chunk);
                }

                if (totalRead == 0) break;
            }
        }
        catch (OperationCanceledException) { /* expected */ }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[MicCapture] Read error: {ex.Message}");
        }
    }

    private void KillProcess()
    {
        _readCts?.Cancel();
        if (_process is not null && !_process.HasExited)
        {
            try { _process.Kill(); } catch { /* best effort */ }
            try { _process.WaitForExit(2000); } catch { }
        }
        try { _process?.Dispose(); } catch { }
        _process = null;
        _readCts?.Dispose();
        _readCts = null;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            _isCapturing = false;
            KillProcess();
        }
    }
}
