using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using OwaWidget.Eas.Models;
using OwaWidget.Eas.Recurrence;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using FontWeight = System.Windows.FontWeight;

namespace OwaWidget.App.Views;

public sealed class StreamSection
{
    public required string Title { get; init; }

    public required string DateLabel { get; init; }

    public required string Summary { get; init; }

    public required string Pill { get; init; }

    public required Brush PillBrush { get; init; }
}

public sealed class StreamAttendeeRow
{
    public required string Name { get; init; }

    public required string Kind { get; init; }
}

public sealed class StreamEvent : INotifyPropertyChanged
{
    public static readonly Geometry CalendarGeometry =
        Geometry.Parse("M4,6 L20,6 L20,20 L4,20 Z M4,10 L20,10 M8.5,3.6 L8.5,7 M15.5,3.6 L15.5,7");

    public static readonly Geometry MailGeometry =
        Geometry.Parse("M3,6 L21,6 L21,18 L3,18 Z M3.6,6.8 L12,13.4 L20.4,6.8");

    private static readonly Brush BlueTile = Frozen(Color.FromArgb(36, 81, 143, 228));
    private static readonly Brush VioletTile = Frozen(Color.FromArgb(36, 146, 95, 230));
    private static readonly Brush TealTile = Frozen(Color.FromArgb(36, 53, 172, 153));
    private static readonly Brush OrangeTile = Frozen(Color.FromArgb(36, 209, 128, 41));
    private static readonly Brush RoseTile = Frozen(Color.FromArgb(36, 212, 86, 112));

    private static readonly Brush Blue = Frozen(Color.FromRgb(0x82, 0xB6, 0xFF));
    private static readonly Brush Violet = Frozen(Color.FromRgb(0xBE, 0x9C, 0xFF));
    private static readonly Brush Teal = Frozen(Color.FromRgb(0x6A, 0xD7, 0xC6));
    private static readonly Brush Orange = Frozen(Color.FromRgb(0xEF, 0xB0, 0x6C));
    private static readonly Brush Rose = Frozen(Color.FromRgb(0xEE, 0x96, 0xAB));

    private static readonly Brush TitleNormal = Frozen(Color.FromRgb(0xDC, 0xE1, 0xEB));
    private static readonly Brush TitleStrong = Frozen(Color.FromRgb(0xF2, 0xF5, 0xFA));
    private static readonly Brush MetaNormal = Frozen(Color.FromRgb(0x6E, 0x76, 0x87));
    private static readonly Brush MetaAccent = Frozen(Color.FromRgb(0x79, 0xAE, 0xF9));

    private bool _isExpanded;
    private bool _isRead;

