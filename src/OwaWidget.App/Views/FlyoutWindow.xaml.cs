using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using OwaWidget.App.Services;
using OwaWidget.Eas.Models;
using OwaWidget.Eas.Recurrence;
using Wpf.Ui.Controls;
using Clipboard = System.Windows.Clipboard;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace OwaWidget.App.Views;

public partial class FlyoutWindow : FluentWindow
{
    private static readonly CultureInfo Russian = CultureInfo.GetCultureInfo("ru-RU");

    private readonly AppSettings _settings;
    private readonly DispatcherTimer _ticker;

    private IReadOnlyList<EasOccurrence> _occurrences = Array.Empty<EasOccurrence>();
    private IReadOnlyList<EasMessage> _messages = Array.Empty<EasMessage>();
    private string _query = string.Empty;
    private EasOccurrence? _hero;
    private EasOccurrence? _detail;
    private EasMessage? _letter;
    private string _mailFilter = "all";
    private readonly HashSet<string> _locallyRead = new(StringComparer.Ordinal);
    private readonly HashSet<string> _bodyLoaded = new(StringComparer.Ordinal);
    private bool _attendeesExpanded;
    private bool _offline;

    public FlyoutWindow(AppSettings settings)
    {
        _settings = settings;
        InitializeComponent();

        BrandText.Text = Tracking.Wide("OWL");
        DetailAgendaLabel.Text = Tracking.Wide("ПОВЕСТКА");

        _ticker = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _ticker.Tick += (_, _) => Rebuild();

        Deactivated += (_, _) => Hide();
        IsVisibleChanged += OnVisibleChanged;
        PreviewKeyDown += OnPreviewKeyDown;
    }

    public event Action<string>? MarkReadRequested;

    public Func<string, bool, Task<string?>>? BodyLoader { get; set; }

    public Func<string, DateTimeOffset?, MeetingReply, bool, Task<bool>>? MeetingResponder { get; set; }

    public void ShowNearTray()
    {
        _detail = null;
        _letter = null;
        DetailPanel.Visibility = Visibility.Collapsed;
        LetterPanel.Visibility = Visibility.Collapsed;
        MailPanel.Visibility = Visibility.Collapsed;
        HideSearch();

        var area = SystemParameters.WorkArea;
        Left = area.Right - Width - 12;
        Top = area.Bottom - Height - 12;

        Show();
        Activate();
    }

