using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;

namespace JvmBridge.Build.Infrastructure;

internal sealed class HttpService : IDisposable
{
    private readonly ConcurrentDictionary<string, JsonNode> _cache = new();
    private readonly HttpClient _client;
    private readonly bool _ownsClient;

    public HttpService()
        : this(new HttpClient(), ownsClient: true)
    {
    }

    internal HttpService(HttpClient client, bool ownsClient = false)
    {
        _client = client;
        _ownsClient = ownsClient;
        _client.Timeout = Timeout.InfiniteTimeSpan;
        _client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(productName: "JvmBridge.Build", productVersion: "1.0"));
    }

    public void Dispose()
    {
        if (_ownsClient)
            _client.Dispose();
    }

    public async Task DownloadFileAsync(string url, string path, CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < 4; attempt++)
        {
            using CancellationTokenSource requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            requestTimeout.CancelAfter(TimeSpan.FromMinutes(minutes: 10));

            using HttpRequestMessage request = CreateRequest(url);

            try
            {
                using HttpResponseMessage response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, requestTimeout.Token).ConfigureAwait(continueOnCapturedContext: false);

                if (!response.IsSuccessStatusCode)
                {
                    if (attempt < 3 && IsTransient(response.StatusCode))
                    {
                        await DelayAsync(attempt, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

                        continue;
                    }

                    throw new InvalidOperationException($"Download failed HTTP {(int)response.StatusCode}: {url}");
                }

                using Stream input = await response.Content.ReadAsStreamAsync(requestTimeout.Token).ConfigureAwait(continueOnCapturedContext: false);

                using FileStream output = new(path, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, useAsync: true);

                await input.CopyToAsync(output, requestTimeout.Token).ConfigureAwait(continueOnCapturedContext: false);

                return;
            }
            catch (Exception failure) when (attempt < 3 && failure is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                await DelayAsync(attempt, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
            }
        }

        throw new InvalidOperationException($"Download failed after retries: {url}");
    }

    public async Task<JsonNode?> GetJsonAsync(string url, bool missing = false, CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(url, out JsonNode? cached))
            return cached;

        string? payload = await GetStringAsync(url, missing, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

        if (payload is null)
            return null;

        JsonNode value = JsonNode.Parse(payload) ?? throw new InvalidOperationException($"Empty JSON response: {url}");
        _cache[url] = value;

        return value;
    }

    public async Task<string?> GetStringAsync(string url, bool missing = false, CancellationToken cancellationToken = default)
    {
        for (int attempt = 0; attempt < 4; attempt++)
        {
            using CancellationTokenSource requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            requestTimeout.CancelAfter(TimeSpan.FromSeconds(seconds: 90));

            using HttpRequestMessage request = CreateRequest(url);

            try
            {
                using HttpResponseMessage response = await _client.SendAsync(request, HttpCompletionOption.ResponseContentRead, requestTimeout.Token).ConfigureAwait(continueOnCapturedContext: false);

                if (missing && response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
                    return null;

                if (response.IsSuccessStatusCode)
                    return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

                if (attempt < 3 && IsTransient(response.StatusCode))
                {
                    await DelayAsync(attempt, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

                    continue;
                }

                throw new InvalidOperationException($"Download failed HTTP {(int)response.StatusCode}: {url}");
            }
            catch (Exception failure) when (attempt < 3 && failure is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                await DelayAsync(attempt, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
            }
        }

        throw new InvalidOperationException($"Download failed after retries: {url}");
    }

    private static HttpRequestMessage CreateRequest(string url)
    {
        HttpRequestMessage request = new(HttpMethod.Get, url);
        Uri uri = request.RequestUri ?? throw new InvalidOperationException(message: "HTTP request URI is missing.");
        string? token = Environment.GetEnvironmentVariable(variable: "GH_TOKEN") ?? Environment.GetEnvironmentVariable(variable: "GITHUB_TOKEN");

        if (!string.IsNullOrEmpty(token) && string.Equals(uri.Host, b: "api.github.com", StringComparison.OrdinalIgnoreCase))
            request.Headers.Authorization = new AuthenticationHeaderValue(scheme: "Bearer", token);

        return request;
    }

    private static Task DelayAsync(int attempt, CancellationToken cancellationToken)
    {
        return Task.Delay(TimeSpan.FromSeconds(Math.Pow(x: 2, attempt)), cancellationToken);
    }

    private static bool IsTransient(HttpStatusCode status)
    {
        return status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests || (int)status >= 500;
    }
}
