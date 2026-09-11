using System.Diagnostics;
using System.Windows;
using Microsoft.Toolkit.Uwp.Notifications;
using OwaWidget.App.Services;
using OwaWidget.App.Views;
using OwaWidget.Eas.Models;
using OwaWidget.Eas.Recurrence;
using Wpf.Ui.Appearance;
using Application = System.Windows.Application;

namespace OwaWidget.App;

public partial class App : Application
{
    private const string SingleInstanceName = @"Local\OwaWidget.SingleInstance";

    private Mutex? _instanceLock;
    private AppSettings _settings = new();
    private AppState _state = new();
    private NotificationService _notifications = null!;
    private ReminderScheduler _reminders = null!;
    private TrayIconService _tray = null!;

    private EasSession? _session;
    private CancellationTokenSource? _cancellation;
    private FlyoutWindow? _flyout;
    private bool _paused;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _instanceLock = new Mutex(true, SingleInstanceName, out var createdNew);
        if (!createdNew)
        {
            Log.Info("another instance is already running, exiting");
            _instanceLock.Dispose();
            _instanceLock = null;
            Shutdown();
            return;
        }

        ShortcutInstaller.SetProcessIdentity();

        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error("unhandled exception", args.Exception);
            _notifications?.ShowError("Сбой виджета", args.Exception.Message);
            args.Handled = true;
        };

        _settings = AppStorage.LoadSettings();
        _state = AppStorage.LoadState();

        ApplicationThemeManager.Apply(ApplicationTheme.Dark);

        if (CredentialStore.TryLoad(_settings.Server) is null && !ShowLogin())
        {
            Shutdown();
            return;
        }

        _notifications = new NotificationService(_settings);

        _reminders = new ReminderScheduler(_settings);
        _reminders.ReminderDue += OnReminderDue;
        _reminders.Start();

        _tray = new TrayIconService();
        _tray.PanelRequested += TogglePanel;
        _tray.WebMailRequested += OpenWebMail;
        _tray.RefreshRequested += StartSession;
        _tray.SettingsRequested += ReconfigureCredentials;
        _tray.TestNotificationRequested += () =>
        {
            Log.Info("test notification requested");
            _notifications.ShowError("Уведомления работают", "Так будет выглядеть сообщение о новом письме.");
        };
        _tray.ExitRequested += Shutdown;
        _tray.PauseToggled += paused => _paused = paused;
        _tray.StartWithWindows = _settings.StartWithWindows;
        _tray.StartWithWindowsToggled += enabled =>
        {
            _settings.StartWithWindows = enabled;
            AppStorage.Save(_settings);
            ShortcutInstaller.SetStartupShortcut(enabled);
        };
        _tray.ResetCacheRequested += () =>
        {
            _session?.ClearCache();
            StartSession();
        };

        ToastNotificationManagerCompat.OnActivated += OnToastActivated;
        ShortcutInstaller.EnsureShortcut();
        ShortcutInstaller.SetStartupShortcut(_settings.StartWithWindows);

        Log.Info($"started, exe={Environment.ProcessPath}");

        StartSession();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _cancellation?.Cancel();
        _session?.Dispose();
        _reminders?.Dispose();
        _tray?.Dispose();

        if (_instanceLock is not null)
        {
            ToastNotificationManagerCompat.Uninstall();
            _instanceLock.ReleaseMutex();
            _instanceLock.Dispose();
            _instanceLock = null;
        }

        base.OnExit(e);
    }

    private void StartSession()
    {
        _cancellation?.Cancel();
        _session?.Dispose();

        _cancellation = new CancellationTokenSource();
        _session = new EasSession(_settings, _state);
        _session.MailArrived += OnMailArrived;
        _session.MailChanged += OnMailChanged;
        _session.CalendarChanged += OnCalendarChanged;
        _session.StatusChanged += OnStatusChanged;
        _session.PrimeFromCache();

        var session = _session;
        var token = _cancellation.Token;

        _ = Task.Run(() => session.RunAsync(token), token);
    }

    private void OnMailArrived(IReadOnlyList<EasMessage> messages)
    {
        Dispatcher.Invoke(() =>
        {
            if (!_paused)
            {
                _notifications.ShowMail(messages);
            }
        });
    }

    private void OnMailChanged()
    {
        Dispatcher.Invoke(() =>
        {
            _tray.Update(_session?.UnreadCount ?? 0, "Подключено");
            _flyout?.UpdateMail(_session?.Messages ?? Array.Empty<EasMessage>());
        });
    }

    private void OnCalendarChanged(IReadOnlyList<EasOccurrence> occurrences)
    {
        Dispatcher.Invoke(() =>
        {
            _reminders.Update(occurrences);
            _flyout?.UpdateCalendar(occurrences);
        });
    }

    private void OnStatusChanged(SessionStatus status, string message)
    {
        Dispatcher.Invoke(() =>
        {
            _tray.Update(_session?.UnreadCount ?? 0, message);
            _flyout?.UpdateStatus(message);

            if (status == SessionStatus.AuthenticationFailed)
            {
                _notifications.ShowError(
                    "Не удалось войти в почту",
                    "Пароль не подошёл. Откройте «Учётные данные» в меню виджета и введите его заново.");
            }
        });
    }

    private void OnReminderDue(EasOccurrence occurrence, int minutes)
    {
        Dispatcher.Invoke(() =>
        {
            if (!_paused)
            {
                _notifications.ShowReminder(occurrence, minutes);
            }
        });
    }

    private void TogglePanel()
    {
        if (_flyout is { IsVisible: true })
        {
            _flyout.Hide();
            return;
        }

        if (_flyout is null)
        {
            _flyout = new FlyoutWindow(_settings);
            _flyout.MarkReadRequested += serverId =>
            {
                var session = _session;
                if (session is not null)
                {
                    _ = session.MarkReadAsync(serverId);
                }
            };
        }

        _flyout.UpdateMail(_session?.Messages ?? Array.Empty<EasMessage>());
        _flyout.UpdateCalendar(_session?.UpcomingOccurrences(30) ?? Array.Empty<EasOccurrence>());
        _flyout.ShowNearTray();
    }

    private void OpenWebMail()
    {
        OpenUrl($"{_settings.Server}/owa/#path=/mail");
    }

    private void ReconfigureCredentials()
    {
        if (ShowLogin())
        {
            StartSession();
        }
    }

    private bool ShowLogin()
    {
        var window = new LoginWindow(_settings, _state);
        return window.ShowDialog() == true;
    }

    private void OnToastActivated(ToastNotificationActivatedEventArgsCompat e)
    {
        Dispatcher.Invoke(() =>
        {
            var arguments = ToastArguments.Parse(e.Argument);

            if (!arguments.TryGetValue("action", out var action))
            {
                return;
            }

            switch (action)
            {
                case "openMail":
                    OpenWebMail();
                    break;

                case "openCalendar":
                    OpenUrl($"{_settings.Server}/owa/#path=/calendar");
                    break;

                case "markRead":
                    if (arguments.TryGetValue("id", out var messageId))
                    {
                        var session = _session;
                        if (session is not null)
                        {
                            _ = session.MarkReadAsync(messageId);
                        }
                    }

                    break;

                case "settings":
                    ReconfigureCredentials();
                    break;
            }
        });
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception)
        {
        }
    }
}