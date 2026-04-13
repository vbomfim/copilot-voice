using System.Diagnostics;

namespace CopilotVoice.Audio;

/// <summary>
/// Captures microphone audio in PCM16 format using an external process.
/// Prefers ffmpeg with AVFoundation (macOS) which correctly accesses USB mics
/// like the C920. Falls back to sox/rec if ffmpeg is unavailable.
///
/// Background: PortAudio and sox/rec use CoreAudio HAL which returns silent
/// zero-buffers from some USB devices on macOS. QuickTime and ffmpeg use
/// AVFoundation which works correctly. The original project used Azure Speech
/// SDK's AudioConfig.FromDefaultMicrophoneInput() which also uses AVFoundation.
///
/// macOS TCC (Transparency, Consent, and Control) note:
/// Microphone access requires the responsible app (terminal or .app bundle) to
/// have NSMicrophoneUsageDescription in its Info.plist. If denied, CoreAudio HAL
/// silently returns zeros while AVFoundation (ffmpeg) crashes with SIGABRT (134).
/// When running as a .app bundle, the app's own Info.plist grants this.
/// When running from a terminal, the terminal app must have mic permission.
/// </summary>
public sealed class MicCapture : IMicCapture
{
    private const int SampleRate = 24000;
    private const int Channels = 1;
    private const int BytesPerSample = 2;
    private const int ChunkBytes = SampleRate * BytesPerSample * Channels / 10; // 4800 bytes = 100ms

    /// <summary>Exit code 134 = SIGABRT (128 + 6), the macOS TCC denial signal.</summary>
    private const int SigAbrtExitCode = 134;

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

            _process = StartCaptureProcess();
            _isCapturing = true;
            _readCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _readTask = Task.Run(() => ReadAudioLoop(_process, _readCts.Token));

            Console.Error.WriteLine(
                $"[MicCapture] Started — pid={_process.Id}, rate={SampleRate}Hz, format=PCM16 mono");
        }

        return Task.CompletedTask;
    }

    private static Process StartCaptureProcess()
    {
        if (OperatingSystem.IsMacOS())
        {
            var p = TryStartFfmpegAvFoundation();
            if (p is not null) return p;
        }

        // Cross-platform: ffmpeg with default input (ALSA on Linux, dshow on Windows)
        {
            var p = TryStartFfmpegDefault();
            if (p is not null) return p;
        }

        // Fallback: sox/rec (uses CoreAudio HAL on macOS — may return silence from USB mics)
        {
            var p = TryStartSoxRec();
            if (p is not null) return p;
        }

        throw new InvalidOperationException(
            "No audio capture tool found. Install ffmpeg (brew install ffmpeg) or sox (brew install sox).");
    }

    /// <summary>
    /// ffmpeg with AVFoundation — the preferred path on macOS.
    /// AVFoundation correctly handles USB mic sample rate conversion and
    /// triggers the macOS TCC permission prompt (unlike CoreAudio HAL).
    /// If the app/terminal lacks NSMicrophoneUsageDescription, ffmpeg will
    /// be killed with SIGABRT (exit 134). We detect this and log guidance.
    /// </summary>
    private static Process? TryStartFfmpegAvFoundation()
    {
        try
        {
            var p = Process.Start(new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = "-f avfoundation -i :default -ar 24000 -ac 1 -f s16le -loglevel quiet pipe:1",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            });
            if (p is null) return null;

            // Give ffmpeg a moment to initialize AVFoundation and trigger TCC check.
            // SIGABRT from TCC denial happens during device open, within ~200ms.
            if (p.WaitForExit(500))
            {
                if (p.ExitCode == SigAbrtExitCode)
                {
                    Console.Error.WriteLine(
                        "[MicCapture] ffmpeg killed by macOS TCC — microphone permission denied.\n" +
                        "  Fix: grant mic access to your terminal app in System Settings → Privacy & Security → Microphone.\n" +
                        "  Or run as a .app bundle (scripts/bundle-macos.sh) which has NSMicrophoneUsageDescription.");
                    p.Dispose();
                    return null;
                }

                Console.Error.WriteLine($"[MicCapture] ffmpeg exited early (code={p.ExitCode})");
                p.Dispose();
                return null;
            }

            Console.Error.WriteLine("[MicCapture] Using ffmpeg (AVFoundation)");
            return p;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// ffmpeg with platform default input (ALSA on Linux, dshow on Windows).
    /// Not used on macOS since AVFoundation is preferred there.
    /// </summary>
    private static Process? TryStartFfmpegDefault()
    {
        if (OperatingSystem.IsMacOS()) return null;

        string inputFormat;
        string inputDevice;
        if (OperatingSystem.IsLinux())
        {
            inputFormat = "alsa";
            inputDevice = "default";
        }
        else if (OperatingSystem.IsWindows())
        {
            inputFormat = "dshow";
            inputDevice = "audio=default";
        }
        else
        {
            return null;
        }

        try
        {
            var p = Process.Start(new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = $"-f {inputFormat} -i {inputDevice} -ar 24000 -ac 1 -f s16le -loglevel quiet pipe:1",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            });
            if (p is not null)
            {
                Console.Error.WriteLine($"[MicCapture] Using ffmpeg ({inputFormat})");
                return p;
            }
        }
        catch { }

        return null;
    }

    /// <summary>
    /// sox/rec fallback. Uses CoreAudio HAL on macOS which may return silence
    /// from USB microphones due to sample rate negotiation failures.
    /// Works reliably on Linux (ALSA) and Windows (waveIn).
    /// </summary>
    private static Process? TryStartSoxRec()
    {
        try
        {
            var p = Process.Start(new ProcessStartInfo
            {
                FileName = "rec",
                Arguments = "-q -r 24000 -c 1 -b 16 -e signed-integer -t raw -",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            });
            if (p is not null)
            {
                Console.Error.WriteLine(
                    "[MicCapture] Using sox/rec (CoreAudio HAL — USB mics may produce silence on macOS)");
                return p;
            }
        }
        catch { }

        return null;
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
                    if (read == 0) break;
                    totalRead += read;
                }

                if (totalRead > 0 && _isCapturing)
                    AudioCaptured?.Invoke(buffer.AsMemory(0, totalRead));

                if (totalRead == 0) break;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[MicCapture] Read error: {ex.Message}");
        }

        // Check if process died from TCC denial during capture
        if (OperatingSystem.IsMacOS() && proc.HasExited && proc.ExitCode == SigAbrtExitCode)
        {
            Console.Error.WriteLine(
                "[MicCapture] Capture process killed by macOS (SIGABRT) — likely TCC mic permission denied.");
        }
    }

    private void KillProcess()
    {
        _readCts?.Cancel();
        if (_process is not null && !_process.HasExited)
        {
            try { _process.Kill(); } catch { }
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
