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
    private bool _searchOpen;
    private bool _ready;
    private string _filter = string.Empty;

    public FlyoutWindow(AppSettings settings)
    {
        _settings = settings;
        InitializeComponent();

        BrandText.Text = Tracking.Wide("OWL");
        DetailAgendaLabel.Text = Tracking.Wide("ПОВЕСТКА");
        FiltersLabel.Text = Tracking.Wide("БЫСТРЫЕ ФИЛЬТРЫ");
        RecentLabel.Text = Tracking.Wide("НЕДАВНИЕ ЗАПРОСЫ");
        SuggestLabel.Text = Tracking.Wide("ПОПРОБУЙТЕ");

        _ticker = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _ticker.Tick += (_, _) => Rebuild();

        Deactivated += (_, _) => Hide();
        IsVisibleChanged += OnVisibleChanged;
        PreviewKeyDown += OnPreviewKeyDown;

        PaintThemeToggle();
        ThemeService.Changed += OnThemeChanged;
        Closed += (_, _) => ThemeService.Changed -= OnThemeChanged;

        _ready = true;
    }

    private void OnToggleTheme(object sender, RoutedEventArgs e)
    {
        var next = ThemeService.IsDark ? ThemeChoice.Light : ThemeChoice.Dark;

        _settings.Theme = ThemeService.Name(next);
        AppStorage.Save(_settings);

        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(120));

        fade.Completed += (_, _) =>
        {
            var before = ThemeService.IsDark;
            ThemeService.Apply(next);

            if (ThemeService.IsDark == before)
            {
                Root.BeginAnimation(OpacityProperty, null);
                Root.Opacity = 1;
            }
        };

        Root.BeginAnimation(OpacityProperty, fade);
    }

    private void OnThemeChanged()
    {
        PaintThemeToggle();

        if (_ready)
        {
            Rebuild();

            if (MailPanel.Visibility == Visibility.Visible)
            {
                RenderMailList();
            }
        }

        Root.BeginAnimation(OpacityProperty, null);

        if (!IsVisible)
        {
            Root.Opacity = 1;
            return;
        }

        Root.Opacity = 0;
        Root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120)));
    }

    private void PaintThemeToggle()
    {
        var dark = ThemeService.IsDark;

        ThemeGlyph.SetResourceReference(System.Windows.Shapes.Path.DataProperty, dark ? "SunIcon" : "MoonIcon");
        ThemeGlyph.Width = dark ? 16 : 14.6;
        ThemeGlyph.Height = ThemeGlyph.Width;
        ThemeToggle.ToolTip = dark ? "Светлая тема" : "Тёмная тема";
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
        var searching = _searchOpen && (words.Length > 0 || _filter.Length > 0);

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

        SearchTools.Visibility = _searchOpen ? Visibility.Visible : Visibility.Collapsed;
        MailFooter.Visibility = searching ? Visibility.Collapsed : Visibility.Visible;
        SearchFooter.Visibility = searching ? Visibility.Visible : Visibility.Collapsed;

        if (_searchOpen)
        {
            RenderSearch(mail, words, now, searching);
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
                    .Where(m => m.DateReceived is { } received && received.ToLocalTime().Date == today)
                    .Take(4)
                    .Select(m => (Sort: m.DateReceived!.Value, Item: (object)StreamMail.Create(m, now, IsUnreadMessage(m)))))
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
        EmptyClear.Visibility = Visibility.Collapsed;
        SuggestBlock.Visibility = Visibility.Collapsed;
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
        IReadOnlyList<EasMessage> mail,
        string[] words,
        DateTimeOffset now,
        bool active)
    {
        HeroCard.Visibility = Visibility.Collapsed;
        CalmCard.Visibility = Visibility.Collapsed;

        PaintChip(FilterToday, FilterTodayText, _filter == "today");
        PaintChip(FilterWeek, FilterWeekText, _filter == "week");
        PaintChip(FilterOpen, FilterOpenText, _filter == "unanswered");

        var recent = _settings.RecentSearches.Take(4).ToList();
        RecentList.ItemsSource = recent;
        RecentBlock.Visibility = recent.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        SearchIdle.Visibility = active ? Visibility.Collapsed : Visibility.Visible;
        FiltersLabel.Visibility = active ? Visibility.Collapsed : Visibility.Visible;

        var pool = _occurrences
            .Where(o => !o.AllDay && (o.End >= now || o.Start.ToLocalTime().Date == now.Date))
            .OrderBy(o => o.Start)
            .ToList();

        SearchScope.Text =
            $"Ищем по {pool.Count} {Format.Plural(pool.Count, "встрече", "встречам", "встречам")} " +
            $"и {mail.Count} {Format.Plural(mail.Count, "письму", "письмам", "письмам")}: " +
            "тема, участники, организатор, отправитель. Календарь загружен на 30 дней вперёд, " +
            "прошедшее — только за сегодня.";

        if (!active)
        {
            StreamList.ItemsSource = Array.Empty<object>();
            EmptyBlock.Visibility = Visibility.Collapsed;
            SearchCount.Visibility = Visibility.Collapsed;
            return;
        }

        var foundMeetings = pool
            .Where(o => PassesFilter(o, now) && (words.Length == 0 || Matches(SearchTextOf(o), words)))
            .ToList();

        var foundMail = mail
            .Where(m => PassesFilter(m, now) && (words.Length == 0 || Matches(SearchTextOf(m), words)))
            .Take(20)
            .ToList();

        var items = new List<object>();
        var today = now.Date;

        foreach (var group in foundMeetings.GroupBy(o => o.Start.ToLocalTime().Date).OrderBy(g => g.Key))
        {
            items.Add(new StreamSection
            {
                Label = Tracking.Wide(DayLabel(group.Key, today)),
                LabelBrush = Palette.Ink
            });

            items.AddRange(group
                .OrderBy(o => o.End <= now)
                .ThenBy(o => o.Start)
                .Select(o => StreamMeeting.CreateForSearch(o, now, words)));
        }

        if (foundMail.Count > 0)
        {
            items.Add(Section("ПОЧТА"));
            items.AddRange(foundMail.Select(m => StreamMail.Create(m, now, IsUnreadMessage(m), words)));
        }

        StreamList.ItemsSource = items;

        var total = foundMeetings.Count + foundMail.Count;
        SearchCount.Text = total.ToString();
        SearchCount.Visibility = Visibility.Visible;

        SearchTally.Text =
            $"{foundMeetings.Count} {Format.Plural(foundMeetings.Count, "встреча", "встречи", "встреч")} " +
            $"из {pool.Count} · {foundMail.Count} " +
            $"{Format.Plural(foundMail.Count, "письмо", "письма", "писем")} из {mail.Count}";

        if (total > 0)
        {
            EmptyBlock.Visibility = Visibility.Collapsed;
            SuggestBlock.Visibility = Visibility.Collapsed;
            return;
        }

        EmptyBlock.Visibility = Visibility.Visible;
        EmptyClear.Visibility = Visibility.Visible;
        EmptyTitle.Text = "Ничего не нашлось";
        EmptyDetail.Text = _filter.Length > 0 && words.Length == 0
            ? "Под выбранный фильтр ничего не подходит."
            : $"Ни в {pool.Count} {Format.Plural(pool.Count, "встрече", "встречах", "встречах")}, " +
              $"ни в {mail.Count} {Format.Plural(mail.Count, "письме", "письмах", "письмах")}. " +
              "Поиск не заглядывает дальше загруженного окна.";

        var hints = Suggestions(pool, mail, words);
        SuggestList.ItemsSource = hints;
        SuggestBlock.Visibility = hints.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static string DayLabel(DateTime date, DateTime today)
    {
        if (date == today)
        {
            return "СЕГОДНЯ";
        }

        return date == today.AddDays(1)
            ? "ЗАВТРА"
            : date.ToString("dddd, d MMMM", Russian).ToUpperInvariant();
    }

    private bool PassesFilter(EasOccurrence occurrence, DateTimeOffset now)
    {
        var start = occurrence.Start.ToLocalTime();

        return _filter switch
        {
            "today" => start.Date == now.Date,
            "week" => start.Date >= now.Date && start.Date <= WeekStart(now).AddDays(6),
            "unanswered" => occurrence.NeedsResponse,
            _ => true
        };
    }

    private bool PassesFilter(EasMessage message, DateTimeOffset now)
    {
        var received = (message.DateReceived ?? now).ToLocalTime();

        return _filter switch
        {
            "today" => received.Date == now.Date,
            "week" => received.Date >= WeekStart(now),
            "unanswered" => message.IsMeetingRequest,
            _ => true
        };
    }

    private static DateTime WeekStart(DateTimeOffset now)
    {
        return now.Date.AddDays(-(((int)now.DayOfWeek + 6) % 7));
    }

    private static List<string> Suggestions(
        IReadOnlyList<EasOccurrence> pool,
        IReadOnlyList<EasMessage> mail,
        string[] words)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var subject in pool.Select(o => o.Subject).Concat(mail.Select(m => m.DisplaySubject)))
        {
            foreach (var word in Tokenize(subject).Distinct())
            {
                if (word.Length < 5 || words.Contains(word))
                {
                    continue;
                }

                counts[word] = counts.GetValueOrDefault(word) + 1;
            }
        }

        return counts
            .Where(pair => pair.Value > 1)
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Take(3)
            .Select(pair => pair.Key)
            .ToList();
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

    private static readonly char[] Separators =
    {
        ' ', '\t', '\n', '\r', ',', '.', ';', ':', '(', ')', '[', ']', '"', '\'', '«', '»',
        '—', '–', '-', '>', '<', '/', '\\', '|', '!', '?', '+', '*', '#'
    };

    private static string[] Tokenize(string query)
    {
        return query
            .Split(Separators, StringSplitOptions.RemoveEmptyEntries)
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
            if (_searchOpen)
            {
                RememberQuery(_query);
            }

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
            if (_searchOpen)
            {
                RememberQuery(_query);
            }

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
        if (active)
        {
            chip.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "Ink");
        }
        else
        {
            chip.Background = System.Windows.Media.Brushes.Transparent;
        }

        chip.SetResourceReference(System.Windows.Controls.Border.BorderBrushProperty, active ? "Ink" : "Line");
        text.SetResourceReference(
            System.Windows.Controls.TextBlock.ForegroundProperty,
            active ? "Surface" : "Secondary");

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
        string foreground,
        double lineHeight)
    {
        var document = new FlowDocument
        {
            PagePadding = new Thickness(0, 0, 10, 14),
            TextAlignment = TextAlignment.Left,
            FontSize = 13,
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI Variable Text, Segoe UI")
        };

        document.SetResourceReference(FlowDocument.ForegroundProperty, foreground);

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
            "Ink",
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
            "Secondary",
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
        if (_searchOpen)
        {
            HideSearch();
            return;
        }

        _searchOpen = true;
        HeaderIdle.Visibility = Visibility.Collapsed;
        SearchRow.Visibility = Visibility.Visible;

        SearchShift.BeginAnimation(
            TranslateTransform.XProperty,
            new DoubleAnimation(16, 0, TimeSpan.FromMilliseconds(140))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });

        SearchRow.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));

        Rebuild();
        SearchBox.Focus();
    }

    private void OnCloseSearch(object sender, MouseButtonEventArgs e) => HideSearch();

    private void HideSearch()
    {
        if (!_searchOpen)
        {
            return;
        }

        _searchOpen = false;
        _filter = string.Empty;
        SearchRow.Visibility = Visibility.Collapsed;
        HeaderIdle.Visibility = Visibility.Visible;

        SearchBox.Text = string.Empty;
        _query = string.Empty;
        Rebuild();
    }

    private void OnSearchChanged(object sender, RoutedEventArgs e)
    {
        if (!_ready)
        {
            return;
        }

        _query = SearchBox.Text ?? string.Empty;
        SearchPlaceholder.Visibility = _query.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        Rebuild();
    }

    private void OnSearchKey(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        RememberQuery(_query);
        Rebuild();
        e.Handled = true;
    }

    private void OnSearchFilter(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag })
        {
            _filter = _filter == tag ? string.Empty : tag;
            Rebuild();
        }
    }

    private void OnUseQuery(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: string query })
        {
            SearchBox.Text = query;
            SearchBox.CaretIndex = query.Length;
            SearchBox.Focus();
        }
    }

    private void OnClearSearch(object sender, MouseButtonEventArgs e)
    {
        _filter = string.Empty;
        SearchBox.Text = string.Empty;
        Rebuild();
        SearchBox.Focus();
    }

    private void RememberQuery(string raw)
    {
        var query = raw.Trim();

        if (query.Length < 2)
        {
            return;
        }

        var recent = _settings.RecentSearches;
        recent.RemoveAll(item => string.Equals(item, query, StringComparison.OrdinalIgnoreCase));
        recent.Insert(0, query);

        while (recent.Count > 4)
        {
            recent.RemoveAt(recent.Count - 1);
        }

        AppStorage.Save(_settings);
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
        else if (_searchOpen)
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
            LetterBodyView.Document = BuildBody(body!, "Ink", 20);
        }
        else if (!isMail && _detail?.ServerId == serverId)
        {
            DetailBodyView.Document = BuildBody(body!, "Secondary", 20);
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