    public void UpdateStatus(string status)
    {
        var healthy = status.Contains("Подключено", StringComparison.OrdinalIgnoreCase) ||
                      status.Contains("Синхрон", StringComparison.OrdinalIgnoreCase);

        _offline = !healthy && !status.Contains("Подключение", StringComparison.OrdinalIgnoreCase);

        StatusText.Text = healthy
            ? $"Синхронизировано в {DateTime.Now:HH:mm}"
            : status;

        OfflineBar.Visibility = _offline ? Visibility.Visible : Visibility.Collapsed;
        OfflineTitle.Text = status;
        OfflineDetail.Text = $"Показаны сохранённые данные · {DateTime.Now:HH:mm}";
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
        ClockText.Text = now.ToString("ddd d MMM", Russian) + $" · {now:HH:mm}";

        var words = Tokenize(_query);
        var searching = words.Length > 0;

        var meetings = _occurrences
            .Where(o => o.End >= now && !o.AllDay)
            .OrderBy(o => o.Start)
            .ToList();

        var mail = _messages
            .OrderByDescending(m => m.DateReceived ?? DateTimeOffset.MinValue)
            .Take(60)
            .ToList();

        var unread = mail.Count(IsUnreadMessage);
        MailLabel.Text = Tracking.Wide("ПОЧТА");
        MailSummary.Text = unread > 0
            ? $"{unread} {Format.Plural(unread, "новое", "новых", "новых")} из {mail.Count} →"
            : $"{mail.Count} {Format.Plural(mail.Count, "письмо", "письма", "писем")} →";

        var peek = mail.Where(IsUnreadMessage).Take(2).ToList();
        if (peek.Count < 2)
        {
            peek.AddRange(mail.Where(m => !IsUnreadMessage(m)).Take(2 - peek.Count));
        }

        MailPeek.ItemsSource = peek.Select(m => StreamMail.Create(m, now, IsUnreadMessage(m))).ToList();

        if (searching)
        {
            RenderSearch(meetings, mail, words, now);
            return;
        }

        var today = now.Date;
        var hasToday = meetings.Any(o => o.Start.ToLocalTime().Date == today);

        _hero = hasToday ? meetings.FirstOrDefault() : null;
        UpdateHero(now);
        UpdateCalm(meetings, now);

        if (!hasToday)
        {
            RenderDays(meetings, now);
            return;
        }

        var items = new List<object>();

        var concurrent = _hero is null
            ? new List<EasOccurrence>()
            : meetings.Where(o => o != _hero && o.Start < _hero.End && o.End > _hero.Start).ToList();

        if (concurrent.Count > 0)
        {
            var at = _hero!.Start.ToLocalTime();
            items.Add(Section($"В ТО ЖЕ ВРЕМЯ · {at:HH:mm}"));
            items.AddRange(concurrent.Select(o => StreamMeeting.Create(o, now, showLead: false)));
        }

        var handled = new HashSet<EasOccurrence>(concurrent) { };
        if (_hero is not null)
        {
            handled.Add(_hero);
        }

        var restToday = meetings
            .Where(o => !handled.Contains(o) && o.Start.ToLocalTime().Date == today)
            .ToList();

        var laterDays = meetings
            .Where(o => !handled.Contains(o) && o.Start.ToLocalTime().Date > today)
            .ToList();

        if (restToday.Count > 0)
        {
            items.Add(Section("ПОТОМ"));

            var mixed = restToday
                .Select(o => (Sort: o.Start, Item: (object)StreamMeeting.Create(o, now, showLead: true)))
                .Concat(mail
                    .Where(m => (m.DateReceived ?? now).ToLocalTime().Date == today)
                    .Take(4)
                    .Select(m => (Sort: m.DateReceived ?? now, Item: (object)StreamMail.Create(m, now, IsUnreadMessage(m)))))
                .OrderBy(x => x.Sort)
                .Select(x => x.Item);

            items.AddRange(mixed);
        }

        if (laterDays.Count > 0)
        {
            AppendDays(items, laterDays, today, 2);
        }

        StreamList.ItemsSource = items;
        EmptyBlock.Visibility = Visibility.Collapsed;
    }

    private void RenderDays(IReadOnlyList<EasOccurrence> meetings, DateTimeOffset now)
    {
        var items = new List<object>();
        AppendDays(items, meetings, now.Date, 4);

        StreamList.ItemsSource = items;
        EmptyBlock.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyTitle.Text = "Впереди пусто";
        EmptyDetail.Text = "Ни одной встречи в ближайшие недели.";
    }

    private void AppendDays(
        List<object> items,
        IReadOnlyList<EasOccurrence> meetings,
        DateTime today,
        int maxDays)
    {
        var now = DateTimeOffset.Now;
        var groups = meetings
            .GroupBy(o => o.Start.ToLocalTime().Date)
            .Where(g => g.Key > today)
            .OrderBy(g => g.Key)
            .ToList();

        foreach (var group in groups.Take(maxDays))
        {
            var calendarDays = (group.Key - today).Days;
            var gap = Format.WholeDays(group.Min(o => o.Start) - now);

            items.Add(new StreamSection
            {
                Label = Tracking.Wide(group.Key.ToString("dddd, d MMMM", Russian).ToUpperInvariant()),
                Note = calendarDays == 1
                    ? "завтра"
                    : $"через {gap} {Format.Plural(gap, "день", "дня", "дней")}",
                LabelBrush = Palette.Ink
            });

            items.AddRange(group.OrderBy(o => o.Start).Select(StreamMeeting.CreateForDay));
        }

        var rest = groups.Count - maxDays;
        if (rest > 0)
        {
            items.Add(new StreamSection
            {
                Label = $"↓ ещё {rest} {Format.Plural(rest, "день", "дня", "дней")} со встречами",
                LabelBrush = Palette.Accent
            });
        }
    }

