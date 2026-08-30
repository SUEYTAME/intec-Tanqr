using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Combustible.Infrastructure.Data;

public sealed class AuditWriter(AppDbContext db)
{
    public async Task WriteAsync(string actor, string ip, string action, string entity, string id,
        CancellationToken cancellationToken = default)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("La auditoría requiere la transacción de la operación.");
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(710210)", cancellationToken);
        var previous = await db.AuditEvents.OrderByDescending(x => x.Id)
            .Select(x => x.Hash).FirstOrDefaultAsync(cancellationToken) ?? string.Empty;
        var entry = new AuditEvent
        {
            // PostgreSQL guarda microsegundos: truncar antes de calcular el hash.
            OccurredAt = new DateTimeOffset(DateTime.UtcNow.Ticks / 10 * 10, TimeSpan.Zero),
            Actor = actor,
            Ip = ip,
            Action = action,
            Entity = entity,
            EntityId = id,
            PreviousHash = previous
        };
        entry.Hash = ComputeHash(entry);
        db.AuditEvents.Add(entry);
        await db.SaveChangesAsync(cancellationToken);
    }

    public static string ComputeHash(AuditEvent entry)
    {
        var payload = JsonSerializer.Serialize(new
        {
            entry.OccurredAt,
            entry.Actor,
            entry.Ip,
            entry.Action,
            entry.Entity,
            entry.EntityId,
            entry.PreviousHash
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }
}
