using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using OwaWidget.App.Services;
using OwaWidget.Eas.Models;
using OwaWidget.Eas.Recurrence;
using Wpf.Ui.Controls;
using Border = System.Windows.Controls.Border;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace OwaWidget.App.Views;

public partial class FlyoutWindow : FluentWindow
{
    private static readonly CultureInfo Russian = CultureInfo.GetCultureInfo("ru-RU");

    private static readonly Brush TabActiveBackground = Frozen(Color.FromArgb(18, 255, 255, 255));
    private static readonly Brush TabActiveText = Frozen(Color.FromRgb(0xEA, 0xF2, 0xFF));
    private static readonly Brush TabIdleText = Frozen(Color.FromRgb(0x85, 0x8C, 0x9C));
    private static readonly Brush LiveDot = Frozen(Color.FromRgb(0x65, 0xD6, 0xA0));
    private static readonly Brush WarnDot = Frozen(Color.FromRgb(0xE0, 0xA0, 0x20));

    private readonly AppSettings _settings;
    private readonly DispatcherTimer _ticker;

    private IReadOnlyList<EasOccurrence> _occurrences = Array.Empty<EasOccurrence>();
    private IReadOnlyList<EasMessage> _messages = Array.Empty<EasMessage>();
    private string _filter = "all";
    private string _query = string.Empty;
    private EasOccurrence? _nextUp;
    private EasOccurrence? _detail;
    private bool _attendeesExpanded;

    public FlyoutWindow(AppSettings settings)
    {
        _settings = settings;
        InitializeComponent();

        _ticker = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _ticker.Tick += (_, _) => UpdateNextUp();

        Deactivated += (_, _) => Hide();
        IsVisibleChanged += OnVisibleChanged;
        PreviewKeyDown += OnPreviewKeyDown;

        ApplyFilterAppearance();
    }

    public event Action<string>? MarkReadRequested;

    public event Action<EasOccurrence, MeetingReply>? RespondRequested;

    public void ShowNearTray()
    {
        CloseDetail();
        HideSearch();

        var area = SystemParameters.WorkArea;
        Left = area.Right - Width - 12;
        Top = area.Bottom - Height - 12;

        Show();
        Activate();
    }

    public void UpdateStatus(string status)
    {
        StatusText.Text = status;

        var healthy = status.Contains("Подключено", StringComparison.OrdinalIgnoreCase) ||
                      status.Contains("Синхрон", StringComparison.OrdinalIgnoreCase);
        StatusDot.Fill = healthy ? LiveDot : WarnDot;
        StatusDot.ToolTip = status;
    }

    public void UpdateMail(IReadOnlyList<EasMessage> messages)
    {
        _messages = messages;
        Rebuild();
    }

    public void UpdateCalendar(IReadOnlyList<EasOccurrence> occurrences)
    {
        _occurrences = occurrences;
        Rebuild();
    }

    private void Rebuild()
    {
        var now = DateTimeOffset.Now;

        var meetings = _occurrences
            .Where(o => o.End >= now.AddHours(-1))
            .OrderBy(o => o.Start)
            .Select(o => StreamEvent.FromMeeting(o, now))
            .ToList();

        var mail = _messages
            .OrderByDescending(m => m.DateReceived ?? DateTimeOffset.MinValue)
            .Take(80)
            .Select(m => StreamEvent.FromMail(m, now))
            .ToList();

        CountMeetings.Text = meetings.Count.ToString();
        CountMail.Text = mail.Count.ToString();
        CountUnread.Text = mail.Count(m => m.IsUnread).ToString();
        CountAll.Text = (meetings.Count + mail.Count).ToString();

        var selected = _filter switch
        {
            "meetings" => meetings,
            "mail" => mail,
            "unread" => mail.Where(m => m.IsUnread).ToList(),
            _ => meetings.Concat(mail).ToList()
        };

        var words = Tokenize(_query);
        if (words.Length > 0)
        {
            selected = selected.Where(e => Matches(e, words)).ToList();
        }

        var items = new List<object>();
        var today = now.Date;

        var groups = selected
            .GroupBy(e => e.Day)
            .OrderBy(g => g.Key.Date < today ? 1 : 0)
            .ThenBy(g => g.Key.Date < today ? today - g.Key.Date : g.Key.Date - today);

        foreach (var group in groups)
        {
            var ordered = group
                .OrderBy(e => e.IsMail ? 1 : 0)
                .ThenBy(e => e.IsMail ? DateTimeOffset.MaxValue - e.Sort : e.Sort - DateTimeOffset.MinValue)
                .ToList();

            items.Add(BuildSection(group.Key, ordered, now));
            items.AddRange(ordered);
        }

        StreamList.ItemsSource = items;

        var empty = items.Count == 0;
        EmptyText.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Text = words.Length > 0
            ? "Ничего не найдено\nПопробуйте изменить запрос"
            : _filter switch
            {
                "meetings" => "Встреч больше нет",
                "mail" => "Писем нет",
                "unread" => "Всё прочитано",
                _ => "Пока пусто"
            };

        var unread = mail.Count(m => m.IsUnread);
        FooterText.Text = unread > 0 ? $"Непрочитанных: {unread}" : "Всё прочитано";

        UpdateNextUp();
    }