    private void UpdateCalm(IReadOnlyList<EasOccurrence> meetings, DateTimeOffset now)
    {
        if (_hero is not null)
        {
            CalmCard.Visibility = Visibility.Collapsed;
            return;
        }

        var next = meetings.FirstOrDefault();

        if (next is null)
        {
            CalmCard.Visibility = Visibility.Collapsed;
            return;
        }

        var start = next.Start.ToLocalTime();
        var days = (start.Date - now.Date).Days;
        var name = days == 1 ? "завтра" : DayName(start.DayOfWeek);

        CalmCard.Visibility = Visibility.Visible;
        CalmLabel.Text = Tracking.Wide("СЕГОДНЯ ВСТРЕЧ БОЛЬШЕ НЕТ");
        CalmNote.Text = days >= 2 && start.DayOfWeek == DayOfWeek.Monday ? "выходные свободны" : string.Empty;
        CalmTitle.Text = days == 1 ? "Следующая — завтра" : $"Следующая — {Preposition(start.DayOfWeek)} {name}";
        CalmDetail.Text = $"{start:d MMMM}, {start:HH:mm} · через {Format.Gap(next.Start - now)}";
        CalmButtonText.Text = days == 1 ? "Открыть завтра" : $"Открыть {name}";
    }

    private static string DayName(DayOfWeek day)
    {
        return day switch
        {
            DayOfWeek.Monday => "понедельник",
            DayOfWeek.Tuesday => "вторник",
            DayOfWeek.Wednesday => "среду",
            DayOfWeek.Thursday => "четверг",
            DayOfWeek.Friday => "пятницу",
            DayOfWeek.Saturday => "субботу",
            _ => "воскресенье"
        };
    }

    private static string Preposition(DayOfWeek day)
    {
        return day == DayOfWeek.Tuesday ? "во" : "в";
    }

    private void OnJumpToNextDay(object sender, RoutedEventArgs e)
    {
        if (StreamList.Items.Count > 0)
        {
            StreamList.BringIntoView();
        }
    }

    private void RenderSearch(
        IReadOnlyList<EasOccurrence> meetings,
        IReadOnlyList<EasMessage> mail,
        string[] words,
        DateTimeOffset now)
    {
        HeroCard.Visibility = Visibility.Collapsed;
        CalmCard.Visibility = Visibility.Collapsed;

        var foundMeetings = meetings
            .Where(o => Matches(SearchTextOf(o), words))
            .Take(20)
            .ToList();

        var foundMail = mail
            .Where(m => Matches(SearchTextOf(m), words))
            .Take(20)
            .ToList();

        var items = new List<object>();

        if (foundMeetings.Count > 0)
        {
            items.Add(Section("ВСТРЕЧИ"));
            items.AddRange(foundMeetings.Select(o => StreamMeeting.Create(o, now, showLead: false)));
        }

        if (foundMail.Count > 0)
        {
            items.Add(Section("ПОЧТА"));
            items.AddRange(foundMail.Select(m => StreamMail.Create(m, now, IsUnreadMessage(m))));
        }

        StreamList.ItemsSource = items;
        EmptyBlock.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyTitle.Text = "Ничего не найдено";
        EmptyDetail.Text = "Попробуйте изменить запрос.";
    }

    private static StreamSection Section(string label)
    {
        return new StreamSection { Label = Tracking.Wide(label) };
    }

