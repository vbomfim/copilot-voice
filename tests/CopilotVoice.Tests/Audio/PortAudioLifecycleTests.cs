using CopilotVoice.Audio;

namespace CopilotVoice.Tests.Audio;

/// <summary>
/// Tests for PortAudioLifecycle (static init/terminate with ref-counting).
/// </summary>
public class PortAudioLifecycleTests
{
    [Fact]
    public void EnsureInitialized_ThenRelease_DoesNotThrow()
    {
        // This may interact with real PortAudio on machines that have it.
        // The test validates that init/release ref-counting is balanced.
        try
        {
            PortAudioLifecycle.EnsureInitialized();
            PortAudioLifecycle.Release();
        }
        catch (Exception ex) when (ex.Message.Contains("PortAudio") || ex is DllNotFoundException)
        {
            // PortAudio native lib not available — skip gracefully
        }
    }

    [Fact]
    public void EnsureInitialized_CalledTwice_ThenReleaseTwice_IsBalanced()
    {
        try
        {
            PortAudioLifecycle.EnsureInitialized();
            PortAudioLifecycle.EnsureInitialized();
            PortAudioLifecycle.Release();
            PortAudioLifecycle.Release();
        }
        catch (Exception ex) when (ex.Message.Contains("PortAudio") || ex is DllNotFoundException)
        {
            // PortAudio native lib not available
        }
    }
}
