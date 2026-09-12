using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using FontStyle = System.Drawing.FontStyle;
using MouseButtons = System.Windows.Forms.MouseButtons;
using NotifyIcon = System.Windows.Forms.NotifyIcon;
using Size = System.Drawing.Size;

namespace OwaWidget.App.Services;

public sealed class TrayIconService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenu _menu;
    private readonly MenuItem _pauseItem;
    private readonly MenuItem _startupItem;
    private readonly Icon _baseIcon;

    private Icon? _currentIcon;
    private IntPtr _currentHandle;
    private string? _renderKey;

    public TrayIconService()
    {
        _baseIcon = LoadBaseIcon();

        _pauseItem = new MenuItem { Header = "Приостановить уведомления", IsCheckable = true };
        _pauseItem.Click += (_, _) => PauseToggled?.Invoke(_pauseItem.IsChecked);

        _startupItem = new MenuItem { Header = "Запускать при входе", IsCheckable = true };
        _startupItem.Click += (_, _) => StartWithWindowsToggled?.Invoke(_startupItem.IsChecked);

        _menu = new ContextMenu();
        _menu.Items.Add(Item("Показать панель", () => PanelRequested?.Invoke()));
        _menu.Items.Add(Item("Открыть почту в браузере", () => WebMailRequested?.Invoke()));
        _menu.Items.Add(new Separator());
        _menu.Items.Add(Item("Синхронизировать сейчас", () => RefreshRequested?.Invoke()));
        _menu.Items.Add(Item("Проверить уведомление", () => TestNotificationRequested?.Invoke()));
        _menu.Items.Add(_pauseItem);
        _menu.Items.Add(new Separator());
        _menu.Items.Add(_startupItem);
        _menu.Items.Add(Item("Сбросить кэш", () => ResetCacheRequested?.Invoke()));
        _menu.Items.Add(new Separator());
        _menu.Items.Add(Item("Учётные данные…", () => SettingsRequested?.Invoke()));
        _menu.Items.Add(Item("Выход", () => ExitRequested?.Invoke()));

        _notifyIcon = new NotifyIcon
        {
            Visible = true,
            Text = "OWA Widget"
        };

        _notifyIcon.MouseClick += (_, args) =>
        {
            if (args.Button == MouseButtons.Left)
            {
                PanelRequested?.Invoke();
            }
            else if (args.Button == MouseButtons.Right)
            {
                ShowMenu();
            }
        };

        Update(0, "Запуск…");
    }

    public event Action? PanelRequested;

    public event Action? WebMailRequested;

    public event Action? RefreshRequested;

    public event Action? SettingsRequested;

    public event Action? TestNotificationRequested;

    public event Action? ExitRequested;

    public event Action<bool>? PauseToggled;

    public event Action<bool>? StartWithWindowsToggled;

    public event Action? ResetCacheRequested;

    public bool StartWithWindows
    {
        get => _startupItem.IsChecked;
        set => _startupItem.IsChecked = value;
    }

    public void Update(int unreadCount, string status)
    {
        Update(new TrayPresentation(TrayState.Free, 0, $"Owl\n{status}"));
    }

    public void Update(TrayPresentation presentation)
    {
        var key = $"{presentation.State}|{Math.Round(presentation.Fill * 12)}";

        if (key != _renderKey)
        {
            _renderKey = key;

            var previous = _currentIcon;
            var previousHandle = _currentHandle;

            var icon = BuildIcon(presentation);
            _notifyIcon.Icon = icon;
            _currentIcon = icon;

            previous?.Dispose();

            if (previousHandle != IntPtr.Zero && previousHandle != _currentHandle)
            {
                DestroyIcon(previousHandle);
            }
        }

        _notifyIcon.Text = presentation.Tooltip.Length > 62
            ? presentation.Tooltip[..62]
            : presentation.Tooltip;
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _baseIcon.Dispose();
        _currentIcon?.Dispose();

        if (_currentHandle != IntPtr.Zero)
        {
            DestroyIcon(_currentHandle);
            _currentHandle = IntPtr.Zero;
        }
    }

    private void ShowMenu()
    {
        _menu.Placement = PlacementMode.MousePoint;
        _menu.IsOpen = true;

        if (PresentationSource.FromVisual(_menu) is HwndSource source)
        {
            SetForegroundWindow(source.Handle);
        }
    }

    private static MenuItem Item(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }


    private Icon BuildIcon(TrayPresentation presentation)
    {
        var size = Math.Max(16, System.Windows.Forms.SystemInformation.SmallIconSize.Width);

        using var bitmap = TrayIconPainter.Paint(presentation, size);

        _currentHandle = bitmap.GetHicon();
        return Icon.FromHandle(_currentHandle);
    }

    private static Icon LoadBaseIcon()
    {
        var uri = new Uri("pack://application:,,,/Assets/owawidget.ico", UriKind.Absolute);
        using var stream = System.Windows.Application.GetResourceStream(uri)!.Stream;
        return new Icon(stream, new Size(32, 32));
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr handle);
}