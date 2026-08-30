using System.Security.Cryptography;
using Combustible.Api.Security;
using Combustible.Application;
using Combustible.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Combustible.Api.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuth(this WebApplication app)
    {
        var group = app.MapGroup("/api/auth").AddEndpointFilter<RequestValidationFilter>().RequireRateLimiting("auth");
        group.MapPost("/login", async (LoginRequest request, AppDbContext db, UserManager<AppUser> users,
            SignInManager<AppUser> signIn, SessionService sessions, AuditWriter audit, HttpContext http) =>
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({request.Email.ToUpperInvariant()}, 0))");
            var user = await users.FindByEmailAsync(request.Email);
            var valid = user is { Active: true } && (await signIn.CheckPasswordSignInAsync(user, request.Password, true)).Succeeded;
            if (valid && user!.TwoFactorEnabled)
            {
                valid = request.RecoveryCode is { Length: > 0 }
                    ? (await users.RedeemTwoFactorRecoveryCodeAsync(user, request.RecoveryCode.Trim())).Succeeded
                    : request.Code is not null && await users.VerifyTwoFactorTokenAsync(user,
                        TokenOptions.DefaultAuthenticatorProvider, request.Code.Replace(" ", string.Empty, StringComparison.Ordinal));
                if (!valid) UserEndpoints.Ensure(await users.AccessFailedAsync(user));
            }
            await audit.WriteAsync(user?.Id.ToString() ?? "anonymous", http.Ip(), valid ? "login" : "login_failed", "User", user?.Id.ToString() ?? string.Empty);
            if (!valid) { await tx.CommitAsync(); return Results.Unauthorized(); }
            UserEndpoints.Ensure(await users.ResetAccessFailedCountAsync(user!));
            var response = await sessions.IssueAsync(user!);
            await tx.CommitAsync();
            return Results.Ok(response);
        });

        group.MapPost("/refresh", async (RefreshRequest request, AppDbContext db, UserManager<AppUser> users,
            SessionService sessions, AuditWriter audit, HttpContext http) =>
        {
            if (!Guid.TryParse(request.RefreshToken.Split('.')[0], out var id)) return Results.Unauthorized();
            await using var tx = await db.Database.BeginTransactionAsync();
            var session = await db.Sessions.FromSqlInterpolated($"SELECT * FROM \"Sessions\" WHERE \"Id\" = {id} FOR UPDATE").SingleOrDefaultAsync();
            if (session is null || session.Revoked || session.ExpiresAt <= DateTimeOffset.UtcNow) return Results.Unauthorized();
            var user = await users.FindByIdAsync(session.UserId.ToString());
            var validHash = CryptographicOperations.FixedTimeEquals(Convert.FromHexString(session.RefreshHash), Convert.FromHexString(SessionService.Hash(request.RefreshToken)));
            if (!validHash || user is not { Active: true } || session.SecurityStamp != user.SecurityStamp)
            {
                session.Revoked = true;
                await audit.WriteAsync(session.UserId.ToString(), http.Ip(), "session_revoked", "Session", id.ToString());
                await tx.CommitAsync();
                return Results.Unauthorized();
            }
            var response = await sessions.IssueAsync(user, session);
            await audit.WriteAsync(user.Id.ToString(), http.Ip(), "refresh", "Session", id.ToString());
            await tx.CommitAsync();
            return Results.Ok(response);
        });

        group.MapGet("/me", async (HttpContext http, UserManager<AppUser> users) =>
        {
            var user = (await users.FindByIdAsync(http.Actor()))!;
            return Results.Ok(new { user.Id, user.DisplayName, user.Email, user.TwoFactorEnabled, Roles = await users.GetRolesAsync(user) });
        }).RequireAuthorization();

        group.MapPost("/logout", async (HttpContext http, AppDbContext db, AuditWriter audit) =>
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            var id = Guid.Parse(http.User.FindFirst("sid")!.Value);
            var session = await db.Sessions.SingleAsync(x => x.Id == id);
            session.Revoked = true;
            await audit.WriteAsync(http.Actor(), http.Ip(), "logout", "Session", id.ToString());
            await tx.CommitAsync();
            return Results.NoContent();
        }).RequireAuthorization();

        group.MapPost("/mfa/setup", async (MfaRequest request, HttpContext http, UserManager<AppUser> users, AppDbContext db, AuditWriter audit, SessionService sessions) =>
        {
            var user = (await users.FindByIdAsync(http.Actor()))!;
            if (!await users.CheckPasswordAsync(user, request.Password)) return Results.Unauthorized();
            if (user.TwoFactorEnabled) return Results.Conflict(new { error = "MFA ya está activado." });
            await using var tx = await db.Database.BeginTransactionAsync();
            var result = await users.ResetAuthenticatorKeyAsync(user);
            if (!result.Succeeded) return Results.Conflict();
            var session = await db.Sessions.SingleAsync(x => x.Id == Guid.Parse(http.User.FindFirst("sid")!.Value));
            session.SecurityStamp = user.SecurityStamp!;
            var renewed = await sessions.IssueAsync(user, session);
            await audit.WriteAsync(http.Actor(), http.Ip(), "mfa_setup", "User", http.Actor());
            await tx.CommitAsync();
            return Results.Ok(new { secret = await users.GetAuthenticatorKeyAsync(user), issuer = "INTEC Combustible", session = renewed });
        }).RequireAuthorization();

        group.MapPost("/mfa/enable", async (MfaRequest request, HttpContext http, UserManager<AppUser> users, AppDbContext db, AuditWriter audit) =>
        {
            var user = (await users.FindByIdAsync(http.Actor()))!;
            if (!await users.CheckPasswordAsync(user, request.Password) || request.Code is null
                || !await users.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultAuthenticatorProvider, request.Code)) return Results.Unauthorized();
            await using var tx = await db.Database.BeginTransactionAsync();
            var result = await users.SetTwoFactorEnabledAsync(user, true);
            if (!result.Succeeded) return Results.Conflict();
            var recoveryCodes = await users.GenerateNewTwoFactorRecoveryCodesAsync(user, 10)
                ?? throw new InvalidOperationException("No se generaron códigos de recuperación.");
            UserEndpoints.Ensure(await users.UpdateSecurityStampAsync(user));
            await audit.WriteAsync(http.Actor(), http.Ip(), "mfa_enabled", "User", http.Actor());
            await tx.CommitAsync();
            return Results.Ok(new { recoveryCodes });
        }).RequireAuthorization();

        group.MapPost("/mfa/disable", async (MfaRequest request, HttpContext http, UserManager<AppUser> users, AppDbContext db, AuditWriter audit) =>
        {
            var user = (await users.FindByIdAsync(http.Actor()))!;
            if (!await users.CheckPasswordAsync(user, request.Password) || request.Code is null
                || !await users.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultAuthenticatorProvider, request.Code)) return Results.Unauthorized();
            await using var tx = await db.Database.BeginTransactionAsync();
            UserEndpoints.Ensure(await users.SetTwoFactorEnabledAsync(user, false));
            UserEndpoints.Ensure(await users.ResetAuthenticatorKeyAsync(user));
            await audit.WriteAsync(http.Actor(), http.Ip(), "mfa_disabled", "User", http.Actor());
            await tx.CommitAsync();
            return Results.NoContent();
        }).RequireAuthorization();
    }

    public static string Actor(this HttpContext http) => http.User.FindFirst("sub")?.Value ?? "anonymous";
    public static string Ip(this HttpContext http) => http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
