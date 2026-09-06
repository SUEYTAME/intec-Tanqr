using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Combustible.Application;
using Combustible.Domain;
using Combustible.Infrastructure.Data;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using OpenIddict.Validation.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Combustible.Api.Endpoints;

// RS-05 / RF-24: clientes de integración con OAuth 2.0 client credentials. Cada cliente actúa con
// un rol del RBAC (RS-02); nunca Administrador ni Despachador (el despacho exige persona, H-05).
public static class OAuthClients
{
    public const string Scope = "combustible.api";
    public static readonly string[] AllowedRoles = [Roles.Supervisor, Roles.Auditor, Roles.Viewer];

    // Tokens OAuth (typ at+jwt, RFC 9068) van al validador de OpenIddict; los de sesión, al JWT propio.
    public static string SelectScheme(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var header = context.Request.Headers.Authorization.ToString();
        if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var token = header[7..].Trim();
            var handler = new JsonWebTokenHandler();
            if (handler.CanReadToken(token) && string.Equals(handler.ReadJsonWebToken(token).Typ, JsonWebTokenTypes.AccessToken, StringComparison.OrdinalIgnoreCase))
                return OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
        }
        return JwtBearerDefaults.AuthenticationScheme;
    }

    public static void MapOAuth(this WebApplication app)
    {
        app.MapPost("/connect/token", async (HttpContext http, IOpenIddictApplicationManager applications, AppDbContext db, AuditWriter audit) =>
        {
            var request = http.GetOpenIddictServerRequest() ?? throw new InvalidOperationException("No es una solicitud OAuth 2.0.");
            // OpenIddict ya autenticó al cliente y rechazó cualquier otro tipo de concesión.
            if (!request.IsClientCredentialsGrantType()) throw new InvalidOperationException("Tipo de concesión no habilitado.");
            var application = await applications.FindByClientIdAsync(request.ClientId!)
                ?? throw new InvalidOperationException("Cliente autenticado inexistente.");
            var properties = await applications.GetPropertiesAsync(application);
            var role = properties.TryGetValue("role", out var value) ? value.GetString() : null;
            if (role is null || !AllowedRoles.Contains(role)) throw new InvalidOperationException($"El cliente {request.ClientId} no tiene un rol válido.");
            var identity = new ClaimsIdentity(TokenValidationParameters.DefaultAuthenticationType, Claims.Name, Claims.Role);
            identity.SetClaim(Claims.Subject, request.ClientId);
            identity.SetClaim(Claims.Name, await applications.GetDisplayNameAsync(application));
            identity.SetClaims(Claims.Role, [role]);
            identity.SetScopes(request.GetScopes().Intersect([Scope]));
            identity.SetDestinations(_ => [Destinations.AccessToken]);
            await using (var tx = await db.Database.BeginTransactionAsync())
            {
                await audit.WriteAsync(request.ClientId!, http.Ip(), "oauth_token", "OAuthClient", request.ClientId!);
                await tx.CommitAsync();
            }
            return Results.SignIn(new ClaimsPrincipal(identity), authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }).RequireRateLimiting("auth");

        var group = app.MapGroup("/api/integraciones").RequireAuthorization("admin", "user").AddEndpointFilter<RequestValidationFilter>();
        group.MapGet("/", async (IOpenIddictApplicationManager applications) =>
        {
            var items = new List<object>();
            await foreach (var application in applications.ListAsync())
            {
                var properties = await applications.GetPropertiesAsync(application);
                items.Add(new
                {
                    clientId = await applications.GetClientIdAsync(application),
                    displayName = await applications.GetDisplayNameAsync(application),
                    role = properties.TryGetValue("role", out var role) ? role.GetString() : null,
                });
            }
            return Results.Ok(new { items, tokenEndpoint = "/connect/token", scope = Scope });
        });
        group.MapPost("/", async (IntegrationClientRequest body, IOpenIddictApplicationManager applications, AppDbContext db, AuditWriter audit, HttpContext http) =>
        {
            if (!AllowedRoles.Contains(body.Role))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["Role"] = ["Rol no permitido para integraciones: Supervisor, Auditor o Consulta."] });
            var clientId = "cli_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
            var secret = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
            var descriptor = new OpenIddictApplicationDescriptor
            {
                ClientId = clientId, ClientSecret = secret, ClientType = ClientTypes.Confidential, DisplayName = body.DisplayName.Trim(),
                Permissions = { Permissions.Endpoints.Token, Permissions.GrantTypes.ClientCredentials, Permissions.Prefixes.Scope + Scope },
            };
            descriptor.Properties["role"] = JsonSerializer.SerializeToElement(body.Role);
            await using var tx = await db.Database.BeginTransactionAsync();
            await applications.CreateAsync(descriptor);
            await audit.WriteAsync(http.Actor(), http.Ip(), "create", "OAuthClient", clientId);
            await tx.CommitAsync();
            // El secreto solo se muestra aquí; OpenIddict guarda un hash.
            return Results.Created($"/api/integraciones/{clientId}", new { clientId, clientSecret = secret, displayName = descriptor.DisplayName, role = body.Role, tokenEndpoint = "/connect/token", scope = Scope });
        });
        group.MapDelete("/{clientId}", async (string clientId, IOpenIddictApplicationManager applications, IOpenIddictTokenManager tokens,
            AppDbContext db, AuditWriter audit, HttpContext http) =>
        {
            var application = await applications.FindByClientIdAsync(clientId);
            if (application is null) return Results.NotFound();
            await using var tx = await db.Database.BeginTransactionAsync();
            await tokens.RevokeByApplicationIdAsync((await applications.GetIdAsync(application))!);
            await applications.DeleteAsync(application);
            await audit.WriteAsync(http.Actor(), http.Ip(), "delete", "OAuthClient", clientId);
            await tx.CommitAsync();
            return Results.NoContent();
        });
    }
}
