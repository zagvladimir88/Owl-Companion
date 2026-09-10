using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using OwaWidget.Eas.Wbxml;

namespace OwaWidget.Eas;

public sealed class EasClient : IDisposable
{
    private const string WbxmlMediaType = "application/vnd.ms-sync.wbxml";

    private readonly EasOptions _options;
    private readonly HttpClient _http;
    private readonly string _authorization;

    public EasClient(EasOptions options)
    {
        _options = options;

        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseProxy = options.UseSystemProxy,
            AutomaticDecompression = DecompressionMethods.All
        };

        _http = new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };

        var raw = Encoding.UTF8.GetBytes($"{options.User}:{options.Password}");
        _authorization = "Basic " + Convert.ToBase64String(raw);
    }

    public string? PolicyKey { get; set; }

    public async Task<IReadOnlyList<string>> GetSupportedVersionsAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, BuildUri(null));
        ApplyHeaders(request, includePolicyKey: false);

        using var cts = CreateTimeout(_options.DefaultTimeout, cancellationToken);
        using var response = await _http.SendAsync(request, cts.Token).ConfigureAwait(false);

        EnsureSuccess(response, "OPTIONS");

        if (response.Headers.TryGetValues("MS-ASProtocolVersions", out var values))
        {
            return string.Join(',', values).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        return Array.Empty<string>();
    }

    public Task<WbxmlElement?> SendAsync(
        string command,
        WbxmlWriter writer,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        return SendAsync(command, writer.ToArray(), timeout, cancellationToken);
    }

    public async Task<WbxmlElement?> SendAsync(
        string command,
        byte[] body,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, BuildUri(command))
        {
            Content = new ByteArrayContent(body)
        };

        request.Content.Headers.ContentType = new MediaTypeHeaderValue(WbxmlMediaType);
        ApplyHeaders(request);

        using var cts = CreateTimeout(timeout ?? _options.DefaultTimeout, cancellationToken);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, cts.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new EasException($"Command {command} timed out.");
        }

        using (response)
        {
            EnsureSuccess(response, command);

            var payload = await response.Content.ReadAsByteArrayAsync(cts.Token).ConfigureAwait(false);
            if (payload.Length == 0)
            {
                return null;
            }

            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (mediaType is not null &&
                !mediaType.Contains("wbxml", StringComparison.OrdinalIgnoreCase))
            {
                throw new EasException(
                    $"Command {command} returned {mediaType} instead of WBXML. First bytes: {Preview(payload)}");
            }

            return WbxmlReader.Parse(payload);
        }
    }

    public static int RequireStatus(WbxmlElement root, string command)
    {
        var status = root.ChildInt("Status");
        if (status is null)
        {
            throw new EasException($"Command {command} returned no Status element.");
        }

        if (status != 1)
        {
            throw new EasStatusException(command, status.Value, DescribeStatus(command, status.Value));
        }

        return status.Value;
    }

    public void Dispose()
    {
        _http.Dispose();
    }

    private Uri BuildUri(string? command)
    {
        var builder = new UriBuilder(_options.ServerUri)
        {
            Path = "/Microsoft-Server-ActiveSync"
        };

        if (command is not null)
        {
            builder.Query = string.Concat(
                "Cmd=", Uri.EscapeDataString(command),
                "&User=", Uri.EscapeDataString(_options.User),
                "&DeviceId=", Uri.EscapeDataString(_options.DeviceId),
                "&DeviceType=", Uri.EscapeDataString(_options.DeviceType));
        }

        return builder.Uri;
    }

    private void ApplyHeaders(HttpRequestMessage request, bool includePolicyKey = true)
    {
        request.Headers.TryAddWithoutValidation("Authorization", _authorization);
        request.Headers.TryAddWithoutValidation("MS-ASProtocolVersion", _options.ProtocolVersion);
        request.Headers.TryAddWithoutValidation("User-Agent", _options.UserAgent);
        request.Headers.TryAddWithoutValidation("Accept", "*/*");

        if (includePolicyKey)
        {
            request.Headers.TryAddWithoutValidation("X-MS-PolicyKey", PolicyKey ?? "0");
        }
    }

    private static CancellationTokenSource CreateTimeout(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);
        return cts;
    }

    private static void EnsureSuccess(HttpResponseMessage response, string command)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var code = (int)response.StatusCode;

        var redirect = response.Headers.TryGetValues("X-MS-Location", out var location)
            ? location.FirstOrDefault()
            : null;

        TimeSpan? retryAfter = null;
        if (response.Headers.TryGetValues("Retry-After", out var retryValues) &&
            int.TryParse(retryValues.FirstOrDefault(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
        {
            retryAfter = TimeSpan.FromSeconds(seconds);
        }

        var message = code switch
        {
            401 => "Authentication failed: check login and password.",
            403 => "Access forbidden: ActiveSync may be disabled for this mailbox.",
            449 => "Server requires provisioning.",
            451 => $"Mailbox moved to another server: {redirect}",
            503 => "Server is throttling requests.",
            _ => $"Server returned HTTP {code}."
        };

        throw new EasHttpException(response.StatusCode, command, message)
        {
            RetryAfter = retryAfter,
            RedirectTo = redirect
        };
    }

    private static string DescribeStatus(string command, int status)
    {
        var meaning = status switch
        {
            3 => "invalid synchronization key, full resync required",
            4 => "protocol error in request",
            5 => "server error, retry later",
            7 => "object conflict",
            8 => "object not found",
            12 => "folder hierarchy changed, run FolderSync",
            14 => "invalid heartbeat interval",
            141 => "device is not provisionable",
            142 => "device is not provisioned",
            143 => "policy refresh required",
            144 => "invalid policy key",
            _ => "see MS-ASCMD status codes"
        };

        return $"Command {command} failed with status {status} ({meaning}).";
    }

    private static string Preview(byte[] payload)
    {
        var length = Math.Min(payload.Length, 80);
        return Encoding.UTF8.GetString(payload, 0, length).Replace('\n', ' ').Replace('\r', ' ');
    }
}