    private static StreamSection BuildSection(DateTimeOffset day, IReadOnlyList<StreamEvent> items, DateTimeOffset now)
    {
        var today = now.Date;
        var isToday = day.Date == today;
        var isTomorrow = day.Date == today.AddDays(1);
        var isYesterday = day.Date == today.AddDays(-1);
        var isPast = day.Date < today;

        var title = isToday ? "Сегодня"
            : isTomorrow ? "Завтра"
            : isYesterday ? "Вчера"
            : day.ToString("dddd", Russian);

        var unread = items.Count(i => i.IsUnread);
        var summary = $"{items.Count} {StreamEvent.Plural(items.Count, "событие", "события", "событий")}";

        if (unread > 0)
        {
            summary += $" · {unread} {StreamEvent.Plural(unread, "непрочитанное", "непрочитанных", "непрочитанных")}";
        }

        return new StreamSection
        {
            Title = char.ToUpper(title[0], Russian) + title[1..],
            DateLabel = day.ToString("d MMMM", Russian),
            Summary = summary,
            Pill = isToday ? $"СЕЙЧАС {now:HH:mm}" : isTomorrow ? "ЗАВТРА" : isPast ? "РАНЕЕ" : string.Empty,
            PillBrush = isToday
                ? Frozen(Color.FromRgb(0x79, 0xAE, 0xF9))
                : Frozen(Color.FromRgb(0x74, 0x7D, 0x8F))
        };
    }

    private void UpdateNextUp()
    {
        var now = DateTimeOffset.Now;

        _nextUp = _occurrences
            .Where(o => o.End >= now && !o.AllDay)
            .OrderBy(o => o.Start)
            .FirstOrDefault();

        if (_nextUp is null)
        {
            NextUpCard.Visibility = Visibility.Collapsed;
            return;
        }

        var start = _nextUp.Start.ToLocalTime();
        var end = _nextUp.End.ToLocalTime();
        var running = _nextUp.Start <= now;

        NextUpCard.Visibility = Visibility.Visible;
        NextUpLabel.Text = running ? "ИДЁТ СЕЙЧАС" : "СЛЕДУЮЩАЯ ВСТРЕЧА";
        NextUpCountdown.Text = running
            ? $"осталось {StreamEvent.Humanize(_nextUp.End - now)}"
            : $"через {StreamEvent.Humanize(_nextUp.Start - now)}";
        NextUpSpan.Text = $"{start:HH:mm}–{end:HH:mm}";
        NextUpSubject.Text = _nextUp.Subject;

        var meta = new List<string>();
        if (!string.IsNullOrWhiteSpace(_nextUp.OrganizerName))
        {
            meta.Add(_nextUp.OrganizerName!);
        }

        if (_nextUp.Attendees.Count > 0)
        {
            meta.Add($"{_nextUp.Attendees.Count} участников");
        }

        NextUpMeta.Text = string.Join(" · ", meta);
        NextUpJoin.Visibility = string.IsNullOrEmpty(_nextUp.OnlineMeetingLink)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private static string[] Tokenize(string query)
    {
        return query
            .Split(new[] { ' ', '\t', ',', '.', ';', ':', '(', ')', '"', '\'' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.ToLowerInvariant())
            .Where(w => w.Length > 0)
            .ToArray();
    }

    private static bool Matches(StreamEvent item, string[] words)
    {
        var haystack = item.SearchText.ToLowerInvariant();

        foreach (var word in words)
        {
            var found = false;
            var index = haystack.IndexOf(word, StringComparison.Ordinal);

            while (index >= 0)
            {
                if (index == 0 || !char.IsLetterOrDigit(haystack[index - 1]))
                {
                    found = true;
                    break;
                }

                index = haystack.IndexOf(word, index + 1, StringComparison.Ordinal);
            }

            if (!found)
            {
                return false;
            }
        }

        return true;
    }

    private void OnRowClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: StreamEvent row })
        {
            return;
        }

