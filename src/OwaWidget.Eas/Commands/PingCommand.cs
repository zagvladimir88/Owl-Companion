using OwaWidget.Eas.Wbxml;

namespace OwaWidget.Eas.Commands;

public enum PingStatus
{
    Expired = 1,
    Changes = 2,
    MissingParameters = 3,
    SyntaxError = 4,
    InvalidHeartbeat = 5,
    TooManyFolders = 6,
    FolderHierarchyOutOfDate = 7,
    ServerError = 8
}

public sealed record PingFolder(string Id, string Class);

public sealed record PingResult(
    int Status,
    IReadOnlyList<string> ChangedFolders,
    int? SuggestedHeartbeat,
    int? MaxFolders)
{
    public PingStatus Kind => (PingStatus)Status;
}

public static class PingCommand
{
    public const string EmailClass = "Email";
    public const string CalendarClass = "Calendar";

    public const int DefaultHeartbeatSeconds = 470;

    public static async Task<PingResult> ExecuteAsync(
        EasClient client,
        IReadOnlyCollection<PingFolder> folders,
        int heartbeatSeconds = DefaultHeartbeatSeconds,
        CancellationToken cancellationToken = default)
    {
        if (folders.Count == 0)
        {
            throw new ArgumentException("Ping requires at least one folder.", nameof(folders));
        }

        var writer = new WbxmlWriter();
        writer.Start(WbxmlCodePages.Ping, "Ping");
        writer.Element(WbxmlCodePages.Ping, "HeartbeatInterval", heartbeatSeconds);
        writer.Start(WbxmlCodePages.Ping, "Folders");

        foreach (var folder in folders)
        {
            writer.Start(WbxmlCodePages.Ping, "Folder");
            writer.Element(WbxmlCodePages.Ping, "Id", folder.Id);
            writer.Element(WbxmlCodePages.Ping, "Class", folder.Class);
            writer.EndElement();
        }

        writer.EndElement();
        writer.EndElement();

        var timeout = TimeSpan.FromSeconds(heartbeatSeconds + 30);

        var root = await client.SendAsync("Ping", writer, timeout, cancellationToken).ConfigureAwait(false);

        if (root is null)
        {
            return new PingResult((int)PingStatus.Expired, Array.Empty<string>(), null, null);
        }

        var status = root.ChildInt("Status") ?? 0;
        var changed = new List<string>();

        var foldersElement = root.Child("Folders");
        if (foldersElement is not null)
        {
            foreach (var folder in foldersElement.ChildrenNamed("Folder"))
            {
                if (!string.IsNullOrEmpty(folder.Text))
                {
                    changed.Add(folder.Text);
                }
            }
        }

        return new PingResult(
            status,
            changed,
            root.ChildInt("HeartbeatInterval"),
            root.ChildInt("MaxFolders"));
    }
}