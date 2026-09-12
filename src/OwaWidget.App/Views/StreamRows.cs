using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using OwaWidget.Eas.Models;
using OwaWidget.Eas.Recurrence;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using FontWeight = System.Windows.FontWeight;

namespace OwaWidget.App.Views;

public static class Palette
{
    public static readonly Brush Ink = Frozen(0x20, 0x1E, 0x1D);
    public static readonly Brush Secondary = Frozen(0x57, 0x52, 0x4F);
    public static readonly Brush Tertiary = Frozen(0x6F, 0x6A, 0x67);
    public static readonly Brush Faint = Frozen(0x8A, 0x84, 0x81);
    public static readonly Brush Line = Frozen(0xCF, 0xCA, 0xC6);
    public static readonly Brush Surface = Frozen(0xF3, 0xF2, 0xF2);
    public static readonly Brush Card = Frozen(0xFF, 0xFF, 0xFF);
    public static readonly Brush Accent = Frozen(0xEC, 0x30, 0x13);
    public static readonly Brush AccentDeep = Frozen(0xB8, 0x1F, 0x05);

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}

public sealed class StreamSection
{
    public required string Label { get; init; }

    public string Note { get; init; } = string.Empty;

    public Brush LabelBrush { get; init; } = Palette.Secondary;
}

public sealed class StreamAttendeeRow
{
    public required string Name { get; init; }

    public required string Kind { get; init; }
}

public sealed class StreamMeeting
{
    public required EasOccurrence Occurrence { get; init; }

    public required string Title { get; init; }

    public required string Meta { get; init; }

    public string TimeColumn { get; init; } = string.Empty;

    public IReadOnlyList<string> Words { get; init; } = Array.Empty<string>();

    public Brush TitleBrush { get; init; } = Palette.Ink;

    public Brush MetaBrush { get; init; } = Palette.Secondary;

    public Visibility TimeColumnVisibility =>
        TimeColumn.Length == 0 ? Visibility.Collapsed : Visibility.Visible;

    public string Link => Occurrence.OnlineMeetingLink ?? string.Empty;

    public Visibility JoinVisibility => Link.Length == 0 ? Visibility.Collapsed : Visibility.Visible;

    public static StreamMeeting CreateForDay(EasOccurrence occurrence)
    {
        var start = occurrence.Start.ToLocalTime();
        var parts = new List<string> { Format.Span(occurrence.End - occurrence.Start) };

        if (occurrence.Attendees.Count > 0)
        {
            parts.Add(occurrence.Attendees.Count.ToString());
        }

        var reply = ReplyLabel(occurrence);
        if (reply is not null)
        {
            parts.Add(reply);
        }

        return new StreamMeeting
        {
            Occurrence = occurrence,
            Title = occurrence.Subject,
            Meta = string.Join(" · ", parts),
            TimeColumn = start.ToString("HH:mm")
        };
    }

    public static StreamMeeting CreateForSearch(
        EasOccurrence occurrence,
        DateTimeOffset now,
        IReadOnlyList<string> words)
    {
        var start = occurrence.Start.ToLocalTime();
        var past = occurrence.End <= now;
        var parts = new List<string> { Format.Span(occurrence.End - occurrence.Start) };

        if (occurrence.Attendees.Count > 0)
        {
            parts.Add(occurrence.Attendees.Count.ToString());
        }

        if (past)
        {
            parts.Add("прошла");
        }
        else if (occurrence.Start <= now)
        {
            parts.Add($"идёт · осталось {Format.Span(occurrence.End - now)}");
        }
        else if (start.Date == now.Date)
        {
            parts.Add($"через {Format.Span(occurrence.Start - now)}");
        }
        else
        {
            var reply = ReplyLabel(occurrence);
            if (reply is not null)
            {
                parts.Add(reply);
            }
        }

        return new StreamMeeting
        {
            Occurrence = occurrence,
            Title = occurrence.Subject,
            Meta = string.Join(" · ", parts),
            TimeColumn = start.ToString("HH:mm"),
            Words = words,
            TitleBrush = past ? Palette.Tertiary : Palette.Ink,
            MetaBrush = past ? Palette.Faint : Palette.Secondary
        };
    }

    private static string? ReplyLabel(EasOccurrence occurrence)
    {
        if (occurrence.NeedsResponse)
        {
            return "вы не ответили";
        }

        return occurrence.ResponseType switch
        {
            EasResponseType.Accepted => "вы приняли",
            EasResponseType.Tentative => "под вопросом",
            EasResponseType.Declined => "вы отклонили",
            _ => null
        };
    }

