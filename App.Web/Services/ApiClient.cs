using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace App.Web.Services;

public class ApiClient
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IHttpClientFactory _httpClientFactory;

    private string? _accessToken;
    private string? _refreshToken;
    private bool _tokensLoaded;

    public ApiClient(IHttpContextAccessor httpContextAccessor, IHttpClientFactory httpClientFactory)
    {
        _httpContextAccessor = httpContextAccessor;
        _httpClientFactory = httpClientFactory;
    }

    private async Task EnsureTokensLoadedAsync()
    {
        if (_tokensLoaded) return;
        var ctx = _httpContextAccessor.HttpContext!;
        _accessToken = await ctx.GetTokenAsync("access_token");
        _refreshToken = await ctx.GetTokenAsync("refresh_token");
        _tokensLoaded = true;
    }

    public async Task<HttpResponseMessage> CallApiAsync(
        HttpMethod method, string path, object? body = null)
    {
        await EnsureTokensLoadedAsync();

        var ctx = _httpContextAccessor.HttpContext!;
        var client = _httpClientFactory.CreateClient("api");

        async Task<HttpResponseMessage> Send()
        {
            var req = new HttpRequestMessage(method, path);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
            if (body is not null)
                req.Content = new StringContent(
                    JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            return await client.SendAsync(req);
        }

        var response = await Send();

        if (response.StatusCode != System.Net.HttpStatusCode.Unauthorized)
            return response;

        if (!await RefreshTokenAsync(ctx, client))
            return response;

        return await Send();
    }

    public async Task<T?> CallApiAsync<T>(string path)
    {
        var response = await CallApiAsync(HttpMethod.Get, path);
        if (!response.IsSuccessStatusCode) return default;

        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
    }

    public async Task<HttpResponseMessage> PostAsync(string path, object body)
        => await CallApiAsync(HttpMethod.Post, path, body);

    public async Task<HttpResponseMessage> PutAsync(string path, object body)
        => await CallApiAsync(HttpMethod.Put, path, body);

    public async Task<HttpResponseMessage> DeleteAsync(string path)
        => await CallApiAsync(HttpMethod.Delete, path);

    private async Task<bool> RefreshTokenAsync(HttpContext ctx, HttpClient client)
    {
        if (string.IsNullOrWhiteSpace(_refreshToken)) return false;

        var response = await client.PostAsync("/connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = _refreshToken,
                ["client_id"] = "app-web",
                ["client_secret"] = "app-web-secret",
            }));

        if (!response.IsSuccessStatusCode) return false;

        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        if (!payload.RootElement.TryGetProperty("access_token", out var at)) return false;
        var newAccessToken = at.GetString();
        if (string.IsNullOrWhiteSpace(newAccessToken)) return false;

        _accessToken = newAccessToken;

        if (payload.RootElement.TryGetProperty("refresh_token", out var rt))
        {
            var newRt = rt.GetString();
            if (!string.IsNullOrWhiteSpace(newRt)) _refreshToken = newRt;
        }

        var authResult = await ctx.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        if (authResult.Succeeded && authResult.Properties is not null)
        {
            authResult.Properties.UpdateTokenValue("access_token", _accessToken);
            authResult.Properties.UpdateTokenValue("refresh_token", _refreshToken ?? "");

            if (payload.RootElement.TryGetProperty("expires_in", out var ei)
                && ei.TryGetInt32(out var expiresIn))
            {
                authResult.Properties.UpdateTokenValue(
                    "expires_at",
                    DateTimeOffset.UtcNow.AddSeconds(expiresIn).ToString("o"));
            }

            await ctx.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                authResult.Principal!,
                authResult.Properties);
        }

        return true;
    }
}
