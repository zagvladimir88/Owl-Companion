namespace OwaWidget.Eas;

public sealed class EasOptions
{
    public required Uri ServerUri { get; init; }

    public required string User { get; init; }

    public required string Password { get; init; }

    public required string DeviceId { get; init; }

    public string DeviceType { get; init; } = "OwaWidget";

    public string ProtocolVersion { get; init; } = "14.1";

    public string UserAgent { get; init; } = "OwaWidget/1.0";

    public bool UseSystemProxy { get; init; }

    public TimeSpan DefaultTimeout { get; init; } = TimeSpan.FromSeconds(60);

    public static string NewDeviceId()
    {
        return Guid.NewGuid().ToString("N");
    }
}