using OwaWidget.Eas.Wbxml;

namespace OwaWidget.Eas.Commands;

public sealed record FetchResult(int Status, WbxmlElement? Properties);

public static class ItemOperationsCommand
{
    public static async Task<FetchResult> FetchAsync(
        EasClient client,
        string collectionId,
        string serverId,
        int truncationSize,
        CancellationToken cancellationToken = default)
    {
        var writer = new WbxmlWriter();
        writer.Start(WbxmlCodePages.ItemOperations, "ItemOperations");
        writer.Start(WbxmlCodePages.ItemOperations, "Fetch");
        writer.Element(WbxmlCodePages.ItemOperations, "Store", "Mailbox");
        writer.Element(WbxmlCodePages.AirSync, "CollectionId", collectionId);
        writer.Element(WbxmlCodePages.AirSync, "ServerId", serverId);

        writer.Start(WbxmlCodePages.ItemOperations, "Options");
        writer.Start(WbxmlCodePages.AirSyncBase, "BodyPreference");
        writer.Element(WbxmlCodePages.AirSyncBase, "Type", 1);
        writer.Element(WbxmlCodePages.AirSyncBase, "TruncationSize", truncationSize);
        writer.EndElement();
        writer.EndElement();

        writer.EndElement();
        writer.EndElement();

        var root = await client.SendAsync("ItemOperations", writer, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (root is null)
        {
            return new FetchResult(0, null);
        }

        var globalStatus = root.ChildInt("Status");
        if (globalStatus is not null && globalStatus != 1)
        {
            throw new EasStatusException("ItemOperations", globalStatus.Value,
                $"ItemOperations failed with status {globalStatus}.");
        }

        var fetch = root.Descendant("Response", "Fetch");
        if (fetch is null)
        {
            return new FetchResult(globalStatus ?? 0, null);
        }

        return new FetchResult(fetch.ChildInt("Status") ?? 1, fetch.Child("Properties"));
    }
}