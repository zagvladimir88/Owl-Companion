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
    private readonly Icon _baseIcon;

    private Icon? _currentIcon;
    private IntPtr _currentHandle;

    public TrayIconService()
    {
        _baseIcon = LoadBaseIcon();

        _pauseItem = new MenuItem { Header = "Приостановить уведомления", IsCheckable = true };
        _pauseItem.Click += (_, _) => PauseToggled?.Invoke(_pauseItem.IsChecked);

        _menu = new ContextMenu();
        _menu.Items.Add(Item("Показать панель", () => PanelRequested?.Invoke()));
        _menu.Items.Add(Item("Открыть почту в браузере", () => WebMailRequested?.Invoke()));
        _menu.Items.Add(new Separator());
        _menu.Items.Add(Item("Синхронизировать сейчас", () => RefreshRequested?.Invoke()));
        _menu.Items.Add(Item("Проверить уведомление", () => TestNotificationRequested?.Invoke()));
        _menu.Items.Add(_pauseItem);
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

    public void Update(int unreadCount, string status)
    {
        var previous = _currentIcon;
        var previousHandle = _currentHandle;

        var icon = BuildIcon(unreadCount);

        _notifyIcon.Icon = icon;
        _currentIcon = icon;

        var tooltip = unreadCount > 0
            ? $"OWA Widget · непрочитанных: {unreadCount}\n{status}"
            : $"OWA Widget\n{status}";

        _notifyIcon.Text = tooltip.Length > 62 ? tooltip[..62] : tooltip;

        previous?.Dispose();

        if (previousHandle != IntPtr.Zero && previousHandle != _currentHandle)
        {
            DestroyIcon(previousHandle);
        }
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

    private Icon BuildIcon(int unreadCount)
    {
        const int size = 32;

        using var bitmap = new Bitmap(size, size);

        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            graphics.Clear(Color.Transparent);
            graphics.DrawIcon(_baseIcon, new Rectangle(0, 0, size, size));

            if (unreadCount > 0)
            {
                var label = unreadCount > 99 ? "99+" : unreadCount.ToString();
                var diameter = label.Length > 2 ? 21 : 18;
                var badge = new Rectangle(size - diameter - 1, size - diameter - 1, diameter, diameter);

                using var badgeBrush = new SolidBrush(Color.FromArgb(255, 214, 44, 44));
                using var borderPen = new Pen(Color.FromArgb(255, 22, 22, 22), 1.6f);
                graphics.FillEllipse(badgeBrush, badge);
                graphics.DrawEllipse(borderPen, badge);

                using var font = new Font("Segoe UI", label.Length > 2 ? 8.5f : 11f, FontStyle.Bold, GraphicsUnit.Pixel);
                using var textBrush = new SolidBrush(Color.White);
                using var format = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center
                };

                graphics.DrawString(label, font, textBrush, badge, format);
            }
        }

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