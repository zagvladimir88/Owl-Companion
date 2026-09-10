using OwaWidget.Eas.Models;
using OwaWidget.Eas.Wbxml;

namespace OwaWidget.Eas.Commands;

public sealed record FolderSyncResult(
    string SyncKey,
    IReadOnlyList<EasFolder> Changed,
    IReadOnlyList<string> Deleted);

public static class FolderSyncCommand
{
    public static async Task<FolderSyncResult> ExecuteAsync(
        EasClient client,
        string syncKey = "0",
        CancellationToken cancellationToken = default)
    {
        var writer = new WbxmlWriter();
        writer.Start(WbxmlCodePages.FolderHierarchy, "FolderSync");
        writer.Element(WbxmlCodePages.FolderHierarchy, "SyncKey", syncKey);
        writer.EndElement();

        var root = await client.SendAsync("FolderSync", writer, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (root is null)
        {
            throw new EasException("FolderSync returned an empty response.");
        }

        EasClient.RequireStatus(root, "FolderSync");

        var newSyncKey = root.ChildText("SyncKey");
        if (string.IsNullOrEmpty(newSyncKey))
        {
            throw new EasException("FolderSync returned no SyncKey.");
        }

        var changed = new List<EasFolder>();
        var deleted = new List<string>();

        var changes = root.Child("Changes");
        if (changes is not null)
        {
            foreach (var element in changes.ChildrenNamed("Add"))
            {
                changed.Add(ParseFolder(element));
            }

            foreach (var element in changes.ChildrenNamed("Update"))
            {
                changed.Add(ParseFolder(element));
            }

            foreach (var element in changes.ChildrenNamed("Delete"))
            {
                var serverId = element.ChildText("ServerId");
                if (!string.IsNullOrEmpty(serverId))
                {
                    deleted.Add(serverId);
                }
            }
        }

        return new FolderSyncResult(newSyncKey, changed, deleted);
    }

    private static EasFolder ParseFolder(WbxmlElement element)
    {
        return new EasFolder(
            element.ChildText("ServerId") ?? string.Empty,
            element.ChildText("ParentId") ?? string.Empty,
            element.ChildText("DisplayName") ?? string.Empty,
            (EasFolderType)(element.ChildInt("Type") ?? 0));
    }
}