    private void UpdateHero(DateTimeOffset now)
    {
        if (_hero is null)
        {
            HeroCard.Visibility = Visibility.Collapsed;
            return;
        }

        var start = _hero.Start.ToLocalTime();
        var end = _hero.End.ToLocalTime();
        var running = _hero.Start <= now;

        var reply = _hero.ResponseType switch
        {
            EasResponseType.Accepted => " · ВЫ ПРИНЯЛИ",
            EasResponseType.Tentative => " · ПОД ВОПРОСОМ",
            EasResponseType.Declined => " · ВЫ ОТКЛОНИЛИ",
            _ => _hero.NeedsResponse ? " · ВЫ НЕ ОТВЕТИЛИ" : string.Empty
        };

        HeroCard.Visibility = Visibility.Visible;
        HeroLabel.Text = Tracking.Wide((running ? "ИДЁТ СЕЙЧАС" : "СЛЕДУЮЩАЯ") + reply);

        var meta = $"{start:HH:mm}–{end:HH:mm}";
        if (_hero.Attendees.Count > 0)
        {
            meta += $" · {_hero.Attendees.Count}";
        }

        HeroMeta.Text = meta;

        var span = running ? _hero.End - now : _hero.Start - now;
        var text = Format.Span(span);
        var cut = text.IndexOf(' ');

        HeroCountdown.Text = (running ? "осталось " : "через ") + (cut > 0 ? text[..cut] : text);
        HeroUnit.Text = cut > 0 ? text[(cut + 1)..] : string.Empty;
        HeroSubject.Text = _hero.Subject;

        var hasLink = !string.IsNullOrEmpty(_hero.OnlineMeetingLink);
        HeroJoin.Visibility = hasLink ? Visibility.Visible : Visibility.Collapsed;
        HeroJoinColumn.Width = hasLink ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        HeroButtons.HorizontalAlignment = hasLink
            ? System.Windows.HorizontalAlignment.Stretch
            : System.Windows.HorizontalAlignment.Left;
    }

    private static string SearchTextOf(EasOccurrence o)
    {
        return string.Join(' ', o.Subject, o.Location, o.OrganizerName, o.OrganizerEmail,
            string.Join(' ', o.Attendees.Select(a => a.DisplayName)));
    }

    private static string SearchTextOf(EasMessage m)
    {
        return string.Join(' ', m.DisplaySubject, m.DisplaySender, m.FromAddress, m.Preview);
    }

