using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using OwaWidget.App.Services;
using OwaWidget.Eas.Models;
using OwaWidget.Eas.Recurrence;
using Wpf.Ui.Controls;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;

namespace OwaWidget.App.Views;

public partial class FlyoutWindow : FluentWindow
{
    private static readonly CultureInfo Russian = CultureInfo.GetCultureInfo("ru-RU");

    private readonly AppSettings _settings;

    private IReadOnlyList<EasOccurrence> _occurrences = Array.Empty<EasOccurrence>();
    private int _rangeDays = 1;

    public FlyoutWindow(AppSettings settings)
    {
        _settings = settings;
        InitializeComponent();

        Deactivated += (_, _) => Hide();
        ApplyRangeAppearance();
    }

    public void ShowNearTray()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Right - Width - 12;
        Top = area.Bottom - Height - 12;

        Show();
        Activate();
    }

    public void UpdateStatus(string status)
    {
        StatusText.Text = status;
    }

    public void UpdateMail(IReadOnlyList<EasMessage> messages)
    {
        MailList.ItemsSource = messages
            .Take(60)
            .Select(message => new MailRow(message))
            .ToList();

        var unread = messages.Count(m => !m.IsRead);
        FooterText.Text = unread > 0
            ? $"Непрочитанных: {unread} · нажмите на письмо, чтобы прочитать"
            : "Всё прочитано · нажмите на письмо, чтобы прочитать";
    }

    public void UpdateCalendar(IReadOnlyList<EasOccurrence> occurrences)
    {
        _occurrences = occurrences;
        RenderMeetings();
    }

    private void RenderMeetings()
    {
        var now = DateTimeOffset.Now;
        var limit = _rangeDays == 1
            ? now.Date.AddDays(1).AddTicks(-1) - now.Offset
            : now.AddDays(_rangeDays);

        var rows = _occurrences
            .Where(occurrence => occurrence.End >= now && occurrence.Start <= limit)
            .OrderBy(occurrence => occurrence.Start)
            .Take(20)
            .Select(occurrence => new MeetingRow(occurrence, now, _rangeDays > 1))
            .ToList();

        MeetingsList.ItemsSource = rows;
        NoMeetingsText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        NoMeetingsText.Text = _rangeDays switch
        {
            1 => "На сегодня встреч больше нет",
            3 => "В ближайшие три дня встреч нет",
            _ => "На неделе встреч нет"
        };
    }

    private void OnRangeToday(object sender, RoutedEventArgs e) => SetRange(1);

    private void OnRangeThree(object sender, RoutedEventArgs e) => SetRange(3);

    private void OnRangeWeek(object sender, RoutedEventArgs e) => SetRange(7);

    private void SetRange(int days)
    {
        _rangeDays = days;
        ApplyRangeAppearance();
        RenderMeetings();
    }

    private void ApplyRangeAppearance()
    {
        RangeTodayButton.Appearance = _rangeDays == 1 ? ControlAppearance.Primary : ControlAppearance.Secondary;
        RangeThreeButton.Appearance = _rangeDays == 3 ? ControlAppearance.Primary : ControlAppearance.Secondary;
        RangeWeekButton.Appearance = _rangeDays == 7 ? ControlAppearance.Primary : ControlAppearance.Secondary;
    }

    public event Action<string>? MarkReadRequested;

    private void OnMailClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: MailRow row })
        {
            return;
        }

        row.IsExpanded = !row.IsExpanded;

        if (row.IsExpanded && row.UnreadVisibility == Visibility.Visible)
        {
            row.MarkRead();
            MarkReadRequested?.Invoke(row.ServerId);
        }
    }

    private void OnOpenMail(object sender, RoutedEventArgs e)
    {
        Open($"{_settings.Server}/owa/#path=/mail");
        Hide();
    }

    private void OnJoin(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string link } && link.Length > 0)
        {
            Open(link);
            Hide();
        }
    }

    private static void Open(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            Log.Error($"open url failed", exception);
        }
    }

    private static string FormatTime(DateTimeOffset? value)
    {
        if (value is null)
        {
            return string.Empty;
        }

        var local = value.Value.ToLocalTime();

        if (local.Date == DateTimeOffset.Now.Date)
        {
            return local.ToString("HH:mm");
        }

        return local.Year == DateTimeOffset.Now.Year
            ? local.ToString("dd MMM", Russian)
            : local.ToString("dd.MM.yy");
    }

    private sealed class MailRow : INotifyPropertyChanged
    {
        private static readonly Brush ReadBrush = new SolidColorBrush(Color.FromArgb(12, 128, 128, 128));
        private static readonly Brush UnreadBrush = new SolidColorBrush(Color.FromArgb(28, 120, 170, 255));

        private bool _isExpanded;
        private bool _isRead;

        public MailRow(EasMessage message)
        {
            ServerId = message.ServerId;
            Sender = message.DisplaySender;
            Subject = message.DisplaySubject;
            Preview = message.Preview ?? string.Empty;
            Body = string.IsNullOrWhiteSpace(message.Body) ? message.Preview ?? "(пустое письмо)" : message.Body;
            Time = FormatTime(message.DateReceived);
            _isRead = message.IsRead;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public string ServerId { get; }

        public string Sender { get; }

        public string Subject { get; }

        public string Preview { get; }

        public string Body { get; }

        public string Time { get; }

        public Visibility UnreadVisibility => _isRead ? Visibility.Hidden : Visibility.Visible;

        public FontWeight Weight => _isRead ? FontWeights.Normal : FontWeights.SemiBold;

        public Brush RowBrush => _isRead ? ReadBrush : UnreadBrush;

        public void MarkRead()
        {
            if (_isRead)
            {
                return;
            }

            _isRead = true;
            Raise(nameof(UnreadVisibility));
            Raise(nameof(Weight));
            Raise(nameof(RowBrush));
        }

        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded == value)
                {
                    return;
                }

                _isExpanded = value;
                Raise(nameof(ExpandedVisibility));
                Raise(nameof(CollapsedVisibility));
            }
        }

        public Visibility ExpandedVisibility => _isExpanded ? Visibility.Visible : Visibility.Collapsed;

        public Visibility CollapsedVisibility => _isExpanded ? Visibility.Collapsed : Visibility.Visible;

        private void Raise(string property)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
        }
    }

    private sealed class MeetingRow
    {
        public MeetingRow(EasOccurrence occurrence, DateTimeOffset now, bool showDay)
        {
            Subject = occurrence.Subject;
            Link = occurrence.OnlineMeetingLink ?? string.Empty;
            JoinVisibility = Link.Length == 0 ? Visibility.Collapsed : Visibility.Visible;

            var isNow = occurrence.Start <= now && occurrence.End >= now;
            AccentBrush = isNow
                ? new SolidColorBrush(Color.FromRgb(80, 200, 120))
                : new SolidColorBrush(Color.FromRgb(80, 140, 230));

            Details = BuildDetails(occurrence, now, showDay, isNow);
        }

        public string Subject { get; }

        public string Details { get; }

        public string Link { get; }

        public Visibility JoinVisibility { get; }

        public Brush AccentBrush { get; }

        private static string BuildDetails(EasOccurrence occurrence, DateTimeOffset now, bool showDay, bool isNow)
        {
            var start = occurrence.Start.ToLocalTime();
            var end = occurrence.End.ToLocalTime();
            var span = $"{start:HH:mm}–{end:HH:mm}";

            string when;

            if (isNow)
            {
                when = $"идёт сейчас · до {end:HH:mm}";
            }
            else
            {
                var minutes = (occurrence.Start - now).TotalMinutes;

                if (minutes < 60)
                {
                    when = $"через {Math.Max(1, (int)minutes)} мин · {span}";
                }
                else if (start.Date == now.Date)
                {
                    when = showDay ? $"сегодня {span}" : span;
                }
                else
                {
                    when = $"{start.ToString("ddd dd.MM", Russian)} {span}";
                }
            }

            var location = occurrence.Location?.Trim();

            if (string.IsNullOrEmpty(location))
            {
                return when;
            }

            if (location.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                return when + " · онлайн";
            }

            return when + " · " + (location.Length > 34 ? location[..33] + "…" : location);
        }
    }
}