        if (row.IsMail)
        {
            row.IsExpanded = !row.IsExpanded;

            if (row.IsExpanded && row.IsUnread)
            {
                row.MarkRead();
                MarkReadRequested?.Invoke(row.ServerId);
            }

            return;
        }

        if (row.Occurrence is not null)
        {
            OpenDetail(row.Occurrence);
        }
    }

    private void OpenDetail(EasOccurrence occurrence)
    {
        _detail = occurrence;
        _attendeesExpanded = false;

        var now = DateTimeOffset.Now;
        var start = occurrence.Start.ToLocalTime();
        var end = occurrence.End.ToLocalTime();
        var running = occurrence.Start <= now && occurrence.End >= now;

        DetailWhen.Text = running
            ? $"ИДЁТ СЕЙЧАС · ДО {end:HH:mm}"
            : start.Date == now.Date
                ? $"СЕГОДНЯ · ЧЕРЕЗ {StreamEvent.Humanize(occurrence.Start - now).ToUpperInvariant()}"
                : start.ToString("dddd, d MMMM", Russian).ToUpperInvariant();

        DetailSubject.Text = occurrence.Subject;

        var length = StreamEvent.Humanize(occurrence.End - occurrence.Start);
        DetailTime.Text = occurrence.IsRecurring
            ? $"{start:HH:mm}–{end:HH:mm} · {length} · повторяется"
            : $"{start:HH:mm}–{end:HH:mm} · {length}";

        DetailOrganizer.Text = string.IsNullOrWhiteSpace(occurrence.OrganizerName)
            ? string.Empty
            : $"Организатор — {occurrence.OrganizerName}";

        var link = occurrence.OnlineMeetingLink ?? string.Empty;
        DetailJoinPanel.Visibility = link.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        DetailPlatform.Text = PlatformName(link);
        DetailLink.Text = link;

        if (occurrence.Attendees.Count > 0)
        {
            var total = occurrence.Attendees.Count;
            DetailAttendees.Visibility = Visibility.Visible;
            DetailAttendeeCount.Text = $"{total} {StreamEvent.Plural(total, "участник", "участника", "участников")}";

            var required = occurrence.Attendees.Count(a => a.Type != EasAttendeeType.Optional);
            var optional = total - required;
            DetailAttendeeSplit.Text = $"{required} обязательных · {optional} необязательных";

            AttendeeItems.ItemsSource = occurrence.Attendees
                .OrderBy(a => a.Type == EasAttendeeType.Optional ? 1 : 0)
                .ThenBy(a => a.DisplayName, StringComparer.CurrentCulture)
                .Take(300)
                .Select(a => new StreamAttendeeRow
                {
                    Name = a.DisplayName,
                    Kind = a.Type switch
                    {
                        EasAttendeeType.Optional => "необязательный",
                        EasAttendeeType.Resource => "ресурс",
                        _ => "обязательный"
                    }
                })
                .ToList();
        }
        else
        {
            DetailAttendees.Visibility = Visibility.Collapsed;
        }

        ApplyAttendeeExpansion();

        DetailBody.Text = string.IsNullOrWhiteSpace(occurrence.Body)
            ? "Организатор не добавил повестку."
            : occurrence.Body!.Trim();

        DetailRsvp.Visibility = occurrence.NeedsResponse ? Visibility.Visible : Visibility.Collapsed;
        DetailRsvpLabel.Text = occurrence.ResponseType switch
        {
            EasResponseType.Tentative => "Вы ответили «под вопросом»",
            EasResponseType.Accepted => "Вы приняли приглашение",
            EasResponseType.Declined => "Вы отклонили приглашение",
            _ => "Вы ещё не ответили на приглашение"
        };

        DetailPanel.Visibility = Visibility.Visible;
    }

    private void CloseDetail()
    {
        _detail = null;
        DetailPanel.Visibility = Visibility.Collapsed;
    }

    private void ApplyAttendeeExpansion()
    {
        DetailAttendeeList.Visibility = _attendeesExpanded ? Visibility.Visible : Visibility.Collapsed;
        DetailAttendeeToggleText.Text = _attendeesExpanded ? "Скрыть" : "Показать";
    }

    private void OnToggleAttendees(object sender, RoutedEventArgs e)
    {
        _attendeesExpanded = !_attendeesExpanded;
        ApplyAttendeeExpansion();
    }

    private void OnCloseDetail(object sender, RoutedEventArgs e) => CloseDetail();

    private void OnFilterClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag })
        {
            _filter = tag;
            ApplyFilterAppearance();
            Rebuild();
        }
    }

    private void ApplyFilterAppearance()
    {
        Paint(TabAll, _filter == "all");
        Paint(TabMeetings, _filter == "meetings");
        Paint(TabMail, _filter == "mail");
        Paint(TabUnread, _filter == "unread");

        static void Paint(Border border, bool active)
        {
            border.Background = active ? TabActiveBackground : System.Windows.Media.Brushes.Transparent;
            TextElement.SetForeground(border, active ? TabActiveText : TabIdleText);
        }
    }

    private void OnToggleSearch(object sender, RoutedEventArgs e)
    {
        if (SearchBar.Visibility == Visibility.Visible)
        {
            HideSearch();
            return;
        }

        SearchBar.Visibility = Visibility.Visible;
        SearchBox.Focus();
    }

    private void HideSearch()
    {
        SearchBar.Visibility = Visibility.Collapsed;

        if (_query.Length > 0)
        {
            SearchBox.Text = string.Empty;
            _query = string.Empty;
            Rebuild();
        }
    }

    private void OnSearchChanged(object sender, RoutedEventArgs e)
    {
        _query = SearchBox.Text ?? string.Empty;
        Rebuild();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
        {
            return;
        }

        if (DetailPanel.Visibility == Visibility.Visible)
        {
            CloseDetail();
        }
        else if (SearchBar.Visibility == Visibility.Visible)
        {
            HideSearch();
        }
        else
        {
            Hide();
        }

        e.Handled = true;
    }

    private void OnVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
        {
            UpdateNextUp();
            _ticker.Start();
        }
        else
        {
            _ticker.Stop();
        }
    }

    private void OnRsvp(object sender, RoutedEventArgs e)
    {
        if (_detail is null || sender is not FrameworkElement { Tag: string tag })
        {
            return;
        }

        var reply = tag switch
        {
            "accept" => MeetingReply.Accept,
            "tentative" => MeetingReply.Tentative,
            _ => MeetingReply.Decline
        };

        if (RespondRequested is null)
        {
            Open($"{_settings.Server}/owa/#path=/calendar");
            Hide();
            return;
        }

        RespondRequested.Invoke(_detail, reply);
        DetailRsvpLabel.Text = "Ответ отправлен";
    }

    private void OnJoin(object sender, RoutedEventArgs e)
    {
        if (_nextUp?.OnlineMeetingLink is { Length: > 0 } link)
        {
            Open(link);
            Hide();
        }
    }

    private void OnJoinDetail(object sender, RoutedEventArgs e)
    {
        if (_detail?.OnlineMeetingLink is { Length: > 0 } link)
        {
            Open(link);
            Hide();
        }
    }

    private void OnOpenMail(object sender, RoutedEventArgs e)
    {
        Open($"{_settings.Server}/owa/#path=/mail");
        Hide();
    }

    private static string PlatformName(string link)
    {
        if (link.Contains("teams.", StringComparison.OrdinalIgnoreCase)) return "Microsoft Teams";
        if (link.Contains("zoom.us", StringComparison.OrdinalIgnoreCase)) return "Zoom";
        if (link.Contains("webex.com", StringComparison.OrdinalIgnoreCase)) return "Webex";
        if (link.Contains("meet.google", StringComparison.OrdinalIgnoreCase)) return "Google Meet";
        if (link.Contains("ktalk.ru", StringComparison.OrdinalIgnoreCase)) return "KTalk";
        if (link.Contains("telemost", StringComparison.OrdinalIgnoreCase)) return "Телемост";
        if (link.Contains("vinteo", StringComparison.OrdinalIgnoreCase)) return "Vinteo";
        return "Онлайн-встреча";
    }

    private static void Open(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            Log.Error("open url failed", exception);
        }
    }

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}

public enum MeetingReply
{
    Accept,
    Tentative,
    Decline
}