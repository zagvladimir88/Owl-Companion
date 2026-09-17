using Microsoft.Toolkit.Uwp.Notifications;
using OwaWidget.Eas.Models;
using OwaWidget.Eas.Recurrence;

namespace OwaWidget.App.Services;

public sealed class NotificationService
{
    private const string MailGroup = "mail";
    private const string ReminderGroup = "reminder";

    private readonly AppSettings _settings;
    private readonly Queue<DateTimeOffset> _recent = new();

    public NotificationService(AppSettings settings)
    {
        _settings = settings;
    }

    public void ShowMail(IReadOnlyList<EasMessage> messages)
    {
        if (messages.Count == 0 || IsQuietHour())
        {
            return;
        }

        var relevant = messages.Where(IsRelevant).ToList();
        if (relevant.Count == 0)
        {
            return;
        }

        RegisterBurst(relevant.Count);

        if (relevant.Count > _settings.BurstThreshold || RecentCount() > _settings.BurstThreshold)
        {
            ShowSummary(relevant);
            return;
        }

        foreach (var message in relevant)
        {
            ShowSingleMail(message);
        }
    }

    public void ShowReminder(EasOccurrence occurrence, int minutesBefore)
    {
        var time = occurrence.Start.ToLocalTime().ToString("HH:mm");
        var place = string.IsNullOrWhiteSpace(occurrence.Location) ? null : occurrence.Location;

        var builder = new ToastContentBuilder()
            .AddArgument("action", "openCalendar")
            .AddText($"Через {minutesBefore} мин: {occurrence.Subject}")
            .AddText(place is null ? $"Начало в {time}" : $"Начало в {time} · {Shorten(place, 60)}");

        if (occurrence.OnlineMeetingLink is not null)
        {
            builder.AddButton(new ToastButton()
                .SetContent("Подключиться")
                .SetProtocolActivation(new Uri(occurrence.OnlineMeetingLink)));
        }

        builder
            .AddButton(new ToastButton().SetContent("Открыть календарь")
                .SetProtocolActivation(new Uri($"{_settings.Server}/owa/#path=/calendar")))
            .AddAudio(new Uri("ms-winsoundevent:Notification.Reminder"))
            .SetToastScenario(ToastScenario.Reminder);

        Show(builder, ReminderGroup, $"{occurrence.ServerId}-{occurrence.Start:yyyyMMddHHmm}");
    }

    public void ShowError(string title, string details)
    {
        var builder = new ToastContentBuilder()
            .AddArgument("action", "settings")
            .AddText(title)
            .AddText(details);

        Show(builder, "system", "error");
    }

    private void ShowSingleMail(EasMessage message)
    {
        var builder = new ToastContentBuilder()
            .AddArgument("action", "openMail")
            .AddArgument("id", message.ServerId)
            .AddText(message.DisplaySender)
            .AddText(message.DisplaySubject);

        if (!string.IsNullOrWhiteSpace(message.Preview))
        {
            builder.AddText(Shorten(message.Preview, 120));
        }

        builder
            .AddButton(new ToastButton()
                .SetContent("Открыть")
                .SetProtocolActivation(new Uri($"{_settings.Server}/owa/#path=/mail")))
            .AddButton(new ToastButton()
                .SetContent("Прочитано")
                .AddArgument("action", "markRead")
                .AddArgument("id", message.ServerId)
                .SetBackgroundActivation())
            .AddAudio(new Uri("ms-winsoundevent:Notification.Mail"));

        Show(builder, MailGroup, message.ServerId);
    }

    private void ShowSummary(IReadOnlyList<EasMessage> messages)
    {
        var senders = messages
            .Select(m => m.DisplaySender)
            .Distinct()
            .Take(4)
            .ToList();

        var builder = new ToastContentBuilder()
            .AddArgument("action", "openMail")
            .AddText($"{messages.Count} {Plural(messages.Count)}")
            .AddText(string.Join(", ", senders) + (messages.Count > senders.Count ? " и другие" : string.Empty))
            .AddButton(new ToastButton()
                .SetContent("Открыть почту")
                .SetProtocolActivation(new Uri($"{_settings.Server}/owa/#path=/mail")));

        Show(builder, MailGroup, "summary");
    }

    private static void Show(ToastContentBuilder builder, string group, string tag)
    {
        try
        {
            builder.Show(toast =>
            {
                toast.Group = group;
                toast.Tag = Sanitize(tag);
                toast.ExpirationTime = DateTimeOffset.Now.AddHours(6);
            });

            Log.Info($"toast shown, group={group}");
        }
        catch (Exception exception)
        {
            Log.Error($"toast failed, group={group}", exception);
        }
    }

    private bool IsRelevant(EasMessage message)
    {
        if (message.IsMeetingCancellation || message.IsMeetingResponse)
        {
            return false;
        }

        if (_settings.IgnoredSenders.Count == 0)
        {
            return true;
        }

        var sender = $"{message.FromName} {message.FromAddress}".ToLowerInvariant();

        foreach (var ignored in _settings.IgnoredSenders)
        {
            if (!string.IsNullOrWhiteSpace(ignored) &&
                sender.Contains(ignored.ToLowerInvariant(), StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private bool IsQuietHour()
    {
        if (!_settings.QuietHoursEnabled)
        {
            return false;
        }

        var hour = DateTime.Now.Hour;
        var from = _settings.QuietFromHour;
        var to = _settings.QuietToHour;

        return from <= to ? hour >= from && hour < to : hour >= from || hour < to;
    }

    private void RegisterBurst(int count)
    {
        var now = DateTimeOffset.Now;

        for (var i = 0; i < count; i++)
        {
            _recent.Enqueue(now);
        }

        Trim(now);
    }

    private int RecentCount()
    {
        Trim(DateTimeOffset.Now);
        return _recent.Count;
    }

    private void Trim(DateTimeOffset now)
    {
        var window = TimeSpan.FromSeconds(_settings.BurstWindowSeconds);

        while (_recent.Count > 0 && now - _recent.Peek() > window)
        {
            _recent.Dequeue();
        }
    }

    private static string Sanitize(string value)
    {
        var cleaned = new string(value.Where(char.IsLetterOrDigit).ToArray());
        return cleaned.Length > 60 ? cleaned[..60] : cleaned;
    }

    private static string Shorten(string value, int length)
    {
        return value.Length <= length ? value : value[..(length - 1)] + "…";
    }

    private static string Plural(int count)
    {
        var mod100 = count % 100;
        var mod10 = count % 10;

        if (mod100 is >= 11 and <= 14)
        {
            return "новых писем";
        }

        return mod10 switch
        {
            1 => "новое письмо",
            2 or 3 or 4 => "новых письма",
            _ => "новых писем"
        };
    }
}