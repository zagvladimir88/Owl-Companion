using OwaWidget.Eas.Wbxml;

namespace OwaWidget.Eas.Commands;

public enum SyncChangeType
{
    Add,
    Change,
    Delete,
    SoftDelete
}

public sealed record SyncChange(SyncChangeType Type, string ServerId, WbxmlElement? ApplicationData);

public sealed record SyncCollectionResult(
    string SyncKey,
    IReadOnlyList<SyncChange> Changes,
    bool MoreAvailable);

public sealed record SyncFlagChange(string ServerId, bool Read);

public sealed class SyncRequest
{
    public required string CollectionId { get; init; }

    public IReadOnlyList<SyncFlagChange>? ClientChanges { get; init; }

    public string SyncKey { get; init; } = "0";

    public int WindowSize { get; init; } = 25;

    public int? FilterType { get; init; }

    public int? BodyType { get; init; }

    public int BodyTruncationSize { get; init; } = 512;

    public TimeSpan? Timeout { get; init; }
}

public static class SyncCommand
{
    public static async Task<SyncCollectionResult> ExecuteAsync(
        EasClient client,
        SyncRequest request,
        CancellationToken cancellationToken = default)
    {
        var writer = BuildRequest(request);

        var root = await client.SendAsync("Sync", writer, request.Timeout, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (root is null)
        {
            return new SyncCollectionResult(request.SyncKey, Array.Empty<SyncChange>(), false);
        }

        var globalStatus = root.ChildInt("Status");
        if (globalStatus is not null && globalStatus != 1)
        {
            throw new EasStatusException("Sync", globalStatus.Value, $"Sync failed with status {globalStatus}.");
        }

        var collection = root.Descendant("Collections", "Collection");
        if (collection is null)
        {
            throw new EasException("Sync returned no Collection element.");
        }

        var status = collection.ChildInt("Status");
        if (status is not null && status != 1)
        {
            throw new EasStatusException("Sync", status.Value, $"Sync collection failed with status {status}.");
        }

        var syncKey = collection.ChildText("SyncKey") ?? request.SyncKey;
        var changes = new List<SyncChange>();

        var commands = collection.Child("Commands");
        if (commands is not null)
        {
            foreach (var element in commands.Children)
            {
                var type = element.Name switch
                {
                    "Add" => SyncChangeType.Add,
                    "Change" => SyncChangeType.Change,
                    "Delete" => SyncChangeType.Delete,
                    "SoftDelete" => SyncChangeType.SoftDelete,
                    _ => (SyncChangeType?)null
                };

                if (type is null)
                {
                    continue;
                }

                var serverId = element.ChildText("ServerId") ?? string.Empty;
                changes.Add(new SyncChange(type.Value, serverId, element.Child("ApplicationData")));
            }
        }

        var moreAvailable = collection.Child("MoreAvailable") is not null;

        return new SyncCollectionResult(syncKey, changes, moreAvailable);
    }

    public static async Task<string> InitializeAsync(
        EasClient client,
        string collectionId,
        CancellationToken cancellationToken = default)
    {
        var result = await ExecuteAsync(
            client,
            new SyncRequest { CollectionId = collectionId, SyncKey = "0" },
            cancellationToken).ConfigureAwait(false);

        return result.SyncKey;
    }

    private static WbxmlWriter BuildRequest(SyncRequest request)
    {
        var writer = new WbxmlWriter();
        writer.Start(WbxmlCodePages.AirSync, "Sync");
        writer.Start(WbxmlCodePages.AirSync, "Collections");
        writer.Start(WbxmlCodePages.AirSync, "Collection");

        writer.Element(WbxmlCodePages.AirSync, "SyncKey", request.SyncKey);
        writer.Element(WbxmlCodePages.AirSync, "CollectionId", request.CollectionId);

        if (request.SyncKey != "0")
        {
            writer.Element(WbxmlCodePages.AirSync, "DeletesAsMoves", 1);
            writer.Element(WbxmlCodePages.AirSync, "GetChanges", 1);
            writer.Element(WbxmlCodePages.AirSync, "WindowSize", request.WindowSize);

            if (request.FilterType is not null || request.BodyType is not null)
            {
                writer.Start(WbxmlCodePages.AirSync, "Options");

                if (request.FilterType is not null)
                {
                    writer.Element(WbxmlCodePages.AirSync, "FilterType", request.FilterType.Value);
                }

                if (request.BodyType is not null)
                {
                    writer.Start(WbxmlCodePages.AirSyncBase, "BodyPreference");
                    writer.Element(WbxmlCodePages.AirSyncBase, "Type", request.BodyType.Value);
                    writer.Element(WbxmlCodePages.AirSyncBase, "TruncationSize", request.BodyTruncationSize);
                    writer.EndElement();
                }

                writer.EndElement();
            }

            if (request.ClientChanges is { Count: > 0 })
            {
                writer.Start(WbxmlCodePages.AirSync, "Commands");

                foreach (var change in request.ClientChanges)
                {
                    writer.Start(WbxmlCodePages.AirSync, "Change");
                    writer.Element(WbxmlCodePages.AirSync, "ServerId", change.ServerId);
                    writer.Start(WbxmlCodePages.AirSync, "ApplicationData");
                    writer.Element(WbxmlCodePages.Email, "Read", change.Read ? 1 : 0);
                    writer.EndElement();
                    writer.EndElement();
                }

                writer.EndElement();
            }
        }

        writer.EndElement();
        writer.EndElement();
        writer.EndElement();

        return writer;
    }
}