    private StreamEvent()
    {
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsMail { get; private init; }

    public string ServerId { get; private init; } = string.Empty;

    public DateTimeOffset Sort { get; private init; }

    public DateTimeOffset Day { get; private init; }

    public EasOccurrence? Occurrence { get; private init; }

    public Geometry Icon { get; private init; } = CalendarGeometry;

    public Brush TileBrush { get; private init; } = BlueTile;

    public Brush AccentBrush { get; private init; } = Blue;

    public string Time { get; private init; } = string.Empty;

    public string Title { get; private init; } = string.Empty;

    public string Subtitle { get; private init; } = string.Empty;

    public string Meta { get; private init; } = string.Empty;

    public Brush MetaBrush { get; private init; } = MetaNormal;

    public FontWeight MetaWeight { get; private init; } = FontWeights.Normal;

    public string Body { get; private init; } = string.Empty;

    public string SearchText { get; private init; } = string.Empty;

    public Visibility NeedsResponseVisibility { get; private init; } = Visibility.Collapsed;

    public string Link { get; private init; } = string.Empty;

    public bool IsUnread => IsMail && !_isRead;

    public Visibility UnreadVisibility => IsUnread ? Visibility.Visible : Visibility.Collapsed;

    public FontWeight TitleWeight => IsUnread ? FontWeights.SemiBold : FontWeights.Normal;

    public Brush TitleBrush => IsUnread ? TitleStrong : TitleNormal;

    public Visibility ExpandedVisibility => _isExpanded ? Visibility.Visible : Visibility.Collapsed;

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
            Raise(nameof(IsExpanded));
        }
    }

    public void MarkRead()
    {
        if (_isRead)
        {
            return;
        }

        _isRead = true;
        Raise(nameof(IsUnread));
        Raise(nameof(UnreadVisibility));
        Raise(nameof(TitleWeight));
        Raise(nameof(TitleBrush));
    }

    public static StreamEvent FromMeeting(EasOccurrence occurrence, DateTimeOffset now)
    {
        var start = occurrence.Start.ToLocalTime();
        var end = occurrence.End.ToLocalTime();
        var running = occurrence.Start <= now && occurrence.End >= now;
        var needsResponse = occurrence.NeedsResponse;

        var accent = running ? Teal : needsResponse ? Orange : Blue;
        var tile = running ? TealTile : needsResponse ? OrangeTile : BlueTile;

        string meta;
        Brush metaBrush = MetaNormal;
        var metaWeight = FontWeights.Normal;

        if (running)
        {
            meta = $"осталось {Humanize(occurrence.End - now)}";
            metaBrush = MetaAccent;
            metaWeight = FontWeights.SemiBold;
        }
        else if (occurrence.Start - now < TimeSpan.FromHours(1))
        {
            meta = $"через {Humanize(occurrence.Start - now)}";
            metaBrush = MetaAccent;
            metaWeight = FontWeights.SemiBold;
        }
        else
        {
            meta = $"{start:HH:mm}–{end:HH:mm}";
        }

        return new StreamEvent
        {
            IsMail = false,
            ServerId = occurrence.ServerId,
            Sort = occurrence.Start,
            Day = new DateTimeOffset(start.Date, start.Offset),
            Occurrence = occurrence,
            Icon = CalendarGeometry,
            TileBrush = tile,
            AccentBrush = accent,
            Time = start.ToString("HH:mm"),
            Title = occurrence.Subject,
            Subtitle = MeetingSubtitle(occurrence),
            Meta = meta,
            MetaBrush = metaBrush,
            MetaWeight = metaWeight,
            Link = occurrence.OnlineMeetingLink ?? string.Empty,
            NeedsResponseVisibility = needsResponse ? Visibility.Visible : Visibility.Collapsed,
            SearchText = string.Join(' ',
                occurrence.Subject,
                occurrence.Location,
                occurrence.OrganizerName,
                occurrence.OrganizerEmail,
                string.Join(' ', occurrence.Attendees.Select(a => a.DisplayName)))
        };
    }

    public static StreamEvent FromMail(EasMessage message, DateTimeOffset now)
    {
        var received = (message.DateReceived ?? now).ToLocalTime();
        var important = message.Importance >= 2;

        return new StreamEvent
        {
            IsMail = true,
            ServerId = message.ServerId,
            Sort = message.DateReceived ?? now,
            Day = new DateTimeOffset(received.Date, received.Offset),
            Icon = MailGeometry,
            TileBrush = important ? RoseTile : VioletTile,
            AccentBrush = important ? Rose : Violet,
            Time = received.ToString("HH:mm"),
            Title = message.DisplaySubject,
            Subtitle = message.DisplaySender,
            Meta = Ago(now - received),
            Body = string.IsNullOrWhiteSpace(message.Body)
                ? message.Preview ?? "(пустое письмо)"
                : message.Body,
            _isRead = message.IsRead,
            SearchText = string.Join(' ',
                message.DisplaySubject,
                message.DisplaySender,
                message.FromAddress,
                message.Preview)
        };
    }

    private static string MeetingSubtitle(EasOccurrence occurrence)
    {
        var parts = new List<string>();

        var location = occurrence.Location?.Trim();
        if (!string.IsNullOrEmpty(location))
        {
            parts.Add(location.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? "онлайн"
                : location.Length > 30 ? location[..29] + "…" : location);
        }

        if (occurrence.Attendees.Count > 0)
        {
            var count = occurrence.Attendees.Count;
            parts.Add($"{count} {Plural(count, "участник", "участника", "участников")}");
        }
        else if (!string.IsNullOrWhiteSpace(occurrence.OrganizerName))
        {
            parts.Add(occurrence.OrganizerName);
        }

        return parts.Count == 0 ? "Без места" : string.Join(" · ", parts);
    }

    public static string Plural(int count, string one, string few, string many)
    {
        var hundred = count % 100;

        if (hundred is >= 11 and <= 14)
        {
            return many;
        }

        return (count % 10) switch
        {
            1 => one,
            2 or 3 or 4 => few,
            _ => many
        };
    }

    public static string Humanize(TimeSpan span)
    {
        if (span < TimeSpan.Zero)
        {
            span = TimeSpan.Zero;
        }

        if (span.TotalMinutes < 60)
        {
            return $"{Math.Max(1, (int)Math.Round(span.TotalMinutes))} мин";
        }

        if (span.TotalHours < 24)
        {
            var hours = (int)span.TotalHours;
            var minutes = span.Minutes;
            return minutes == 0 ? $"{hours} ч" : $"{hours} ч {minutes} мин";
        }

        var days = (int)span.TotalDays;
        return $"{days} д";
    }

    private static string Ago(TimeSpan span)
    {
        if (span < TimeSpan.FromMinutes(1))
        {
            return "только что";
        }

        if (span < TimeSpan.FromDays(1))
        {
            return $"{Humanize(span)} назад";
        }

        return span.TotalDays < 2 ? "вчера" : $"{(int)span.TotalDays} д назад";
    }

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private void Raise(string property)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }
}