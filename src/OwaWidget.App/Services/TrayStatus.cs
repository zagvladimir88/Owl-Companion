using OwaWidget.Eas.Recurrence;

namespace OwaWidget.App.Services;

public enum TrayState
{
    Free,
    Soon,
    Running,
    Offline
}

public sealed record TrayPresentation(TrayState State, double Fill, string Tooltip);

public static class TrayStatus
{
    private const int LeadMinutes = 60;

    public static TrayPresentation Compute(
        IReadOnlyList<EasOccurrence> occurrences,
        int unreadCount,
        bool offline,
        string status,
        DateTimeOffset now)
    {
        var next = occurrences
            .Where(o => o.End >= now && !o.AllDay)
            .OrderBy(o => o.Start)
            .FirstOrDefault();

        if (offline)
        {
            return new TrayPresentation(TrayState.Offline, 0, Tooltip(next, unreadCount, status, now));
        }

        if (next is null)
        {
            return new TrayPresentation(TrayState.Free, 0, Tooltip(null, unreadCount, status, now));
        }

        if (next.Start <= now)
        {
            return new TrayPresentation(TrayState.Running, 1, Tooltip(next, unreadCount, status, now));
        }

        var minutes = (next.Start - now).TotalMinutes;

        if (minutes > LeadMinutes)
        {
            return new TrayPresentation(TrayState.Free, 0, Tooltip(next, unreadCount, status, now));
        }

        var fill = Math.Clamp(1 - minutes / LeadMinutes, 0, 1);
        return new TrayPresentation(TrayState.Soon, fill, Tooltip(next, unreadCount, status, now));
    }

    private static string Tooltip(EasOccurrence? next, int unreadCount, string status, DateTimeOffset now)
    {
        string head;

        if (next is null)
        {
            head = "Встреч нет";
        }
        else if (next.Start <= now)
        {
            head = $"Идёт · осталось {Minutes(next.End - now)}";
        }
        else
        {
            head = $"Через {Minutes(next.Start - now)} · {next.Start.ToLocalTime():HH:mm}";
        }

        if (unreadCount > 0)
        {
            head += $" · писем {unreadCount}";
        }

        var text = $"{head}\n{status}";
        return text.Length > 62 ? text[..62] : text;
    }

    private static string Minutes(TimeSpan span)
    {
        if (span < TimeSpan.Zero)
        {
            span = TimeSpan.Zero;
        }

        return span.TotalMinutes < 60
            ? $"{Math.Max(1, (int)Math.Round(span.TotalMinutes))} мин"
            : $"{(int)span.TotalHours} ч {span.Minutes} мин";
    }
}