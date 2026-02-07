using SharpHook;
using SharpHook.Native;

namespace CopilotVoice.Hotkey;

public class HotkeyListener : IDisposable
{
    private SimpleGlobalHook? _hook;
    private readonly HashSet<KeyCode> _targetKeys = new();
    private readonly HashSet<KeyCode> _pressedKeys = new();
    private bool _isActive;
    private bool _disposed;

    public event Action? OnPushToTalkStart;
    public event Action? OnPushToTalkStop;

    public HotkeyListener(string hotkeyCombination)
    {
        SetHotkey(hotkeyCombination);
    }

    public void SetHotkey(string hotkeyCombination)
    {
        _targetKeys.Clear();
        foreach (var part in hotkeyCombination.Split('+', StringSplitOptions.TrimEntries))
        {
            _targetKeys.Add(ParseKeyCode(part));
        }
        
        if (_targetKeys.Count < 2)
            throw new ArgumentException("Hotkey must include at least one modifier and one key");
    }

    public void Start()
    {
        if (_hook != null) return;
        
        _hook = new SimpleGlobalHook();
        _hook.KeyPressed += OnKeyPressed;
        _hook.KeyReleased += OnKeyReleased;
        
        // Run hook on background thread
        Task.Run(() => _hook.Run());
    }

    public void Stop()
    {
        _hook?.Dispose();
        _hook = null;
        _isActive = false;
        _pressedKeys.Clear();
    }

    private void OnKeyPressed(object? sender, KeyboardHookEventArgs e)
    {
        if (!_targetKeys.Contains(e.Data.KeyCode)) return;
        
        _pressedKeys.Add(e.Data.KeyCode);
        
        if (!_isActive && _targetKeys.IsSubsetOf(_pressedKeys))
        {
            _isActive = true;
            OnPushToTalkStart?.Invoke();
        }
    }

    private void OnKeyReleased(object? sender, KeyboardHookEventArgs e)
    {
        if (!_targetKeys.Contains(e.Data.KeyCode)) return;
        
        _pressedKeys.Remove(e.Data.KeyCode);
        
        if (_isActive && !_targetKeys.IsSubsetOf(_pressedKeys))
        {
            _isActive = false;
            OnPushToTalkStop?.Invoke();
        }
    }

    public static KeyCode ParseKeyCode(string key)
    {
        return key.ToLowerInvariant() switch
        {
            "ctrl" or "control" => KeyCode.VcLeftControl,
            "shift" => KeyCode.VcLeftShift,
            "alt" or "option" => KeyCode.VcLeftAlt,
            "meta" or "cmd" or "command" or "win" or "super" => KeyCode.VcLeftMeta,
            "space" => KeyCode.VcSpace,
            "tab" => KeyCode.VcTab,
            "enter" or "return" => KeyCode.VcEnter,
            "backspace" => KeyCode.VcBackspace,
            "escape" or "esc" => KeyCode.VcEscape,
            // Letters
            var k when k.Length == 1 && char.IsLetter(k[0]) =>
                Enum.Parse<KeyCode>($"Vc{char.ToUpper(k[0])}"),
            // Function keys
            var k when k.StartsWith("f") && int.TryParse(k[1..], out var n) && n >= 1 && n <= 12 =>
                Enum.Parse<KeyCode>($"VcF{n}"),
            // Numbers
            var k when k.Length == 1 && char.IsDigit(k[0]) =>
                Enum.Parse<KeyCode>($"Vc{k}"),
            _ => throw new ArgumentException($"Unknown key: {key}")
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        GC.SuppressFinalize(this);
    }
}
