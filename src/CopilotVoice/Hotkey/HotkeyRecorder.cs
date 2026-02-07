using SharpHook;
using SharpHook.Native;

namespace CopilotVoice.Hotkey;

public class HotkeyRecorder : IDisposable
{
    private SimpleGlobalHook? _hook;
    private readonly HashSet<KeyCode> _currentKeys = new();
    private bool _capturing;
    private bool _disposed;

    public event Action<string>? OnHotkeyCaptured;

    public void StartCapture()
    {
        StopCapture();
        _capturing = true;
        _currentKeys.Clear();
        
        _hook = new SimpleGlobalHook();
        _hook.KeyPressed += OnCaptureKeyPressed;
        _hook.KeyReleased += OnCaptureKeyReleased;
        
        Task.Run(() => _hook.Run());
    }

    public void CancelCapture()
    {
        StopCapture();
    }

    private void StopCapture()
    {
        _capturing = false;
        _hook?.Dispose();
        _hook = null;
        _currentKeys.Clear();
    }

    private void OnCaptureKeyPressed(object? sender, KeyboardHookEventArgs e)
    {
        if (!_capturing) return;
        _currentKeys.Add(e.Data.KeyCode);
    }

    private void OnCaptureKeyReleased(object? sender, KeyboardHookEventArgs e)
    {
        if (!_capturing || _currentKeys.Count < 2) return;

        // Must have at least one modifier
        bool hasModifier = _currentKeys.Any(k => IsModifier(k));
        bool hasNonModifier = _currentKeys.Any(k => !IsModifier(k));

        if (hasModifier && hasNonModifier)
        {
            var combo = string.Join("+", _currentKeys
                .OrderBy(k => IsModifier(k) ? 0 : 1)
                .Select(FormatKeyCode));
            
            StopCapture();
            OnHotkeyCaptured?.Invoke(combo);
        }
    }

    private static bool IsModifier(KeyCode key) => key is
        KeyCode.VcLeftControl or KeyCode.VcRightControl or
        KeyCode.VcLeftShift or KeyCode.VcRightShift or
        KeyCode.VcLeftAlt or KeyCode.VcRightAlt or
        KeyCode.VcLeftMeta or KeyCode.VcRightMeta;

    private static string FormatKeyCode(KeyCode key) => key switch
    {
        KeyCode.VcLeftControl or KeyCode.VcRightControl => "Ctrl",
        KeyCode.VcLeftShift or KeyCode.VcRightShift => "Shift",
        KeyCode.VcLeftAlt or KeyCode.VcRightAlt => "Alt",
        KeyCode.VcLeftMeta or KeyCode.VcRightMeta => "Meta",
        _ => key.ToString().Replace("Vc", "")
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopCapture();
        GC.SuppressFinalize(this);
    }
}
