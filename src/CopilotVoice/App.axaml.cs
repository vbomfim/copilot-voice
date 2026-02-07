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

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // No main window — tray icon app
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            _services = new AppServices();

            // Create and show the avatar window
            _avatarWindow = new AvatarWindow();
            _avatarWindow.SetServices(_services);
            _avatarWindow.Show();

            // Setup tray icon
            var trayIcon = new TrayIcon
            {
                ToolTipText = "Copilot Voice",
                IsVisible = true,
                Menu = BuildTrayMenu(desktop)
            };

            // Set tray icon text (emoji-style isn't supported, use a simple icon)
            trayIcon.Clicked += (_, _) =>
            {
                if (_avatarWindow.IsVisible)
                    _avatarWindow.Hide();
                else
                    _avatarWindow.Show();
            };

            // Update tray tooltip based on state
            _services.OnStateChanged += state =>
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    trayIcon.ToolTipText = $"Copilot Voice — {state}");

            // Start all services
            _ = _services.StartAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private NativeMenu BuildTrayMenu(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var menu = new NativeMenu();

        var showItem = new NativeMenuItem("Show Avatar");
        showItem.Click += (_, _) => _avatarWindow?.Show();
        menu.Add(showItem);

        var hideItem = new NativeMenuItem("Hide Avatar");
        hideItem.Click += (_, _) => _avatarWindow?.Hide();
        menu.Add(hideItem);

        menu.Add(new NativeMenuItemSeparator());

        if (_services != null)
        {
            var sessionsItem = new NativeMenuItem("Sessions") { Menu = new NativeMenu() };
            _services.OnSessionsRefreshed += sessions =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    sessionsItem.Menu!.Items.Clear();
                    foreach (var s in sessions)
                    {
                        var item = new NativeMenuItem(
                            $"{(s.IsFocused ? "● " : "○ ")}{s.Label}");
                        sessionsItem.Menu.Items.Add(item);
                    }
                });
            };
            menu.Add(sessionsItem);
            menu.Add(new NativeMenuItemSeparator());
        }

        var quitItem = new NativeMenuItem("Quit");
        quitItem.Click += (_, _) =>
        {
            _services?.Dispose();
            desktop.Shutdown();
        };
        menu.Add(quitItem);

        return menu;
    }
}
