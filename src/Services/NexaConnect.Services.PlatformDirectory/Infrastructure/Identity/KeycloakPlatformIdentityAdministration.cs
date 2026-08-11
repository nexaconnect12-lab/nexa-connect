using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using NexaConnect.Contracts.Platform;
using NexaConnect.Services.PlatformDirectory.Application.Administration;

namespace NexaConnect.Services.PlatformDirectory.Infrastructure.Identity;

public sealed class KeycloakPlatformIdentityAdministration(HttpClient client, IConfiguration configuration) : IPlatformIdentityAdministration
{
    private readonly string realm = configuration["KeycloakAdmin:Realm"] ?? throw new InvalidOperationException("KeycloakAdmin:Realm is required.");
    private readonly string clientId = configuration["KeycloakAdmin:ClientId"] ?? throw new InvalidOperationException("KeycloakAdmin:ClientId is required.");
    private readonly string clientSecret = configuration["KeycloakAdmin:ClientSecret"] ?? throw new InvalidOperationException("KeycloakAdmin:ClientSecret is required.");

    public async Task<IReadOnlyCollection<PlatformUserSummary>> ListUsersAsync(CancellationToken cancellationToken)
    {
        await AuthorizeAsync(cancellationToken);
        var result = new List<PlatformUserSummary>();
        const int pageSize = 200;
        for (int first = 0;; first += pageSize)
        {
            KeycloakUser[] users = await client.GetFromJsonAsync<KeycloakUser[]>($"admin/realms/{Uri.EscapeDataString(realm)}/users?first={first}&max={pageSize}", cancellationToken) ?? [];
            foreach (KeycloakUser user in users) result.Add(await ToSummaryAsync(user, cancellationToken));
            if (users.Length < pageSize) break;
        }
        return result;
    }

    public async Task<PlatformUserSummary> CreateUserAsync(CreatePlatformUserRequest request, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(cancellationToken);
        using HttpResponseMessage response = await client.PostAsJsonAsync($"admin/realms/{Uri.EscapeDataString(realm)}/users", new { username=request.Username, email=request.Email, enabled=request.Enabled }, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Conflict) throw new ArgumentException("The platform username or email already exists.");
        response.EnsureSuccessStatusCode();
        string subjectId = response.Headers.Location?.Segments.Last().Trim('/') ?? throw new InvalidOperationException("Keycloak did not return the created subject identifier.");
        await SetRolesAsync(subjectId, request.Roles, cancellationToken);
        return (await GetUserAsync(subjectId, cancellationToken))!;
    }

    public async Task<PlatformUserSummary?> UpdateUserAsync(string subjectId, UpdatePlatformUserRequest request, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(cancellationToken);
        KeycloakUser? existing = await GetRawUserAsync(subjectId, cancellationToken); if (existing is null) return null;
        using HttpResponseMessage response = await client.PutAsJsonAsync($"admin/realms/{Uri.EscapeDataString(realm)}/users/{Uri.EscapeDataString(subjectId)}", new { username=existing.Username, email=request.Email, enabled=request.Enabled }, cancellationToken);
        response.EnsureSuccessStatusCode(); return await GetUserAsync(subjectId, cancellationToken);
    }

    public async Task<PlatformUserSummary?> ChangeRolesAsync(string subjectId, IReadOnlyCollection<string> roles, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(cancellationToken); if (await GetRawUserAsync(subjectId, cancellationToken) is null) return null;
        await SetRolesAsync(subjectId, roles, cancellationToken); return await GetUserAsync(subjectId, cancellationToken);
    }

    private async Task AuthorizeAsync(CancellationToken cancellationToken)
    {
        using var body = new FormUrlEncodedContent(new Dictionary<string,string>{{"grant_type","client_credentials"},{"client_id",clientId},{"client_secret",clientSecret}});
        using HttpResponseMessage response = await client.PostAsync($"realms/{Uri.EscapeDataString(realm)}/protocol/openid-connect/token", body, cancellationToken); response.EnsureSuccessStatusCode();
        Token token = (await response.Content.ReadFromJsonAsync<Token>(cancellationToken))!; client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
    }

    private async Task<PlatformUserSummary?> GetUserAsync(string id, CancellationToken ct) { KeycloakUser? user=await GetRawUserAsync(id,ct); return user is null?null:await ToSummaryAsync(user,ct); }
    private async Task<KeycloakUser?> GetRawUserAsync(string id, CancellationToken ct) { using HttpResponseMessage r=await client.GetAsync($"admin/realms/{Uri.EscapeDataString(realm)}/users/{Uri.EscapeDataString(id)}",ct); if(r.StatusCode==HttpStatusCode.NotFound)return null; r.EnsureSuccessStatusCode(); return await r.Content.ReadFromJsonAsync<KeycloakUser>(ct); }
    private async Task<PlatformUserSummary> ToSummaryAsync(KeycloakUser user, CancellationToken ct) => new(user.Id,user.Username,user.Email,user.Enabled,await GetRolesAsync(user.Id,ct));
    private async Task<IReadOnlyCollection<string>> GetRolesAsync(string id,CancellationToken ct) { KeycloakRole[] roles=await client.GetFromJsonAsync<KeycloakRole[]>($"admin/realms/{Uri.EscapeDataString(realm)}/users/{Uri.EscapeDataString(id)}/role-mappings/realm",ct)??[]; return roles.Select(x=>x.Name).Where(Domain.Administration.PlatformRoleCatalog.PermissionsByRole.ContainsKey).Order().ToArray(); }
    private async Task SetRolesAsync(string id,IReadOnlyCollection<string> desired,CancellationToken ct) { KeycloakRole[] available=await client.GetFromJsonAsync<KeycloakRole[]>($"admin/realms/{Uri.EscapeDataString(realm)}/roles",ct)??[]; var platform=available.Where(x=>Domain.Administration.PlatformRoleCatalog.PermissionsByRole.ContainsKey(x.Name)).ToArray(); var current=(await GetRolesAsync(id,ct)).ToHashSet(); var add=platform.Where(x=>desired.Contains(x.Name)&&!current.Contains(x.Name)).ToArray(); var remove=platform.Where(x=>!desired.Contains(x.Name)&&current.Contains(x.Name)).ToArray(); if(add.Length>0){using var r=await client.PostAsJsonAsync($"admin/realms/{Uri.EscapeDataString(realm)}/users/{Uri.EscapeDataString(id)}/role-mappings/realm",add,ct);r.EnsureSuccessStatusCode();} if(remove.Length>0){using var req=new HttpRequestMessage(HttpMethod.Delete,$"admin/realms/{Uri.EscapeDataString(realm)}/users/{Uri.EscapeDataString(id)}/role-mappings/realm"){Content=JsonContent.Create(remove)};using var r=await client.SendAsync(req,ct);r.EnsureSuccessStatusCode();} }
    private sealed record Token([property: System.Text.Json.Serialization.JsonPropertyName("access_token")] string AccessToken);
    private sealed record KeycloakUser(string Id,string Username,string? Email,bool Enabled);
    private sealed record KeycloakRole(string Id,string Name);
}
