using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace MedGuard.DeviceSimulator.Demo;

/// <summary>
/// Thin client for MedGuard.API: a cached client_credentials token, and the
/// Response&lt;T&gt; envelope unwrapped, so callers see data or an <see cref="ApiException"/>.
/// </summary>
public sealed class MedGuardApi : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly DemoOptions _options;
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _token;
    private DateTime _tokenRenewAtUtc;

    public MedGuardApi(DemoOptions options)
    {
        _options = options;
        // A free-tier host can take a while to wake up; a sensor would rather wait than drop.
        _http = new HttpClient { BaseAddress = new Uri(options.ApiBaseUrl.TrimEnd('/') + "/api/"), Timeout = TimeSpan.FromSeconds(60) };
    }

    public Task<T> GetAsync<T>(string path, CancellationToken ct) => SendAsync<T>(HttpMethod.Get, path, null, auth: true, ct);

    public Task<T> PostAsync<T>(string path, object? body, CancellationToken ct) => SendAsync<T>(HttpMethod.Post, path, body, auth: true, ct);

    /// <summary>The heartbeat endpoint is anonymous: a sensor has no user.</summary>
    public Task<T> PostAnonymousAsync<T>(string path, object? body, CancellationToken ct) => SendAsync<T>(HttpMethod.Post, path, body, auth: false, ct);

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, bool auth, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            using var request = new HttpRequestMessage(method, path);
            if (body is not null) request.Content = JsonContent.Create(body, options: Json);
            if (auth) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await GetTokenAsync(attempt > 1, ct));

            using var response = await _http.SendAsync(request, ct);
            // One retry with a fresh token: the cached one may have been revoked or
            // the IdentityServer restarted with new signing keys.
            if (response.StatusCode == HttpStatusCode.Unauthorized && auth && attempt == 1) continue;

            var text = await response.Content.ReadAsStringAsync(ct);
            Envelope<T>? envelope = null;
            try { envelope = JsonSerializer.Deserialize<Envelope<T>>(text, Json); }
            catch (JsonException) { /* not an envelope, e.g. an HTML error page from the host */ }

            if (!response.IsSuccessStatusCode || envelope is null || !envelope.Succeeded)
            {
                var message = envelope?.Message ?? (text.Length > 200 ? text[..200] : text);
                throw new ApiException(response.StatusCode, $"{method} {path} -> {(int)response.StatusCode}: {message}");
            }
            return envelope.Data!;
        }
    }

    private async Task<string> GetTokenAsync(bool forceRenew, CancellationToken ct)
    {
        if (!forceRenew && _token is not null && DateTime.UtcNow < _tokenRenewAtUtc) return _token;

        await _tokenLock.WaitAsync(ct);
        try
        {
            if (!forceRenew && _token is not null && DateTime.UtcNow < _tokenRenewAtUtc) return _token;

            using var response = await _http.PostAsync($"{_options.Authority.TrimEnd('/')}/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = _options.ClientId,
                ["client_secret"] = _options.ClientSecret,
                ["scope"] = _options.Scope,
            }), ct);
            var token = await response.Content.ReadFromJsonAsync<TokenResponse>(Json, ct);
            if (!response.IsSuccessStatusCode || token?.AccessToken is null)
                throw new ApiException(response.StatusCode, $"token request failed ({(int)response.StatusCode}) - check Demo:ClientSecret");

            _token = token.AccessToken;
            // Renew a minute early so a request never leaves with a token about to expire.
            _tokenRenewAtUtc = DateTime.UtcNow.AddSeconds(Math.Max(60, token.ExpiresIn - 60));
            return _token;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    public void Dispose()
    {
        _http.Dispose();
        _tokenLock.Dispose();
    }

    private sealed record Envelope<T>(bool Succeeded, string? Message, T? Data);

    private sealed record TokenResponse(
        [property: System.Text.Json.Serialization.JsonPropertyName("access_token")] string? AccessToken,
        [property: System.Text.Json.Serialization.JsonPropertyName("expires_in")] int ExpiresIn);
}

public sealed class ApiException(HttpStatusCode status, string message) : Exception(message)
{
    public HttpStatusCode Status { get; } = status;
}

// The API's DTOs, as far as the demo needs them. Enums travel as numbers.
public enum BatchState { Active = 0, InTransit = 1, Delivered = 2, Quarantined = 3, Recalled = 4 }
public enum ShipmentState { Preparing = 0, InTransit = 1, Delivered = 2, Aborted = 3 }

public sealed record BatchInfo(Guid Id, string BatchNumber, string DrugName, decimal MinSafeTemperatureC, decimal MaxSafeTemperatureC, BatchState Status);
public sealed record DeviceInfo(Guid Id, string DeviceCode, string Model, Guid? AssignedBatchId);
public sealed record ShipmentInfo(Guid Id, Guid BatchId, string OriginLocation, string DestinationLocation, ShipmentState Status, DateTime? DepartedAtUtc);
public sealed record AlertInfo(Guid Id, Guid BatchId, string BatchNumber, int Severity, DateTime TriggeredAtUtc);
public sealed record SettingsInfo(decimal WarningMarginPercent);