    public static StreamMeeting Create(EasOccurrence occurrence, DateTimeOffset now, bool showLead)
    {
        var start = occurrence.Start.ToLocalTime();
        var end = occurrence.End.ToLocalTime();

        var parts = new List<string>();

        if (showLead)
        {
            parts.Add(occurrence.Start <= now
                ? $"идёт · осталось {Format.Span(occurrence.End - now)}"
                : $"через {Format.Span(occurrence.Start - now)}");
        }

        parts.Add($"{start:HH:mm}–{end:HH:mm}");

        var length = Format.Span(occurrence.End - occurrence.Start);
        if (!showLead)
        {
            parts.Add(length);
        }

        if (occurrence.Attendees.Count > 0)
        {
            parts.Add(occurrence.Attendees.Count.ToString());
        }

        var reply = ReplyLabel(occurrence);

        if (reply is not null)
        {
            parts.Add(reply);
        }

        return new StreamMeeting
        {
            Occurrence = occurrence,
            Title = occurrence.Subject,
            Meta = string.Join(" · ", parts)
        };
    }
}

public sealed class MailListRow
{
    public required EasMessage Message { get; init; }

    public required string Sender { get; init; }

    public required string SenderNote { get; init; }

    public required string Time { get; init; }

    public required string Subject { get; init; }

    public required string Preview { get; init; }

    public required bool IsUnread { get; init; }

    public required bool IsInvitation { get; init; }

    public Visibility InvitationVisibility =>
        IsInvitation ? Visibility.Visible : Visibility.Collapsed;

    public Visibility DotVisibility => IsUnread ? Visibility.Visible : Visibility.Hidden;

    public Brush RowBackground => IsUnread ? Palette.Card : System.Windows.Media.Brushes.Transparent;

    public Visibility PreviewVisibility =>
        IsUnread && Preview.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    public double SenderSize => IsUnread ? 13 : 12;

    public double SubjectSize => IsUnread ? 14 : 13;

    public FontWeight SenderWeight => IsUnread ? FontWeights.SemiBold : FontWeights.Normal;

    public FontWeight SubjectWeight => IsUnread ? FontWeights.SemiBold : FontWeights.Normal;

    public Brush SenderBrush => IsUnread ? Palette.Ink : Palette.Secondary;

    public Brush SubjectBrush => IsUnread ? Palette.Ink : Palette.Secondary;

    public static MailListRow Create(EasMessage message, DateTimeOffset now, bool unread)
    {
        var received = (message.DateReceived ?? now).ToLocalTime();
        var robot = !message.DisplaySender.Contains(' ');

        return new MailListRow
        {
            Message = message,
            Sender = message.DisplaySender,
            SenderNote = robot ? " · рассылка" : string.Empty,
            Time = received.ToString("HH:mm"),
            Subject = string.IsNullOrWhiteSpace(message.Subject) ? "(без темы)" : message.Subject!,
            Preview = (message.Preview ?? string.Empty).Replace('\n', ' ').Trim(),
            IsUnread = unread,
            IsInvitation = message.IsMeetingRequest
        };
    }
}

public sealed class StreamMail : INotifyPropertyChanged
{
    private bool _isRead;
    private bool _isExpanded;

    public required EasMessage Message { get; init; }

    public required string ServerId { get; init; }

    public required string Line { get; init; }

    public required string Time { get; init; }

    public required string Body { get; init; }

    public required bool Robot { get; init; }

    public IReadOnlyList<string> Words { get; init; } = Array.Empty<string>();

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsUnread => !_isRead;

    public Visibility DotVisibility => _isRead ? Visibility.Hidden : Visibility.Visible;

    public Brush LineBrush => _isRead ? Palette.Secondary : Palette.Ink;

    public FontWeight LineWeight => _isRead ? FontWeights.Normal : FontWeights.SemiBold;

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
        Raise(nameof(DotVisibility));
        Raise(nameof(LineBrush));
        Raise(nameof(LineWeight));
    }

    public static StreamMail Create(
        EasMessage message,
        DateTimeOffset now,
        bool unread,
        IReadOnlyList<string>? words = null)
    {
        var received = (message.DateReceived ?? now).ToLocalTime();
        var sender = ShortSender(message.DisplaySender);

        return new StreamMail
        {
            Message = message,
            ServerId = message.ServerId,
            Line = $"{sender} — {message.DisplaySubject}",
            Time = received.ToString("HH:mm"),
            Robot = !message.DisplaySender.Contains(' '),
            Words = words ?? Array.Empty<string>(),
            Body = string.IsNullOrWhiteSpace(message.Body)
                ? message.Preview ?? "(пустое письмо)"
                : message.Body,
            _isRead = !unread
        };
    }

    private static string ShortSender(string sender)
    {
        var parts = sender.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return parts.Length >= 2 && parts[1].Length > 0
            ? $"{parts[0]} {char.ToUpperInvariant(parts[1][0])}."
            : sender;
    }

    private void Raise(string property)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }
}

public static class Format
{
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

    public static int WholeDays(TimeSpan span)
    {
        return Math.Max(1, (int)Math.Floor(span.TotalDays));
    }

    public static string Gap(TimeSpan span)
    {
        if (span.TotalDays >= 1)
        {
            var days = (int)span.TotalDays;
            var hours = span.Hours;

            var text = $"{days} {Plural(days, "день", "дня", "дней")}";
            return hours == 0 ? text : $"{text} {hours} {Plural(hours, "час", "часа", "часов")}";
        }

        return Span(span);
    }

    public static string Span(TimeSpan span)
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

        return $"{(int)span.TotalDays} д";
    }
}