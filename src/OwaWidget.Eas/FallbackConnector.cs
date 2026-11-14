using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace OwaWidget.Eas;

internal sealed class FallbackConnector : IDisposable
{
    private static readonly string[] DohEndpoints =
    [
        "https://cloudflare-dns.com/dns-query?type=A&name=",
        "https://dns.google/resolve?type=A&name="
    ];

    private static readonly TimeSpan DohTimeout = TimeSpan.FromSeconds(10);

    private readonly HttpClient _doh;
    private readonly Action<string>? _trace;

    public FallbackConnector(bool useSystemProxy, Action<string>? trace)
    {
        _trace = trace;
        _doh = new HttpClient(new SocketsHttpHandler { UseProxy = useSystemProxy })
        {
            Timeout = DohTimeout
        };
        _doh.DefaultRequestHeaders.Accept.ParseAdd("application/dns-json");
    }

    public async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var host = context.DnsEndPoint.Host;
        var port = context.DnsEndPoint.Port;

        IPAddress[] addresses;
        try
        {
            addresses = await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
        }
        catch (SocketException exception) when (IsResolutionFailure(exception.SocketErrorCode))
        {
            addresses = await ResolvePublicAsync(host, cancellationToken).ConfigureAwait(false);
            if (addresses.Length == 0)
            {
                throw;
            }

            _trace?.Invoke($"system DNS failed for {host} ({exception.SocketErrorCode}), using public DNS: {string.Join(", ", addresses.Select(a => a.ToString()))}");
        }

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(addresses, port, cancellationToken).ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        _doh.Dispose();
    }

    private async Task<IPAddress[]> ResolvePublicAsync(string host, CancellationToken cancellationToken)
    {
        foreach (var endpoint in DohEndpoints)
        {
            try
            {
                await using var stream = await _doh.GetStreamAsync(endpoint + Uri.EscapeDataString(host), cancellationToken)
                    .ConfigureAwait(false);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                var addresses = ParseAnswers(document.RootElement);
                if (addresses.Length > 0)
                {
                    return addresses;
                }
            }
            catch (Exception exception) when (exception is HttpRequestException or JsonException
                                              || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested))
            {
                _trace?.Invoke($"public DNS {new Uri(endpoint).Host} failed for {host}: {exception.GetType().Name}: {exception.Message}");
            }
        }

        return [];
    }

    private static IPAddress[] ParseAnswers(JsonElement root)
    {
        if (!root.TryGetProperty("Answer", out var answers) || answers.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new List<IPAddress>();
        foreach (var answer in answers.EnumerateArray())
        {
            if (answer.TryGetProperty("type", out var type) && type.TryGetInt32(out var code) && code == 1 &&
                answer.TryGetProperty("data", out var data) &&
                IPAddress.TryParse(data.GetString(), out var address))
            {
                result.Add(address);
            }
        }

        return result.ToArray();
    }

    private static bool IsResolutionFailure(SocketError error)
    {
        return error is SocketError.HostNotFound or SocketError.TryAgain or SocketError.NoData or SocketError.NoRecovery;
    }
}