using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using OwaWidget.Eas;

namespace OwaWidget.App.Services;

public sealed class AppSettings
{
    public string Server { get; set; } = string.Empty;

    public string User { get; set; } = string.Empty;

    public int ReminderMinutes { get; set; } = 5;

    public int MailFilterType { get; set; } = 3;

    public int CalendarFilterType { get; set; } = 5;

    public bool QuietHoursEnabled { get; set; }

    public int QuietFromHour { get; set; } = 22;

    public int QuietToHour { get; set; } = 8;

    public int BurstThreshold { get; set; } = 3;

    public int BurstWindowSeconds { get; set; } = 60;

    public List<string> IgnoredSenders { get; set; } = new();

    public List<string> RecentSearches { get; set; } = new();

    public bool StartWithWindows { get; set; }

    public bool RespectAppointmentReminder { get; set; } = true;
}

public sealed class AppState
{
    public string DeviceId { get; set; } = EasOptions.NewDeviceId();

    public string? PolicyKey { get; set; }

    public string FolderSyncKey { get; set; } = "0";

    public string MailSyncKey { get; set; } = "0";

    public string CalendarSyncKey { get; set; } = "0";

    public string? InboxId { get; set; }

    public string? CalendarId { get; set; }
}

public static class AppStorage
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string Directory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OwaWidget");

    public static string SettingsPath => Path.Combine(Directory, "settings.json");

    public static string StatePath => Path.Combine(Directory, "state.json");

    public static AppSettings LoadSettings()
    {
        return Load<AppSettings>(SettingsPath) ?? new AppSettings();
    }

    public static AppState LoadState()
    {
        return Load<AppState>(StatePath) ?? new AppState();
    }

    public static void Save(AppSettings settings)
    {
        Write(SettingsPath, settings);
    }

    public static void Save(AppState state)
    {
        Write(StatePath, state);
    }

    internal static T? Load<T>(string path, JsonSerializerOptions? options = null) where T : class
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), options ?? Options);
        }
        catch (Exception)
        {
            return null;
        }
    }

    internal static void Write<T>(string path, T value, JsonSerializerOptions? options = null)
    {
        System.IO.Directory.CreateDirectory(Directory);

        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(value, options ?? Options));
        File.Move(temporary, path, overwrite: true);
    }
}