using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using OwaWidget.Eas.Models;

namespace OwaWidget.App.Services;

public sealed class MailCache
{
    public int Version { get; set; }

    public string? CollectionId { get; set; }

    public string? DeviceId { get; set; }

    public DateTimeOffset SavedAt { get; set; }

    public List<EasMessage> Messages { get; set; } = new();
}

public sealed class CalendarCache
{
    public int Version { get; set; }

    public string? CollectionId { get; set; }

    public string? DeviceId { get; set; }

    public DateTimeOffset SavedAt { get; set; }

    public List<EasAppointment> Appointments { get; set; } = new();
}

public static class CacheStore
{
    public const int CurrentVersion = 2;

    private const int RetainAppointmentDays = 7;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        IgnoreReadOnlyProperties = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string MailPath => Path.Combine(AppStorage.Directory, "cache-mail.json");

    public static string CalendarPath => Path.Combine(AppStorage.Directory, "cache-calendar.json");

    public static List<EasMessage>? LoadMail(AppState state)
    {
        var cache = AppStorage.Load<MailCache>(MailPath, Options);

        if (!IsUsable(cache?.Version, cache?.DeviceId, cache?.CollectionId, state.DeviceId, state.InboxId))
        {
            return null;
        }

        return cache!.Messages;
    }

    public static List<EasAppointment>? LoadCalendar(AppState state)
    {
        var cache = AppStorage.Load<CalendarCache>(CalendarPath, Options);

        if (!IsUsable(cache?.Version, cache?.DeviceId, cache?.CollectionId, state.DeviceId, state.CalendarId))
        {
            return null;
        }

        return cache!.Appointments;
    }

    public static void SaveMail(AppState state, IReadOnlyList<EasMessage> messages)
    {
        AppStorage.Write(MailPath, new MailCache
        {
            Version = CurrentVersion,
            CollectionId = state.InboxId,
            DeviceId = state.DeviceId,
            SavedAt = DateTimeOffset.Now,
            Messages = messages.ToList()
        }, Options);
    }

    public static void SaveCalendar(AppState state, IReadOnlyList<EasAppointment> appointments)
    {
        AppStorage.Write(CalendarPath, new CalendarCache
        {
            Version = CurrentVersion,
            CollectionId = state.CalendarId,
            DeviceId = state.DeviceId,
            SavedAt = DateTimeOffset.Now,
            Appointments = Prune(appointments)
        }, Options);
    }

    public static void Clear()
    {
        Delete(MailPath);
        Delete(CalendarPath);
    }

    public static List<EasAppointment> Prune(IReadOnlyList<EasAppointment> appointments)
    {
        var cutoff = DateTimeOffset.Now.AddDays(-RetainAppointmentDays);

        return appointments.Where(appointment =>
        {
            if (appointment.Recurrence is { } recurrence)
            {
                return recurrence.Until is null || recurrence.Until >= cutoff;
            }

            return appointment.End is null || appointment.End >= cutoff;
        }).ToList();
    }

    private static bool IsUsable(
        int? version,
        string? cachedDeviceId,
        string? cachedCollectionId,
        string deviceId,
        string? collectionId)
    {
        return version == CurrentVersion &&
               cachedDeviceId == deviceId &&
               collectionId is not null &&
               cachedCollectionId == collectionId;
    }

    private static void Delete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception)
        {
        }
    }
}