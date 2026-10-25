using System.Net;
using System.Net.Http;
using System.Text.Json;
using FusionNet.Core.Abstractions;
using FusionNet.Core.Models;

namespace FusionNet.Windows.Network;

/// <summary>
/// Resolves the machine's public IP through several independent HTTPS services, with a short
/// timeout per endpoint, ISP/country enrichment when the endpoint provides it, and a five-minute
/// success cache. Every failure becomes a <see cref="PublicIpInfo"/> with <c>ErrorText</c>; this
/// service never throws at the UI, even when the machine is offline.
/// </summary>
public sealed class PublicIpService : IPublicIpService
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan FailureCacheDuration = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan EndpointTimeout = TimeSpan.FromSeconds(4);

    private static readonly HttpClient Http = CreateHttpClient();

    private static readonly Endpoint[] Endpoints =
    [
        new("ipinfo.io", "https://ipinfo.io/json", EndpointKind.IpInfoJson),
        new("ipify.org", "https://api.ipify.org?format=json", EndpointKind.IpifyJson),
        new("ifconfig.me", "https://ifconfig.me/ip", EndpointKind.PlainText)
    ];

    private readonly SemaphoreSlim _mutex = new(1, 1);
    private readonly object _cacheGate = new();

    private PublicIpInfo? _cachedSuccess;
    private DateTimeOffset _cachedAt;
    private PublicIpInfo? _cachedFailure;
    private DateTimeOffset _failureAt;

    public async Task<PublicIpInfo> GetAsync(CancellationToken cancellationToken = default)
    {
        var cached = TryGetCached();
        if (cached is not null) return cached;

        try
        {
            await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return new PublicIpInfo(null, null, null, "cancelled");
        }

        try
        {
            cached = TryGetCached();
            if (cached is not null) return cached;

            var info = await QueryEndpointsAsync(cancellationToken).ConfigureAwait(false);
            StoreInCache(info);
            return info;
        }
        catch (OperationCanceledException)
        {
            return new PublicIpInfo(null, null, null, "cancelled");
        }
        catch (Exception ex)
        {
            var failure = new PublicIpInfo(null, null, null, ex.Message);
            StoreInCache(failure);
            return failure;
        }
        finally
        {
            _mutex.Release();
        }
    }

    private PublicIpInfo? TryGetCached()
    {
        lock (_cacheGate)
        {
            var now = DateTimeOffset.UtcNow;
            if (_cachedSuccess is not null && now - _cachedAt < CacheDuration) return _cachedSuccess;
            if (_cachedFailure is not null && now - _failureAt < FailureCacheDuration) return _cachedFailure;
            return null;
        }
    }

    private void StoreInCache(PublicIpInfo info)
    {
        lock (_cacheGate)
        {
            var now = DateTimeOffset.UtcNow;
            if (info.Success)
            {
                _cachedSuccess = info;
                _cachedAt = now;
                _cachedFailure = null;
            }
            else
            {
                _cachedFailure = info;
                _failureAt = now;
            }
        }
    }

    private static async Task<PublicIpInfo> QueryEndpointsAsync(CancellationToken cancellationToken)
    {
        var errors = new List<string>(Endpoints.Length);

        foreach (var endpoint in Endpoints)
        {
            if (cancellationToken.IsCancellationRequested)
                return new PublicIpInfo(null, null, null, "cancelled");

            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(EndpointTimeout);

                using var request = new HttpRequestMessage(HttpMethod.Get, endpoint.Url);
                using var response = await Http
                    .SendAsync(request, HttpCompletionOption.ResponseContentRead, timeoutCts.Token)
                    .ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    errors.Add($"{endpoint.Name}: HTTP {(int)response.StatusCode}");
                    continue;
                }

                var body = await response.Content.ReadAsStringAsync(timeoutCts.Token).ConfigureAwait(false);
                var info = ParseBody(endpoint, body);
                if (info.Success) return info;

                errors.Add($"{endpoint.Name}: {info.ErrorText}");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return new PublicIpInfo(null, null, null, "cancelled");
            }
            catch (OperationCanceledException)
            {
                errors.Add($"{endpoint.Name}: timeout");
            }
            catch (Exception ex)
            {
                errors.Add($"{endpoint.Name}: {ex.Message}");
            }
        }

        var summary = string.Join(" | ", errors);
        if (summary.Length > 300) summary = summary[..300] + "…";
        return new PublicIpInfo(null, null, null,
            errors.Count == 0 ? "no public IP endpoint is configured" : "all public IP services failed: " + summary);
    }

    private static PublicIpInfo ParseBody(Endpoint endpoint, string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return new PublicIpInfo(null, null, null, "empty response");

        if (endpoint.Kind == EndpointKind.PlainText)
        {
            var firstLine = body.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault();
            return IsPublicIp(firstLine)
                ? new PublicIpInfo(firstLine, null, null)
                : new PublicIpInfo(null, null, null, "response did not contain an IP address");
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return new PublicIpInfo(null, null, null, "unexpected JSON shape");

            var ip = root.TryGetProperty("ip", out var ipElement) && ipElement.ValueKind == JsonValueKind.String
                ? ipElement.GetString()
                : null;
            if (!IsPublicIp(ip))
                return new PublicIpInfo(null, null, null, "JSON did not contain a valid IP address");

            var isp = root.TryGetProperty("org", out var orgElement) && orgElement.ValueKind == JsonValueKind.String
                ? NormalizeIsp(orgElement.GetString())
                : null;
            var country = root.TryGetProperty("country", out var countryElement) && countryElement.ValueKind == JsonValueKind.String
                ? countryElement.GetString()?.Trim()
                : null;

            return new PublicIpInfo(ip!.Trim(), isp, string.IsNullOrWhiteSpace(country) ? null : country);
        }
        catch (JsonException ex)
        {
            return new PublicIpInfo(null, null, null, "invalid JSON: " + ex.Message);
        }
    }

    private static bool IsPublicIp(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var trimmed = value.Trim();
        if (trimmed.Length > 64) return false;
        return IPAddress.TryParse(trimmed, out _);
    }

    /// <summary>ipinfo returns organisations as "AS15169 Google LLC"; users care about the name.</summary>
    private static string? NormalizeIsp(string? organisation)
    {
        if (string.IsNullOrWhiteSpace(organisation)) return null;

        var value = organisation.Trim();
        if (value.StartsWith("AS", StringComparison.OrdinalIgnoreCase))
        {
            var space = value.IndexOf(' ');
            // Strip the "AS<number>" prefix: "AS15169 Google LLC" -> "Google LLC".
            if (space > 2 && int.TryParse(value[2..space], out _))
            {
                value = value[(space + 1)..].Trim();
            }
        }

        return value.Length == 0 ? null : value;
    }

    private static HttpClient CreateHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(3),
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        };

        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(8)
        };

        client.DefaultRequestHeaders.UserAgent.ParseAdd("FusionNetPro/3.3.3");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
        return client;
    }

    private enum EndpointKind
    {
        IpInfoJson,
        IpifyJson,
        PlainText
    }

    private sealed record Endpoint(string Name, string Url, EndpointKind Kind);
}


