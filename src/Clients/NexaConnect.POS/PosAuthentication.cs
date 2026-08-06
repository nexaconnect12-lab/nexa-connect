using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

namespace NexaConnect.POS;

public sealed record PosTokenSet(
    string AccessToken,
    string? RefreshToken,
    DateTimeOffset ExpiresAtUtc,
    string TokenType);

public sealed class PosAuthentication : IDisposable
{
    private const int MaxCallbackLength = 4096;
    private readonly PosClientConfiguration _configuration;
    private readonly HttpClient _httpClient = new();
    private readonly WindowsTokenStore _tokenStore = new();
    private readonly object _sync = new();
    private TaskCompletionSource<PosTokenSet>? _pending;
    private PkceRequest? _pkce;
    private int _callbackConsumed;

    public PosAuthentication(PosClientConfiguration configuration)
    {
        _configuration = configuration;
        CurrentToken = _tokenStore.Load();
    }

    public event EventHandler<string>? StatusChanged;

    public PosTokenSet? CurrentToken { get; private set; }

    public void SignOut()
    {
        lock (_sync)
        {
            _pending?.TrySetCanceled();
            _pending = null;
            _pkce = null;
            _callbackConsumed = 0;
        }

        CurrentToken = null;
        _tokenStore.Delete();
        StatusChanged?.Invoke(this, "Signed out. Stored credentials were cleared.");
    }

    public async Task<PosTokenSet> SignInAsync(CancellationToken cancellationToken = default)
    {
        TaskCompletionSource<PosTokenSet> completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        PkceRequest pkce = Pkce.Create();
        lock (_sync)
        {
            if (_pending is not null)
            {
                throw new InvalidOperationException("A POS sign-in is already in progress.");
            }

            _pkce = pkce;
            _pending = completion;
            _callbackConsumed = 0;
        }

        try
        {
            string authorizeUri = BuildAuthorizeUri(pkce);
            Process.Start(new ProcessStartInfo(authorizeUri) { UseShellExecute = true });
            StatusChanged?.Invoke(this, "Complete sign-in in your browser…");
            using (cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken)))
            {
                return await completion.Task;
            }
        }
        finally
        {
            lock (_sync)
            {
                _pending = null;
                _pkce = null;
            }
        }
    }

    public async Task HandleCallbackAsync(string callbackUri)
    {
        if (callbackUri.Length > MaxCallbackLength ||
            !Uri.TryCreate(callbackUri, UriKind.Absolute, out Uri? callback) ||
            !string.Equals(callback.Scheme, "nexaconnect-pos", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(callback.Host, "oauth", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(callback.AbsolutePath, "/callback", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Dictionary<string, string> values = ParseQuery(callback.Query);
        TaskCompletionSource<PosTokenSet>? completion;
        PkceRequest? pkce;
        lock (_sync)
        {
            completion = _pending;
            pkce = _pkce;
        }

        if (completion is null || pkce is null)
        {
            return;
        }

        try
        {
            if (!values.TryGetValue("state", out string? state) ||
                !CryptographicOperations.FixedTimeEquals(
                    System.Text.Encoding.UTF8.GetBytes(state),
                    System.Text.Encoding.UTF8.GetBytes(pkce.State)))
            {
                throw new InvalidOperationException("The sign-in response state was invalid.");
            }

            if (values.TryGetValue("error", out string? error))
            {
                throw new OperationCanceledException($"Identity provider returned {error}.");
            }

            if (Interlocked.Exchange(ref _callbackConsumed, 1) != 0)
            {
                throw new InvalidOperationException("The sign-in response was already processed.");
            }

            if (!values.TryGetValue("code", out string? code) || string.IsNullOrWhiteSpace(code))
            {
                throw new InvalidOperationException("The sign-in response did not contain an authorization code.");
            }

            PosTokenSet token = await RedeemCodeAsync(code, pkce.Verifier);
            CurrentToken = token;
            _tokenStore.Save(token);
            StatusChanged?.Invoke(this, "Sign-in completed.");
            completion.TrySetResult(token);
        }
        catch (Exception exception)
        {
            completion.TrySetException(exception);
        }
    }

    private string BuildAuthorizeUri(PkceRequest pkce)
    {
        string endpoint = _configuration.Authority.TrimEnd('/') + "/protocol/openid-connect/auth";
        var query = new Dictionary<string, string>
        {
            ["client_id"] = _configuration.ClientId,
            ["response_type"] = "code",
            ["redirect_uri"] = _configuration.RedirectUri,
            ["scope"] = _configuration.Scopes,
            ["state"] = pkce.State,
            ["code_challenge"] = pkce.Challenge,
            ["code_challenge_method"] = "S256"
        };
        return endpoint + "?" + string.Join(
            "&",
            query.Select(pair => Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value)));
    }

    private async Task<PosTokenSet> RedeemCodeAsync(string code, string verifier)
    {
        string endpoint = _configuration.Authority.TrimEnd('/') + "/protocol/openid-connect/token";
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = _configuration.ClientId,
            ["code"] = code,
            ["redirect_uri"] = _configuration.RedirectUri,
            ["code_verifier"] = verifier
        });
        using HttpResponseMessage response = await _httpClient.PostAsync(endpoint, content);
        response.EnsureSuccessStatusCode();
        using JsonDocument document = await response.Content.ReadFromJsonAsync<JsonDocument>()
            ?? throw new InvalidDataException("The token response was empty.");
        JsonElement root = document.RootElement;
        string accessToken = root.GetProperty("access_token").GetString()
            ?? throw new InvalidDataException("The token response did not contain an access token.");
        int expiresIn = root.TryGetProperty("expires_in", out JsonElement expiry)
            ? expiry.GetInt32()
            : 300;
        return new PosTokenSet(
            accessToken,
            root.TryGetProperty("refresh_token", out JsonElement refresh)
                ? refresh.GetString()
                : null,
            DateTimeOffset.UtcNow.AddSeconds(expiresIn),
            root.TryGetProperty("token_type", out JsonElement type)
                ? type.GetString() ?? "Bearer"
                : "Bearer");
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = pair.Split('=', 2);
            if (parts.Length == 2)
            {
                values[Uri.UnescapeDataString(parts[0])] = Uri.UnescapeDataString(parts[1]);
            }
        }

        return values;
    }

    public void Dispose() => _httpClient.Dispose();
}
