using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using NexaConnect.Observability;

namespace NexaConnect.POS;

public sealed class PosAuthentication : IDisposable
{
    private const int MaxCallbackLength = 4096;
    private readonly PosClientConfiguration _configuration;
    private readonly HttpClient _httpClient;
    private readonly IPosTokenStore _tokenStore;
    private readonly object _sync = new();
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private TaskCompletionSource<PosTokenSet>? _pending;
    private PkceRequest? _pkce;
    private int _callbackConsumed;
    private bool _requiresInteractiveSignIn;
    private readonly Action<string> _launchBrowser;
    private readonly ILoggerFactory _loggerFactory = NexaConnectObservabilityExtensions.CreateClientLoggerFactory("nexaconnect-pos-client");
    private Func<bool> _hasRetainedWork = () => false;

    public PosAuthentication(PosClientConfiguration configuration)
        : this(configuration, new HttpClientHandler { AllowAutoRedirect = false }, new WindowsTokenStore())
    {
    }

    internal PosAuthentication(PosClientConfiguration configuration, HttpMessageHandler handler,
        IPosTokenStore tokenStore, Action<string>? launchBrowser = null)
    {
        _configuration = configuration;
        configuration.ValidateCheckout();
        _httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20), MaxResponseContentBufferSize = 1024 * 1024 };
        _tokenStore = tokenStore;
        _launchBrowser = launchBrowser ?? (uri =>
        {
            if (Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true, Verb = "open" }) is null)
                throw new InvalidOperationException("Windows could not open secure sign-in.");
        });
        CurrentToken = _tokenStore.Load();
        if (CurrentToken?.LockedAtUtc is not null)
        {
            _requiresInteractiveSignIn = true;
        }
        else if (CurrentToken is not null)
        {
            RequireInteractiveSignIn();
        }
    }

    public event EventHandler<string>? StatusChanged;

    public PosTokenSet? CurrentToken { get; private set; }
    public bool RequiresInteractiveSignIn => _requiresInteractiveSignIn;
    public bool HasVerifiedOperator => HasOperator(CurrentToken);

    // The UI supplies only whether retained work exists; authentication never mutates it.
    public void SetRetainedWorkGuard(Func<bool> hasRetainedWork) =>
        _hasRetainedWork = hasRetainedWork ?? throw new ArgumentNullException(nameof(hasRetainedWork));

    private bool HasOperator(PosTokenSet? token) => !string.IsNullOrWhiteSpace(token?.Subject)
        && string.Equals(token.Authority, _configuration.Authority.TrimEnd('/'), StringComparison.Ordinal);

    public void SignOut()
    {
        lock (_sync)
        {
            if (_hasRetainedWork())
                throw new PosSessionResumeException("Resolve retained work before signing out. Lock the screen to pause instead.");
            _pending?.TrySetCanceled();
            _pending = null;
            _pkce = null;
            _callbackConsumed = 0;
            _tokenStore.Delete();
            CurrentToken = null;
            _requiresInteractiveSignIn = false;
        }
        StatusChanged?.Invoke(this, "Signed out. Stored credentials were cleared.");
    }

    public async Task<PosTokenSet> SignInAsync(bool forceReauthentication = false,
        CancellationToken cancellationToken = default)
    {
        TaskCompletionSource<PosTokenSet> completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        PkceRequest pkce = Pkce.Create();
        PosTokenSet? expectedOperator;
        lock (_sync)
        {
            if (_pending is not null)
            {
                throw new InvalidOperationException("A POS sign-in is already in progress.");
            }

            expectedOperator = CurrentToken;
            if (_hasRetainedWork() && !HasOperator(expectedOperator))
                throw new PosSessionResumeException("Retained work has no verified cashier binding. Preserve recovery and request original-cashier reconciliation before upgrading or resuming.");

            _pkce = pkce;
            _pending = completion;
            _callbackConsumed = 0;
        }

        try
        {
            string authorizeUri = BuildAuthorizeUri(pkce, forceReauthentication || _requiresInteractiveSignIn);
            _launchBrowser(authorizeUri);
            StatusChanged?.Invoke(this, "Complete sign-in in your browser…");
            using (cancellationToken.Register(() =>
            {
                lock (_sync) completion.TrySetCanceled(cancellationToken);
            }))
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
                throw new OperationCanceledException("Secure sign-in was not completed.");
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
            lock (_sync)
            {
                // Do not install credentials from a cancelled or superseded attempt.
                if (!ReferenceEquals(_pending, completion) || completion.Task.IsCompleted) return;
                if (HasOperator(CurrentToken) && !SameOperator(CurrentToken!, token))
                {
                    SafeEvent("resume-identity-denied", null);
                    throw new PosSessionResumeException("Resume requires the original cashier. The workspace remains locked and saved work is unchanged.");
                }
                if (_hasRetainedWork() && !HasOperator(CurrentToken))
                    throw new PosSessionResumeException("Retained work has no verified cashier binding. Preserve recovery and request reconciliation.");
                _tokenStore.Save(token);
                CurrentToken = token;
                _requiresInteractiveSignIn = false;
                completion.TrySetResult(token);
            }
            StatusChanged?.Invoke(this, "Sign-in completed.");
        }
        catch (Exception exception)
        {
            completion.TrySetException(exception);
        }
    }

    public async Task<PosTokenSet> RefreshAsync(CancellationToken cancellationToken = default)
    {
        using var correlationScope = CorrelationContext.Push(IdentityCorrelation());
        await _refreshGate.WaitAsync(cancellationToken);
        try
        {
            PosTokenSet current;
            lock (_sync)
            {
                current = CurrentToken ?? throw new PosReauthenticationRequiredException();
            }
            if (_requiresInteractiveSignIn || current.LockedAtUtc is not null ||
                string.IsNullOrWhiteSpace(current.RefreshToken) ||
                PosSessionPolicy.IsAbsoluteExpired(current, DateTimeOffset.UtcNow))
            {
                RequireInteractiveSignIn();
                throw new PosReauthenticationRequiredException();
            }

            string endpoint = _configuration.Authority.TrimEnd('/') + "/protocol/openid-connect/token";
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["client_id"] = _configuration.ClientId,
                ["refresh_token"] = current.RefreshToken
            });
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = content };
            using HttpResponseMessage response = await SendIdentityAsync(request, "refresh", cancellationToken);
            if (response.StatusCode is System.Net.HttpStatusCode.BadRequest or System.Net.HttpStatusCode.Unauthorized)
            {
                lock (_sync)
                {
                    if (!ReferenceEquals(CurrentToken, current))
                        return CurrentToken ?? throw new PosReauthenticationRequiredException();
                    RequireInteractiveSignIn();
                    throw new PosReauthenticationRequiredException();
                }
            }
            response.EnsureSuccessStatusCode();
            using JsonDocument document = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken)
                ?? throw new InvalidDataException("The token refresh response was empty.");
            PosTokenSet refreshed = ReadToken(document.RootElement, current.RefreshToken,
                current.SessionStartedAtUtc!.Value, current.SessionExpiresAtUtc!.Value);
            refreshed = await BindOperatorAsync(refreshed, cancellationToken);
            lock (_sync)
            {
                if (!ReferenceEquals(CurrentToken, current))
                    return CurrentToken ?? throw new PosReauthenticationRequiredException();
                if (!SameOperator(current, refreshed))
                {
                    RequireInteractiveSignIn();
                    SafeEvent("refresh-identity-denied", null);
                    throw new PosReauthenticationRequiredException();
                }
                _tokenStore.Save(refreshed);
                CurrentToken = refreshed;
                _requiresInteractiveSignIn = false;
                return refreshed;
            }
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    public void RequireInteractiveSignIn()
    {
        lock (_sync)
        {
            var marker = new PosTokenSet(string.Empty, null, DateTimeOffset.MinValue, "Bearer",
                null, null, DateTimeOffset.UtcNow, CurrentToken?.Subject, CurrentToken?.Authority);
            CurrentToken = marker;
            _requiresInteractiveSignIn = true;
            _tokenStore.Save(marker);
        }
    }

    private string BuildAuthorizeUri(PkceRequest pkce, bool forceReauthentication)
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
        if (forceReauthentication) query["prompt"] = "login";
        return endpoint + "?" + string.Join(
            "&",
            query.Select(pair => Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value)));
    }

    private async Task<PosTokenSet> RedeemCodeAsync(string code, string verifier)
    {
        using var correlationScope = CorrelationContext.Push(IdentityCorrelation());
        string endpoint = _configuration.Authority.TrimEnd('/') + "/protocol/openid-connect/token";
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = _configuration.ClientId,
            ["code"] = code,
            ["redirect_uri"] = _configuration.RedirectUri,
            ["code_verifier"] = verifier
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = content };
        using HttpResponseMessage response = await SendIdentityAsync(request, "sign-in", CancellationToken.None);
        response.EnsureSuccessStatusCode();
        using JsonDocument document = await response.Content.ReadFromJsonAsync<JsonDocument>()
            ?? throw new InvalidDataException("The token response was empty.");
        JsonElement root = document.RootElement;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return await BindOperatorAsync(ReadToken(root, null, now, now.AddHours(_configuration.SessionAbsoluteTimeoutHours)), CancellationToken.None);
    }

    private static bool SameOperator(PosTokenSet first, PosTokenSet second) =>
        !string.IsNullOrWhiteSpace(first.Subject) && first.Subject == second.Subject && first.Authority == second.Authority;

    private async Task<PosTokenSet> BindOperatorAsync(PosTokenSet token, CancellationToken cancellationToken)
    {
        // Resolve identity from the configured provider over the authenticated UserInfo endpoint;
        // never trust a locally decoded, unvalidated JWT claim for an unlock decision.
        using var request = new HttpRequestMessage(HttpMethod.Get,
            _configuration.Authority.TrimEnd('/') + "/protocol/openid-connect/userinfo");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        using var response = await SendIdentityAsync(request, "operator-identity", cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken)
            ?? throw new InvalidDataException("The identity response was empty.");
        if (!document.RootElement.TryGetProperty("sub", out var value) || value.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(value.GetString()) || value.GetString()!.Length > 256
            || value.GetString()!.Any(char.IsControl))
            throw new InvalidDataException("The identity response did not establish a cashier.");
        return token with { Subject = value.GetString(), Authority = _configuration.Authority.TrimEnd('/') };
    }

    private async Task<HttpResponseMessage> SendIdentityAsync(HttpRequestMessage request, string operation,
        CancellationToken cancellationToken)
    {
        string correlation = IdentityCorrelation();
        request.Headers.Add("X-Correlation-ID", correlation);
        try
        {
            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) SafeEvent(operation, (int)response.StatusCode, correlation);
            return response;
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException)
        {
            SafeEvent(operation, null, correlation);
            throw;
        }
    }

    private static string IdentityCorrelation()
    {
        string? value = CorrelationContext.Current;
        return value is { Length: > 0 and <= 128 }
            && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or ':' or '-')
            ? value : Guid.NewGuid().ToString("N");
    }

    private void SafeEvent(string category, int? status, string? correlation = null) =>
        _loggerFactory.CreateLogger("NexaConnect.POS.Session").LogWarning(
            "POS session boundary {Category} {StatusCode} {CorrelationId}", category, status, correlation ?? IdentityCorrelation());

    private static PosTokenSet ReadToken(JsonElement root, string? fallbackRefreshToken,
        DateTimeOffset sessionStartedAtUtc, DateTimeOffset sessionExpiresAtUtc)
    {
        string accessToken = root.GetProperty("access_token").GetString()
            ?? throw new InvalidDataException("The token response did not contain an access token.");
        int expiresIn = root.TryGetProperty("expires_in", out JsonElement expiry)
            ? expiry.GetInt32()
            : 300;
        string? refreshToken = root.TryGetProperty("refresh_token", out JsonElement refresh)
            ? refresh.GetString()
            : fallbackRefreshToken;
        return new PosTokenSet(accessToken, refreshToken, DateTimeOffset.UtcNow.AddSeconds(expiresIn),
            root.TryGetProperty("token_type", out JsonElement type) ? type.GetString() ?? "Bearer" : "Bearer",
            sessionStartedAtUtc, sessionExpiresAtUtc);
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

    public void Dispose()
    {
        _refreshGate.Dispose();
        _httpClient.Dispose();
        _loggerFactory.Dispose();
    }
}

public sealed class PosSessionResumeException(string message) : Exception(message);

public sealed class PosReauthenticationRequiredException : Exception
{
    public PosReauthenticationRequiredException() : base("The POS session requires sign-in.") { }
}
