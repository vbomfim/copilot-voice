namespace CopilotVoice.Audio;

/// <summary>
/// Thread-safe reference-counted PortAudio initialization manager.
/// Both MicCapture and AudioPlayer need PortAudio initialized before use.
/// Pa_Initialize/Pa_Terminate must be balanced and called in pairs.
/// </summary>
internal static class PortAudioLifecycle
{
    private static readonly object s_lock = new();
    private static int s_refCount;

    /// <summary>
    /// Increments the reference count and initializes PortAudio on the first call.
    /// Safe to call from multiple threads and multiple times.
    /// </summary>
    public static void EnsureInitialized()
    {
        lock (s_lock)
        {
            if (s_refCount == 0)
            {
                PortAudioSharp.PortAudio.Initialize();
            }
            s_refCount++;
        }
    }

    /// <summary>
    /// Decrements the reference count and terminates PortAudio when it reaches zero.
    /// Each call must match a prior <see cref="EnsureInitialized"/> call.
    /// </summary>
    public static void Release()
    {
        lock (s_lock)
        {
            if (s_refCount <= 0) return;

            s_refCount--;
            if (s_refCount == 0)
            {
                try
                {
                    PortAudioSharp.PortAudio.Terminate();
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[PortAudioLifecycle] Terminate error: {ex.Message}");
                }
            }
        }
    }
}
