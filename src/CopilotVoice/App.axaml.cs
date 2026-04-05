using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using CopilotVoice.Views;

namespace CopilotVoice;

public class App : Application
{
    private AvatarWindow? _avatarWindow;
    private AppServices? _services;
    private NativeMenuItem? _lastTranscriptionItem;
    private NativeMenuItem? _hotkeyItem;
    private NativeMenuItem? _micStatusItem;
    private NativeMenuItem? _pomodoroItem;
    private NativeMenuItem? _voiceItem;
    private NativeMenuItem? _muteItem;
    private NativeMenuItem? _topmostItem;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            _services = new AppServices();

            _avatarWindow = new AvatarWindow();
            _avatarWindow.SetServices(_services);
            _avatarWindow.Show();

            WindowIcon? trayIconImage = null;
            try
            {
                var iconPath = System.IO.Path.Combine(
                    AppContext.BaseDirectory, "Assets", "tray-icon.png");
                Console.WriteLine($"[copilot-voice] Tray icon path: {iconPath} (exists: {System.IO.File.Exists(iconPath)})");
                if (System.IO.File.Exists(iconPath))
                    trayIconImage = new WindowIcon(iconPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[copilot-voice] Tray icon load error: {ex.Message}");
            }

            var trayIcon = new TrayIcon
            {
                ToolTipText = "Copilot Voice",
                IsVisible = true,
                Menu = BuildTrayMenu(desktop)
            };
            if (trayIconImage != null)
            {
                trayIcon.Icon = trayIconImage;
                Console.WriteLine("[copilot-voice] Tray icon set");
            }
            else
            {
                Console.WriteLine("[copilot-voice] Tray icon NOT set (null)");
            }

            _services.OnStateChanged += state =>
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    trayIcon.ToolTipText = $"Copilot Voice — {state}");

            _services.OnSpeechBubble += (text, _) =>
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (_lastTranscriptionItem != null && !string.IsNullOrEmpty(text))
                        _lastTranscriptionItem.Header = $"🔊 \"{Truncate(text, 30)}\"";
                });

            _services.OnMicAvailabilityChanged += available =>
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (_micStatusItem != null)
                        _micStatusItem.Header = available ? "🎤 Mic: OK" : "🎤✕ No microphone";
                });

            _ = _services.StartAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void RebuildVoiceMenu()
    {
        if (_voiceItem?.Menu == null || _services == null) return;
        _voiceItem.Menu.Items.Clear();

        var currentVoice = _services.Config.VoiceName;
        foreach (var (name, label) in Config.AppConfig.AvailableVoices)
        {
            var prefix = name == currentVoice ? "✓ " : "   ";
            var item = new NativeMenuItem($"{prefix}{label}");
            var voiceName = name;
            item.Click += (_, _) => _services.ChangeVoice(voiceName);
            _voiceItem.Menu.Items.Add(item);
        }

        var currentLabel = Config.AppConfig.AvailableVoices
            .FirstOrDefault(v => v.Name == currentVoice).Label ?? currentVoice;
        _voiceItem.Header = $"🔊 Voice: {currentLabel}";
    }

    private NativeMenu BuildTrayMenu(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var menu = new NativeMenu();

        // Title
        var titleItem = new NativeMenuItem("🎤🤖 Copilot Voice") { IsEnabled = false };
        menu.Add(titleItem);
        menu.Add(new NativeMenuItemSeparator());

        if (_services != null)
        {
            // Voice submenu
            _voiceItem = new NativeMenuItem($"🔊 Voice: {_services.Config.VoiceName}")
            {
                Menu = new NativeMenu()
            };
            RebuildVoiceMenu();
            _services.OnVoiceChanged += _ =>
                Avalonia.Threading.Dispatcher.UIThread.Post(RebuildVoiceMenu);
            menu.Add(_voiceItem);

            _muteItem = new NativeMenuItem("🔊 Mute: Off");
            _muteItem.Click += (_, _) =>
            {
                _services?.ToggleMute();
            };
            _services.OnMuteChanged += muted =>
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (_muteItem != null)
                        _muteItem.Header = muted ? "🔇 Mute: On" : "🔊 Mute: Off";
                });
            menu.Add(_muteItem);
            menu.Add(new NativeMenuItemSeparator());
        }

        // Info section
        _hotkeyItem = new NativeMenuItem($"⌨️  Hotkey: {_services?.Config.Hotkey ?? "Alt+Space"}") { IsEnabled = false };
        menu.Add(_hotkeyItem);

        _micStatusItem = new NativeMenuItem("🎤 Mic: OK") { IsEnabled = false };
        menu.Add(_micStatusItem);

        _lastTranscriptionItem = new NativeMenuItem("🔊 (no transcription yet)") { IsEnabled = false };
        menu.Add(_lastTranscriptionItem);

        _pomodoroItem = new NativeMenuItem("🍅 Pomodoro: Off") { IsEnabled = false };
        menu.Add(_pomodoroItem);

        menu.Add(new NativeMenuItemSeparator());

        // Actions
        var toggleAvatarItem = new NativeMenuItem("👁️  Hide Avatar");
        toggleAvatarItem.Click += (_, _) =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    if (_avatarWindow?.IsVisible == true)
                    {
                        _avatarWindow.Hide();
                        toggleAvatarItem.Header = "👁️  Show Avatar";
                    }
                    else
                    {
                        _avatarWindow?.Show();
                        toggleAvatarItem.Header = "👁️  Hide Avatar";
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[copilot-voice] Toggle error: {ex.Message}");
                }
            });
        };
        menu.Add(toggleAvatarItem);

        _topmostItem = new NativeMenuItem("📌 Always on Top ✓");
        _topmostItem.Click += (_, _) =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    if (_avatarWindow != null)
                    {
                        _avatarWindow.SetTopmost(!_avatarWindow.Topmost);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[copilot-voice] Topmost error: {ex.Message}");
                }
            });
        };
        menu.Add(_topmostItem);

        // Sync tray menu when window pin button changes topmost
        if (_avatarWindow != null)
        {
            _avatarWindow.OnTopmostChanged += (on) =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (_topmostItem != null)
                        _topmostItem.Header = on ? "📌 Always on Top ✓" : "📌 Always on Top";
                });
            };
            _avatarWindow.OnVisibilityChanged += (visible) =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    toggleAvatarItem.Header = visible ? "👁️  Hide Avatar" : "👁️  Show Avatar";
                });
            };
        }

        var resetPosItem = new NativeMenuItem("📍 Reset Position");
        resetPosItem.Click += (_, _) =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                try { _avatarWindow?.ResetPosition(); }
                catch (Exception ex) { Console.WriteLine($"[copilot-voice] Reset pos error: {ex.Message}"); }
            });
        };
        menu.Add(resetPosItem);

        menu.Add(new NativeMenuItemSeparator());

        var quitItem = new NativeMenuItem("❌ Quit");
        quitItem.Click += (_, _) =>
        {
            _services?.Dispose();
            desktop.Shutdown();
        };
        menu.Add(quitItem);

        return menu;
    }

    private static string Truncate(string text, int maxLen)
        => text.Length <= maxLen ? text : text[..maxLen] + "…";
}
