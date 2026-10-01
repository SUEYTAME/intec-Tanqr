using System.Globalization;
using Combustible.Application;
using Combustible.Domain;
using Combustible.Infrastructure.Data;
using Combustible.Infrastructure.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Combustible.Api.Endpoints;

public static class UserEndpoints
{
    public static void MapUsers(this WebApplication app)
    {
        var group = app.MapGroup("/api/usuarios").RequireAuthorization("admin").AddEndpointFilter<RequestValidationFilter>();
        group.MapGet("/", async (UserManager<AppUser> users, int page = 1) =>
        {
            if (page is < 1 or > 100000) return Results.BadRequest();
            var result = new List<object>();
            foreach (var user in await users.Users.OrderBy(x => x.Email).Skip((page - 1) * 50).Take(50).ToListAsync())
                result.Add(new { user.Id, user.Email, user.DisplayName, user.Active, user.TwoFactorEnabled, Version = user.ConcurrencyStamp, Roles = await users.GetRolesAsync(user) });
            return Results.Ok(new { items = result, total = await users.Users.CountAsync() });
        });
        group.MapPost("/", async (UserRequest request, UserManager<AppUser> users, AppDbContext db, AuditWriter audit, HttpContext http) =>
        {
            if (!Roles.All.Contains(request.Role)) return Results.BadRequest(new { error = "Rol inválido." });
            await using var tx = await db.Database.BeginTransactionAsync();
            var user = new AppUser { UserName = request.Email, Email = request.Email, DisplayName = request.DisplayName.Trim() };
            var result = await users.CreateAsync(user, request.Password);
            if (!result.Succeeded) return Results.ValidationProblem(result.Errors.ToDictionary(x => x.Code, x => new[] { x.Description }));
            Ensure(await users.AddToRoleAsync(user, request.Role));
            await audit.WriteAsync(http.Actor(), http.Ip(), "create", "User", user.Id.ToString());
            await tx.CommitAsync();
            return Results.Created($"/api/usuarios/{user.Id}", new { user.Id, user.Email, user.DisplayName, role = request.Role });
        });
        group.MapPut("/{id:guid}/acceso", async (Guid id, UserAccessRequest request, UserManager<AppUser> users, AppDbContext db, AuditWriter audit, HttpContext http) =>
        {
            if (!Roles.All.Contains(request.Role)) return Results.BadRequest();
            if (id.ToString() == http.Actor()) return Results.Conflict(new { error = "Otro administrador debe modificar tu acceso." });
            await using var tx = await db.Database.BeginTransactionAsync();
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(710211)");
            var user = await users.FindByIdAsync(id.ToString());
            if (user is null) return Results.NotFound();
            // Cambio de acceso = leer-modificar-escribir: exige la versión que el administrador vio.
            if (user.ConcurrencyStamp != request.Version) return Results.StatusCode(412);
            if ((!request.Active || request.Role != Roles.Administrator) && await users.IsInRoleAsync(user, Roles.Administrator)
                && (await users.GetUsersInRoleAsync(Roles.Administrator)).Count(x => x.Active) <= 1)
                return Results.Conflict(new { error = "Debe quedar al menos un administrador activo." });
            user.Active = request.Active;
            Ensure(await users.UpdateAsync(user));
            Ensure(await users.RemoveFromRolesAsync(user, await users.GetRolesAsync(user)));
            Ensure(await users.AddToRoleAsync(user, request.Role));
            Ensure(await users.UpdateSecurityStampAsync(user));
            await audit.WriteAsync(http.Actor(), http.Ip(), "access_changed", "User", id.ToString());
            await tx.CommitAsync();
            return Results.NoContent();
        });
        group.MapPut("/{id:guid}", async (Guid id, UserUpdateRequest request, UserManager<AppUser> users, AppDbContext db, AuditWriter audit, HttpContext http) =>
        {
            if (!Roles.All.Contains(request.Role)) return Results.BadRequest();
            if (id.ToString() == http.Actor()) return Results.Conflict(new { error = "Otro administrador debe modificar tu cuenta." });
            await using var tx = await db.Database.BeginTransactionAsync();
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(710211)");
            var user = await users.FindByIdAsync(id.ToString());
            if (user is null) return Results.NotFound();
            if (user.ConcurrencyStamp != request.Version) return Results.StatusCode(412);
            if ((!request.Active || request.Role != Roles.Administrator) && await users.IsInRoleAsync(user, Roles.Administrator)
                && (await users.GetUsersInRoleAsync(Roles.Administrator)).Count(x => x.Active) <= 1)
                return Results.Conflict(new { error = "Debe quedar al menos un administrador activo." });
            user.Email = request.Email; user.UserName = request.Email; user.DisplayName = request.DisplayName.Trim();
            user.Active = request.Active;
            var result = await users.UpdateAsync(user);
            if (!result.Succeeded) return Results.ValidationProblem(result.Errors.ToDictionary(x => x.Code, x => new[] { x.Description }));
            Ensure(await users.RemoveFromRolesAsync(user, await users.GetRolesAsync(user)));
            Ensure(await users.AddToRoleAsync(user, request.Role));
            Ensure(await users.UpdateSecurityStampAsync(user));
            await audit.WriteAsync(http.Actor(), http.Ip(), "user_updated", "User", id.ToString());
            await tx.CommitAsync();
            return Results.NoContent();
        });
        group.MapPost("/{id:guid}/password", async (Guid id, PasswordRequest request, UserManager<AppUser> users, AppDbContext db, AuditWriter audit, HttpContext http) =>
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            var user = await users.FindByIdAsync(id.ToString());
            if (user is null) return Results.NotFound();
            var token = await users.GeneratePasswordResetTokenAsync(user);
            var result = await users.ResetPasswordAsync(user, token, request.Password);
            if (!result.Succeeded) return Results.ValidationProblem(result.Errors.ToDictionary(x => x.Code, x => new[] { x.Description }));
            await audit.WriteAsync(http.Actor(), http.Ip(), "password_reset", "User", id.ToString());
            await tx.CommitAsync();
            return Results.NoContent();
        });
        app.MapGet("/api/auditoria", async (AppDbContext db, int page = 1) =>
        {
            if (page is < 1 or > 100000) return Results.BadRequest();
            // El evento guarda el id (entra en el hash). La consulta muestra el nombre.
            var events = await db.AuditEvents.AsNoTracking().OrderByDescending(x => x.Id).Skip((page - 1) * 50).Take(50).ToListAsync();
            var ids = new List<Guid>();
            foreach (var actor in events.Select(x => x.Actor).Distinct())
                if (Guid.TryParse(actor, out var id)) ids.Add(id);
            var names = ids.Count == 0
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : (await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).Select(u => new { u.Id, u.DisplayName }).ToListAsync())
                    .ToDictionary(x => x.Id.ToString(), x => x.DisplayName, StringComparer.OrdinalIgnoreCase);
            return Results.Ok(new
            {
                items = events.Select(x => new
                {
                    x.Id,
                    x.OccurredAt,
                    Actor = names.TryGetValue(x.Actor, out var name) && !string.IsNullOrWhiteSpace(name) ? name : x.Actor,
                    x.Ip,
                    x.Action,
                    x.Entity,
                    x.EntityId,
                    x.PreviousHash,
                    x.Hash,
                }).ToList(),
                total = await db.AuditEvents.CountAsync(),
            });
        }).RequireAuthorization("audit-read");
        app.MapGet("/api/auditoria/verificar", async (AppDbContext db, CancellationToken cancellationToken) =>
        {
            var previous = string.Empty;
            var count = 0;
            // Una sola consulta mantiene una instantánea consistente mientras se agregan eventos.
            await foreach (var entry in db.AuditEvents.AsNoTracking().OrderBy(x => x.Id).AsAsyncEnumerable().WithCancellation(cancellationToken))
            {
                if (entry.PreviousHash != previous || entry.Hash != AuditWriter.ComputeHash(entry))
                    return Results.Conflict(new { valid = false, firstInvalidEvent = entry.Id });
                previous = entry.Hash;
                count++;
            }
            return Results.Ok(new { valid = true, events = count, lastHash = previous });
        }).RequireAuthorization("audit-read");

        // RS-06: ancla firmada del final de la cadena. Guardada FUERA del sistema (correo, papel, WORM),
        // permite detectar que un superusuario reconstruyó la cadena completa después de emitirla.
        app.MapGet("/api/auditoria/ancla", async (AppDbContext db, TicketSigner signer, AuditWriter audit, HttpContext http) =>
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            await audit.WriteAsync(http.Actor(), http.Ip(), "audit_anchor", "AuditEvent", string.Empty);
            await tx.CommitAsync();
            var last = await db.AuditEvents.AsNoTracking().OrderByDescending(x => x.Id).Select(x => new { x.Id, x.Hash }).FirstAsync();
            var count = await db.AuditEvents.LongCountAsync(x => x.Id <= last.Id);
            var issuedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var signature = signer.SignDetached(AnchorPurpose, AnchorData(last.Id, last.Hash, count, issuedAt));
            return Results.Ok(new AuditAnchor(last.Id, last.Hash, count, issuedAt, signature));
        }).RequireAuthorization("audit-read");
        app.MapGet("/api/auditoria/clave-publica", (TicketSigner signer) => Results.Text(signer.PublicKeyPem(), "application/x-pem-file"))
            .RequireAuthorization("audit-read");
        app.MapPost("/api/auditoria/ancla/verificar", async (AuditAnchor anchor, AppDbContext db, TicketSigner signer, CancellationToken cancellationToken) =>
        {
            if (!signer.VerifyDetached(AnchorPurpose, AnchorData(anchor.LastId, anchor.LastHash, anchor.Count, anchor.IssuedAt), anchor.Signature))
                return Results.Conflict(new { valid = false, reason = "La firma del ancla no corresponde a este sistema." });
            var previous = string.Empty;
            long count = 0;
            await foreach (var entry in db.AuditEvents.AsNoTracking().Where(x => x.Id <= anchor.LastId).OrderBy(x => x.Id).AsAsyncEnumerable().WithCancellation(cancellationToken))
            {
                if (entry.PreviousHash != previous || entry.Hash != AuditWriter.ComputeHash(entry))
                    return Results.Conflict(new { valid = false, reason = $"La cadena se rompe en el evento {entry.Id}." });
                previous = entry.Hash;
                count++;
            }
            if (count != anchor.Count || previous != anchor.LastHash)
                return Results.Conflict(new { valid = false, reason = "La cadena actual no coincide con el ancla: fue reconstruida o truncada." });
            return Results.Ok(new { valid = true, anchor.LastId, anchor.Count });
        }).RequireAuthorization("audit-read").AddEndpointFilter<RequestValidationFilter>();
    }

    private const string AnchorPurpose = "AUDIT-ANCHOR-v1";

    private static string AnchorData(long lastId, string lastHash, long count, long issuedAt) =>
        string.Join('|', lastId.ToString(CultureInfo.InvariantCulture), lastHash, count.ToString(CultureInfo.InvariantCulture), issuedAt.ToString(CultureInfo.InvariantCulture));

    public static void Ensure(IdentityResult result)
    {
        if (!result.Succeeded) throw new InvalidOperationException(string.Join("; ", result.Errors.Select(x => x.Code)));
    }
}
