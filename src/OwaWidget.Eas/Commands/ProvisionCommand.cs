using OwaWidget.Eas.Wbxml;

namespace OwaWidget.Eas.Commands;

public static class ProvisionCommand
{
    private const string PolicyType = "MS-EAS-Provisioning-WBXML";

    public static async Task<string> AcquirePolicyKeyAsync(
        EasClient client,
        EasOptions options,
        CancellationToken cancellationToken = default)
    {
        var temporaryKey = await RequestPolicyAsync(client, options, cancellationToken).ConfigureAwait(false);
        var finalKey = await AcknowledgePolicyAsync(client, temporaryKey, cancellationToken).ConfigureAwait(false);

        client.PolicyKey = finalKey;
        return finalKey;
    }

    private static async Task<string> RequestPolicyAsync(
        EasClient client,
        EasOptions options,
        CancellationToken cancellationToken)
    {
        var writer = new WbxmlWriter();
        writer.Start(WbxmlCodePages.Provision, "Provision");

        writer.Start(WbxmlCodePages.Settings, "DeviceInformation");
        writer.Start(WbxmlCodePages.Settings, "Set");
        writer.Element(WbxmlCodePages.Settings, "Model", options.DeviceType);
        writer.Element(WbxmlCodePages.Settings, "FriendlyName", "OWA Widget");
        writer.Element(WbxmlCodePages.Settings, "OS", Environment.OSVersion.VersionString);
        writer.Element(WbxmlCodePages.Settings, "UserAgent", options.UserAgent);
        writer.EndElement();
        writer.EndElement();

        writer.Start(WbxmlCodePages.Provision, "Policies");
        writer.Start(WbxmlCodePages.Provision, "Policy");
        writer.Element(WbxmlCodePages.Provision, "PolicyType", PolicyType);
        writer.EndElement();
        writer.EndElement();

        writer.EndElement();

        var previousKey = client.PolicyKey;
        client.PolicyKey = "0";

        try
        {
            var root = await client.SendAsync("Provision", writer, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return ExtractPolicyKey(root, "initial Provision");
        }
        catch
        {
            client.PolicyKey = previousKey;
            throw;
        }
    }

    private static async Task<string> AcknowledgePolicyAsync(
        EasClient client,
        string temporaryKey,
        CancellationToken cancellationToken)
    {
        var writer = new WbxmlWriter();
        writer.Start(WbxmlCodePages.Provision, "Provision");
        writer.Start(WbxmlCodePages.Provision, "Policies");
        writer.Start(WbxmlCodePages.Provision, "Policy");
        writer.Element(WbxmlCodePages.Provision, "PolicyType", PolicyType);
        writer.Element(WbxmlCodePages.Provision, "PolicyKey", temporaryKey);
        writer.Element(WbxmlCodePages.Provision, "Status", 1);
        writer.EndElement();
        writer.EndElement();
        writer.EndElement();

        client.PolicyKey = temporaryKey;

        var root = await client.SendAsync("Provision", writer, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return ExtractPolicyKey(root, "Provision acknowledgement");
    }

    private static string ExtractPolicyKey(WbxmlElement? root, string stage)
    {
        if (root is null)
        {
            throw new EasException($"{stage} returned an empty response.");
        }

        EasClient.RequireStatus(root, "Provision");

        var policy = root.Descendant("Policies", "Policy");
        if (policy is null)
        {
            throw new EasException($"{stage} returned no Policy element.");
        }

        var policyStatus = policy.ChildInt("Status");
        if (policyStatus is not null && policyStatus != 1)
        {
            throw new EasStatusException("Provision", policyStatus.Value,
                $"{stage} returned policy status {policyStatus}.");
        }

        var key = policy.ChildText("PolicyKey");
        if (string.IsNullOrEmpty(key))
        {
            throw new EasException($"{stage} returned no PolicyKey.");
        }

        return key;
    }
}