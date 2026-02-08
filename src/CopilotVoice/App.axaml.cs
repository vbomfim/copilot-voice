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
    private NativeMenuItem? _pomodoroItem;
    private NativeMenuItem? _sessionsItem;
    private NativeMenuItem? _lockToggleItem;

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

            var trayIcon = new TrayIcon
            {
                ToolTipText = "Copilot Voice",
                IsVisible = true,
                Menu = BuildTrayMenu(desktop)
            };

            trayIcon.Clicked += (_, _) =>
            {
                if (_avatarWindow.IsVisible)
                    _avatarWindow.Hide();
                else
                    _avatarWindow.Show();
            };

            _services.OnStateChanged += state =>
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    trayIcon.ToolTipText = $"Copilot Voice — {state}");

            _services.OnSpeechBubble += (text, _) =>
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (_lastTranscriptionItem != null && !string.IsNullOrEmpty(text))
                        _lastTranscriptionItem.Header = $"🔊 \"{Truncate(text, 30)}\"";
                });

            _services.OnTimerTick += (phase, remaining) =>
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (_pomodoroItem != null)
                    {
                        if (phase == null)
                            _pomodoroItem.Header = "🍅 Pomodoro: Off";
                        else
                        {
                            var icon = phase == "Work" ? "🔨" : "☕";
                            _pomodoroItem.Header = $"🍅 {icon} {remaining:mm\\:ss} {phase.ToUpper()}";
                        }
                    }
                });

            _ = _services.StartAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private NativeMenu BuildTrayMenu(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var menu = new NativeMenu();

        // Title
        var titleItem = new NativeMenuItem("🎤🤖 Copilot Voice") { IsEnabled = false };
        menu.Add(titleItem);
        menu.Add(new NativeMenuItemSeparator());

        // Sessions section
        if (_services != null)
        {
            var sessionsHeader = new NativeMenuItem("Active Sessions:") { IsEnabled = false };
            menu.Add(sessionsHeader);

            _lockToggleItem = new NativeMenuItem("🔓 Auto-select");
            _lockToggleItem.Click += (_, _) =>
            {
                _services?.ToggleSessionLock();
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (_lockToggleItem != null)
                        _lockToggleItem.Header = _services?.IsSessionLocked == true
                            ? "🔒 Locked" : "🔓 Auto-select";
                });
            };
            menu.Add(_lockToggleItem);

            _sessionsItem = new NativeMenuItem("Sessions") { Menu = new NativeMenu() };
            _services.OnSessionsRefreshed += sessions =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    _sessionsItem.Menu!.Items.Clear();
                    if (sessions.Count == 0)
                    {
                        _sessionsItem.Menu.Items.Add(
                            new NativeMenuItem("No sessions found") { IsEnabled = false });
                        return;
                    }
                    foreach (var s in sessions)
                    {
                        var prefix = s.IsFocused ? "● " : "○ ";
                        var item = new NativeMenuItem($"{prefix}{s.Label}");
                        var session = s;
                        item.Click += (_, _) => _services?.SelectSession(session);
                        _sessionsItem.Menu.Items.Add(item);
                    }
                });
            };
            menu.Add(_sessionsItem);
            menu.Add(new NativeMenuItemSeparator());
        }

        // Info section
        _hotkeyItem = new NativeMenuItem($"⌨️  Hotkey: {_services?.Config.Hotkey ?? "Ctrl+Space"}") { IsEnabled = false };
        menu.Add(_hotkeyItem);

        _lastTranscriptionItem = new NativeMenuItem("🔊 (no transcription yet)") { IsEnabled = false };
        menu.Add(_lastTranscriptionItem);

        _pomodoroItem = new NativeMenuItem("🍅 Pomodoro: Off") { IsEnabled = false };
        menu.Add(_pomodoroItem);

        menu.Add(new NativeMenuItemSeparator());

        // Actions
        var showItem = new NativeMenuItem("👁️  Show Avatar");
        showItem.Click += (_, _) => _avatarWindow?.Show();
        menu.Add(showItem);

        var hideItem = new NativeMenuItem("🙈 Hide Avatar");
        hideItem.Click += (_, _) => _avatarWindow?.Hide();
        menu.Add(hideItem);

        var refreshItem = new NativeMenuItem("🔄 Refresh Sessions");
        refreshItem.Click += (_, _) => _services?.RefreshSessions();
        menu.Add(refreshItem);

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