    private static string[] Tokenize(string query)
    {
        return query
            .Split(new[] { ' ', '\t', ',', '.', ';', ':', '(', ')', '"', '\'' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.ToLowerInvariant())
            .ToArray();
    }

    private static bool Matches(string haystackRaw, string[] words)
    {
        var haystack = haystackRaw.ToLowerInvariant();

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

    private void OnMeetingClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: StreamMeeting row })
        {
            OpenDetail(row.Occurrence);
        }
    }

    private void OnJoinMeeting(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: StreamMeeting row } && row.Link.Length > 0)
        {
            e.Handled = true;
            Open(row.Link);
            Hide();
        }
    }

    private void OnMailClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: StreamMail row })
        {
            OpenLetter(row.Message);
        }
    }

    private void OnLetterClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: MailListRow row })
        {
            OpenLetter(row.Message);
        }
    }

    private void OnOpenMailPanel(object sender, RoutedEventArgs e) => OpenMailPanel();

    private void OpenMailPanel()
    {
        CloseDetail();
        CloseLetter();
        ShowPanel(MailPanel);
        RenderMailList();
    }

    private void OnCloseMailPanel(object sender, RoutedEventArgs e)
    {
        HidePanel(MailPanel);
    }

    private void OnMailFilter(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag })
        {
            _mailFilter = tag;
            RenderMailList();
        }
    }

    private void RenderMailList()
    {
        var now = DateTimeOffset.Now;

        var all = _messages
            .OrderByDescending(m => m.DateReceived ?? DateTimeOffset.MinValue)
            .ToList();

        var unreadCount = all.Count(IsUnreadMessage);
        var robots = all.Count(m => !m.DisplaySender.Contains(' '));

        ChipUnreadText.Text = $"{unreadCount} {Format.Plural(unreadCount, "новое", "новых", "новых")}";
        ChipAllText.Text = $"все {all.Count}";
        MailRobotsText.Text = $"{robots} из {all.Count} — от роботов";

        PaintChip(ChipUnread, ChipUnreadText, _mailFilter == "unread");
        PaintChip(ChipAll, ChipAllText, _mailFilter == "all");
        PaintChip(ChipPeople, ChipPeopleText, _mailFilter == "people");

        var selected = _mailFilter switch
        {
            "all" => all,
            "people" => all.Where(m => m.DisplaySender.Contains(' ')).ToList(),
            _ => all.Where(IsUnreadMessage).ToList()
        };

        var items = new List<object>();
        var unread = selected.Where(IsUnreadMessage).ToList();
        var read = selected.Where(m => !IsUnreadMessage(m)).ToList();

        items.AddRange(unread.Select(m => MailListRow.Create(m, now, IsUnreadMessage(m))));

        if (read.Count > 0)
        {
            if (unread.Count > 0)
            {
                items.Add(new StreamSection { Label = Tracking.Wide("ПРОЧИТАННЫЕ") });
            }

            items.AddRange(read.Select(m => MailListRow.Create(m, now, IsUnreadMessage(m))));
        }

        MailList.ItemsSource = items;
        MarkAllButton.Visibility = unreadCount > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static void PaintChip(
        System.Windows.Controls.Border chip,
        System.Windows.Controls.TextBlock text,
        bool active)
    {
        chip.Background = active ? Palette.Ink : System.Windows.Media.Brushes.Transparent;
        chip.BorderBrush = active ? Palette.Ink : Palette.Line;
        text.Foreground = active ? Palette.Surface : Palette.Secondary;
        text.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
    }

    private void OnMarkAllRead(object sender, RoutedEventArgs e)
    {
        foreach (var message in _messages.Where(IsUnreadMessage).ToList())
        {
            MarkReadRequested?.Invoke(message.ServerId);
        }

        RenderMailList();
    }

    private static readonly Regex ContentIdRef = new(@"\[cid:[^\]\s]*\]", RegexOptions.IgnoreCase);
    private static readonly Regex ImagePlaceholder = new(@"\[(?:image|изображение)[^\]]{0,40}\]", RegexOptions.IgnoreCase);
    private static readonly Regex IconPlaceholder = new(@"\[[\p{L}\s]{0,20}(?:icon|иконка|логотип|logo)\]", RegexOptions.IgnoreCase);
    private static readonly Regex BlankRun = new(@"\n{3,}");
    private static readonly Regex TrailingSpaces = new(@"[ \t]+(?=\n)");

    private static string CleanBody(string text)
    {
        var cleaned = text.Replace("\r\n", "\n").Replace('\r', '\n');

        cleaned = ContentIdRef.Replace(cleaned, string.Empty);
        cleaned = ImagePlaceholder.Replace(cleaned, string.Empty);
        cleaned = IconPlaceholder.Replace(cleaned, string.Empty);
        cleaned = TrailingSpaces.Replace(cleaned, string.Empty);
        cleaned = BlankRun.Replace(cleaned, "\n\n");

        return cleaned.Trim();
    }

    private static FlowDocument BuildBody(
        string text,
        System.Windows.Media.Brush foreground,
        double lineHeight)
    {
        var document = new FlowDocument
        {
            PagePadding = new Thickness(0, 0, 10, 14),
            TextAlignment = TextAlignment.Left,
            FontSize = 13,
            Foreground = foreground,
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI Variable Text, Segoe UI")
        };

        var blocks = CleanBody(text)
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Trim())
            .Where(part => part.Length > 0)
            .ToList();

        if (blocks.Count == 0)
        {
            blocks.Add(text.Trim());
        }

        foreach (var block in blocks)
        {
            document.Blocks.Add(new Paragraph(new Run(block))
            {
                LineHeight = lineHeight,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                Margin = new Thickness(0, 0, 0, 12)
            });
        }

        return document;
    }

    private static void ShowPanel(System.Windows.Controls.Border panel)
    {
        var slide = new TranslateTransform(16, 0);
        panel.RenderTransform = slide;
        panel.Opacity = 0;
        panel.Visibility = Visibility.Visible;

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        panel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160))
        {
            EasingFunction = ease
        });

        slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(16, 0, TimeSpan.FromMilliseconds(160))
        {
            EasingFunction = ease
        });
    }

    private static void HidePanel(System.Windows.Controls.Border panel)
    {
        if (panel.Visibility != Visibility.Visible)
        {
            return;
        }

        var slide = panel.RenderTransform as TranslateTransform;
        if (slide is null)
        {
            slide = new TranslateTransform();
            panel.RenderTransform = slide;
        }

        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
        var fade = new DoubleAnimation(panel.Opacity, 0, TimeSpan.FromMilliseconds(120)) { EasingFunction = ease };

        fade.Completed += (_, _) =>
        {
            panel.Visibility = Visibility.Collapsed;
            panel.BeginAnimation(OpacityProperty, null);
            panel.Opacity = 1;
        };

        panel.BeginAnimation(OpacityProperty, fade);
        slide.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(slide.X, 12, TimeSpan.FromMilliseconds(120)) { EasingFunction = ease });
    }

    private bool IsUnreadMessage(EasMessage message)
    {
        return !message.IsRead && !_locallyRead.Contains(message.ServerId);
    }

    private void MarkLocallyRead(EasMessage message)
    {
        if (!IsUnreadMessage(message))
        {
            return;
        }

        _locallyRead.Add(message.ServerId);
        MarkReadRequested?.Invoke(message.ServerId);
    }

    private void OpenLetter(EasMessage message)
    {
        _letter = message;
        MarkLocallyRead(message);

        var received = (message.DateReceived ?? DateTimeOffset.Now).ToLocalTime();
        var today = DateTimeOffset.Now.Date;

        LetterSubject.Text = string.IsNullOrWhiteSpace(message.Subject) ? "(без темы)" : message.Subject!;
        LetterFrom.Text = message.FromAddress ?? message.DisplaySender;
        LetterTo.Text = string.IsNullOrWhiteSpace(message.ThreadTopic)
            ? "кому: вы"
            : $"тема: {message.ThreadTopic}";

        var day = received.Date == today ? "сегодня"
            : received.Date == today.AddDays(-1) ? "вчера"
            : received.ToString("d MMMM", Russian);

        LetterDate.Text = $"{day}\n{received:HH:mm}";

        LetterBodyView.Document = BuildBody(
            string.IsNullOrWhiteSpace(message.Body) ? message.Preview ?? "(пустое письмо)" : message.Body!,
            Palette.Ink,
            20);

        var ordered = _messages
            .OrderByDescending(m => m.DateReceived ?? DateTimeOffset.MinValue)
            .ToList();

        var index = ordered.FindIndex(m => m.ServerId == message.ServerId);
        LetterPosition.Text = index >= 0 ? $"{index + 1} из {ordered.Count}" : string.Empty;

        LetterMarkRead.Visibility = Visibility.Collapsed;

        ShowPanel(LetterPanel);
        LoadFullBody(message.ServerId, isMail: true);
    }

    private void CloseLetter()
    {
        _letter = null;
        HidePanel(LetterPanel);
    }

    private void OnCloseLetter(object sender, RoutedEventArgs e) => CloseLetter();

    private void OnLetterMarkRead(object sender, RoutedEventArgs e)
    {
        if (_letter is null)
        {
            return;
        }

        MarkReadRequested?.Invoke(_letter.ServerId);
        LetterMarkRead.Visibility = Visibility.Collapsed;
    }

    private void OnLetterPrev(object sender, RoutedEventArgs e) => StepLetter(-1);

    private void OnLetterNext(object sender, RoutedEventArgs e) => StepLetter(1);

    private void StepLetter(int delta)
    {
        if (_letter is null)
        {
            return;
        }

        var ordered = _messages
            .OrderByDescending(m => m.DateReceived ?? DateTimeOffset.MinValue)
            .ToList();

        var index = ordered.FindIndex(m => m.ServerId == _letter.ServerId);
        var next = index + delta;

        if (index >= 0 && next >= 0 && next < ordered.Count)
        {
            OpenLetter(ordered[next]);
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

        var when = running
            ? $"ИДЁТ СЕЙЧАС · ДО {end:HH:mm}"
            : start.Date == now.Date
                ? $"СЕГОДНЯ · {start:HH:mm}–{end:HH:mm} · ЧЕРЕЗ {Format.Span(occurrence.Start - now).ToUpperInvariant()}"
                : $"{start.ToString("dddd, d MMMM", Russian).ToUpperInvariant()} · {start:HH:mm}–{end:HH:mm}";

        DetailWhen.Text = Tracking.Wide(when);
        DetailRecurrence.Text = occurrence.IsRecurring ? "повтор · еженедельно" : string.Empty;
        DetailSubject.Text = occurrence.Subject;
        DetailOrganizer.Text = string.IsNullOrWhiteSpace(occurrence.OrganizerName)
            ? string.Empty
            : $"Организатор — {occurrence.OrganizerName}";

        var link = occurrence.OnlineMeetingLink ?? string.Empty;
        DetailJoinRow.Visibility = link.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        DetailLink.Visibility = link.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        DetailLink.Text = link;

        if (occurrence.Attendees.Count > 0)
        {
            var total = occurrence.Attendees.Count;
            DetailAttendees.Visibility = Visibility.Visible;
            DetailAttendeeCount.Text = $"{total} {Format.Plural(total, "участник", "участника", "участников")}";

            var required = occurrence.Attendees.Count(a => a.Type != EasAttendeeType.Optional);
            DetailAttendeeSplit.Text =
                $"{required} обязательных · {total - required} необязательных · организатор {occurrence.OrganizerName}";

            AttendeeItems.ItemsSource = occurrence.Attendees
                .OrderBy(a => a.Type == EasAttendeeType.Optional ? 1 : 0)
                .ThenBy(a => a.DisplayName, StringComparer.CurrentCulture)
                .Take(300)
                .Select(a => new StreamAttendeeRow
                {
                    Name = a.DisplayName,
                    Kind = a.Status switch
                    {
                        EasAttendeeStatus.Accepted => "принял",
                        EasAttendeeStatus.Declined => "отклонил",
                        EasAttendeeStatus.Tentative => "под вопросом",
                        _ => "ответ неизвестен"
                    }
                })
                .ToList();
        }
        else
        {
            DetailAttendees.Visibility = Visibility.Collapsed;
        }

        ApplyAttendeeExpansion();

        DetailBodyView.Document = BuildBody(
            string.IsNullOrWhiteSpace(occurrence.Body) ? "Организатор не добавил повестку." : occurrence.Body!,
            Palette.Secondary,
            20);

        DetailRsvp.Visibility = occurrence.IsMeeting && !occurrence.IsOrganizer
            ? Visibility.Visible
            : Visibility.Collapsed;

        DetailRsvpLabel.Text = occurrence.ResponseType switch
        {
            EasResponseType.Accepted => "Ваш ответ: принято",
            EasResponseType.Tentative => "Ваш ответ: под вопросом",
            EasResponseType.Declined => "Ваш ответ: отклонено",
            _ => "Вы ещё не ответили на приглашение"
        };

        ShowPanel(DetailPanel);
        LoadFullBody(occurrence.ServerId, isMail: false);
    }

    private void CloseDetail()
    {
        _detail = null;
        HidePanel(DetailPanel);
    }

    private void ApplyAttendeeExpansion()
    {
        DetailAttendeeList.Visibility = _attendeesExpanded ? Visibility.Visible : Visibility.Collapsed;
        DetailAttendeeToggle.Text = _attendeesExpanded ? "Скрыть" : "Показать";
    }

    private void OnToggleAttendees(object sender, RoutedEventArgs e)
    {
        _attendeesExpanded = !_attendeesExpanded;
        ApplyAttendeeExpansion();
    }

    private void OnCloseDetail(object sender, RoutedEventArgs e) => CloseDetail();

    private void OnOpenHeroDetail(object sender, RoutedEventArgs e)
    {
        if (_hero is not null)
        {
            OpenDetail(_hero);
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

        if (LetterPanel.Visibility == Visibility.Visible)
        {
            CloseLetter();
        }
        else if (MailPanel.Visibility == Visibility.Visible)
        {
            HidePanel(MailPanel);
        }
        else if (DetailPanel.Visibility == Visibility.Visible)
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
            Rebuild();
            _ticker.Start();
        }
        else
        {
            _ticker.Stop();
        }
    }

    private static MeetingReply ParseReply(string tag)
    {
        return tag switch
        {
            "accept" => MeetingReply.Accept,
            "tentative" => MeetingReply.Tentative,
            _ => MeetingReply.Decline
        };
    }

    private async void OnRsvp(object sender, RoutedEventArgs e)
    {
        if (_detail is null || sender is not FrameworkElement { Tag: string tag })
        {
            return;
        }

        if (MeetingResponder is null)
        {
            Open($"{_settings.Server}/owa/#path=/calendar");
            Hide();
            return;
        }

        var reply = ParseReply(tag);
        DetailRsvpLabel.Text = "Отправляем ответ…";

        var instance = _detail.IsRecurring ? _detail.OriginalStart : (DateTimeOffset?)null;
        var ok = await MeetingResponder.Invoke(_detail.ServerId, instance, reply, false);

        DetailRsvpLabel.Text = ok
            ? reply switch
            {
                MeetingReply.Accept => "Ваш ответ: принято",
                MeetingReply.Tentative => "Ваш ответ: под вопросом",
                _ => "Ваш ответ: отклонено"
            }
            : "Не удалось отправить ответ — откройте встречу в OWA";
    }

    private async void OnInvitationReply(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string tag, DataContext: MailListRow row })
        {
            return;
        }

        if (MeetingResponder is null)
        {
            Open($"{_settings.Server}/owa/#path=/mail");
            Hide();
            return;
        }

        var ok = await MeetingResponder.Invoke(row.Message.ServerId, null, ParseReply(tag), true);

        if (ok)
        {
            _locallyRead.Add(row.Message.ServerId);
            RenderMailList();
        }
    }

    private async void LoadFullBody(string serverId, bool isMail)
    {
        if (BodyLoader is null || !_bodyLoaded.Add(serverId))
        {
            return;
        }

        var body = await BodyLoader.Invoke(serverId, isMail);

        if (string.IsNullOrWhiteSpace(body))
        {
            return;
        }

        if (isMail && _letter?.ServerId == serverId)
        {
            LetterBodyView.Document = BuildBody(body!, Palette.Ink, 20);
        }
        else if (!isMail && _detail?.ServerId == serverId)
        {
            DetailBodyView.Document = BuildBody(body!, Palette.Secondary, 20);
        }
    }

    private void OnJoinHero(object sender, RoutedEventArgs e)
    {
        if (_hero?.OnlineMeetingLink is { Length: > 0 } link)
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

    private void OnCopyLink(object sender, RoutedEventArgs e)
    {
        if (_detail?.OnlineMeetingLink is { Length: > 0 } link)
        {
            try
            {
                Clipboard.SetText(link);
                DetailLink.Text = "Ссылка скопирована";
            }
            catch (Exception exception)
            {
                Log.Error("clipboard failed", exception);
            }
        }
    }

    private void OnOpenMail(object sender, RoutedEventArgs e)
    {
        Open($"{_settings.Server}/owa/#path=/mail");
        Hide();
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
}

public enum MeetingReply
{
    Accept,
    Tentative,
    Decline
}