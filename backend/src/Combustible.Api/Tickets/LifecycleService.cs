using System.Globalization;
using Combustible.Api.Endpoints;
using Combustible.Domain;
using Combustible.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Combustible.Api.Tickets;

public sealed record LifecycleResult(int Expired, int NearExpiry, int SchedulesRun, int Delivered);

// RF-10 (vencimiento), RF-05/RF-11 (solicitudes programadas y recurrentes), RF-23 (alertas).
public sealed class LifecycleService(IServiceScopeFactory scopes)
{
    private const string System = "sistema";
    private const string Local = "local";

    public async Task<LifecycleResult> RunOnceAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var expired = await ExpireAsync(now, cancellationToken);
        var near = await WarnAsync(now, cancellationToken);
        var schedules = await RunSchedulesAsync(now, cancellationToken);
        var delivered = await RetryDeliveriesAsync(now, cancellationToken);
        return new LifecycleResult(expired, near, schedules, delivered);
    }

    private static async Task<bool> TryLockAsync(AppDbContext db, long key, CancellationToken cancellationToken) =>
        (await db.Database.SqlQuery<bool>($"SELECT pg_try_advisory_xact_lock({key}) AS \"Value\"").ToListAsync(cancellationToken)).Single();

    private static readonly string[] ActiveNames = TicketService.Active.Select(x => x.ToString()).ToArray();

    private async Task<int> ExpireAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var audit = scope.ServiceProvider.GetRequiredService<AuditWriter>();
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        if (!await TryLockAsync(db, 710220, cancellationToken)) return 0;
        var tickets = await db.Tickets.FromSqlInterpolated(
            $"SELECT * FROM \"Tickets\" WHERE \"Status\" = ANY({ActiveNames}) AND \"ExpiresAt\" <= {now} FOR UPDATE SKIP LOCKED").ToListAsync(cancellationToken);
        foreach (var ticket in tickets)
        {
            ticket.Status = TicketStatus.Expired;
            ticket.Version = Guid.NewGuid();
            await Notifier.NotifyAsync(db, NotificationKind.TicketExpired, $"expired:{ticket.Id:N}", ticket.Id.ToString(), now,
                $"El ticket {ticket.Number} venció sin consumirse ({BusinessClock.Format(ticket.ExpiresAt)}).");
            await audit.WriteAsync(System, Local, "ticket_expired", "Ticket", ticket.Id.ToString(), cancellationToken);
        }
        await tx.CommitAsync(cancellationToken);
        return tickets.Count;
    }

    private async Task<int> WarnAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var audit = scope.ServiceProvider.GetRequiredService<AuditWriter>();
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        if (!await TryLockAsync(db, 710221, cancellationToken)) return 0;
        var settings = await db.TicketSettings.AsNoTracking().SingleAsync(cancellationToken);
        var limit = now.AddHours(settings.WarningHours);
        string[] states = [nameof(TicketStatus.Created), nameof(TicketStatus.Sent), nameof(TicketStatus.Pending)];
        var tickets = await db.Tickets.FromSqlInterpolated(
            $"SELECT * FROM \"Tickets\" WHERE \"Status\" = ANY({states}) AND \"ExpiresAt\" > {now} AND \"ExpiresAt\" <= {limit} FOR UPDATE SKIP LOCKED")
            .ToListAsync(cancellationToken);
        foreach (var ticket in tickets)
        {
            ticket.Status = TicketStatus.NearExpiry;
            ticket.Version = Guid.NewGuid();
            await Notifier.NotifyAsync(db, NotificationKind.TicketNearExpiry, $"near:{ticket.Id:N}", ticket.Id.ToString(), now,
                $"El ticket {ticket.Number} vence el {BusinessClock.Format(ticket.ExpiresAt)}.");
            await audit.WriteAsync(System, Local, "ticket_near_expiry", "Ticket", ticket.Id.ToString(), cancellationToken);
        }
        await tx.CommitAsync(cancellationToken);
        return tickets.Count;
    }

    private async Task<int> RunSchedulesAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var run = 0;
        while (true)
        {
            Guid? issuedTicket;
            await using (var scope = scopes.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var audit = scope.ServiceProvider.GetRequiredService<AuditWriter>();
                var tickets = scope.ServiceProvider.GetRequiredService<TicketService>();
                await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
                var schedule = await db.FuelSchedules.FromSqlInterpolated(
                    $"SELECT * FROM \"FuelSchedules\" WHERE \"Active\" AND \"NextRunAt\" <= {now} ORDER BY \"NextRunAt\" LIMIT 1 FOR UPDATE SKIP LOCKED")
                    .SingleOrDefaultAsync(cancellationToken);
                if (schedule is null) return run;
                issuedTicket = await ExecuteAsync(db, audit, tickets, schedule, now, cancellationToken);
                Advance(schedule, now);
                schedule.LastRunAt = TicketService.Truncate(now);
                schedule.Version = Guid.NewGuid();
                await audit.WriteAsync(System, Local, "schedule_run", "FuelSchedule", schedule.Id.ToString(), cancellationToken);
                await tx.CommitAsync(cancellationToken);
            }
            run++;
            if (issuedTicket is { } ticketId) await DeliverAsync(ticketId, cancellationToken);
        }
    }

    // Genera la solicitud; si la programación aprueba sola y las reglas lo permiten, emite el ticket.
    private static async Task<Guid?> ExecuteAsync(AppDbContext db, AuditWriter audit, TicketService tickets, FuelSchedule schedule,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        decimal quantity;
        if (schedule.Rule == QuantityRule.Fixed) quantity = schedule.Quantity;
        else
        {
            var history = await (from d in db.Dispatches.AsNoTracking()
                                 join t in db.Tickets on d.TicketId equals t.Id
                                 where t.VehicleId == schedule.VehicleId
                                 orderby d.OccurredAt descending
                                 select d.Quantity).Take(schedule.HistorySize).ToListAsync(cancellationToken);
            if (history.Count == 0)
            {
                await FailAsync(db, schedule, now, "El vehículo no tiene despachos previos para calcular la cantidad por historial.");
                return null;
            }
            quantity = decimal.Round(history.Average(), 3, MidpointRounding.AwayFromZero);
        }
        var error = await tickets.ValidateRequestAsync(schedule.EmployeeId, schedule.VehicleId, schedule.DepartmentId, schedule.FuelTypeId,
            quantity, null, now, cancellationToken);
        if (error is not null)
        {
            await FailAsync(db, schedule, now, error);
            return null;
        }
        var request = new FuelRequest
        {
            EmployeeId = schedule.EmployeeId, VehicleId = schedule.VehicleId, DepartmentId = schedule.DepartmentId, FuelTypeId = schedule.FuelTypeId,
            AuthorizedQuantity = quantity, RequestedAt = TicketService.Truncate(now), ScheduleId = schedule.Id, Status = RequestStatus.Pending,
            Origin = schedule.Frequency == ScheduleFrequency.Once ? RequestOrigin.Scheduled : RequestOrigin.Recurring,
            Notes = $"Generada por la programación «{schedule.Name}»" + (schedule.Rule == QuantityRule.History ? $" (promedio de {schedule.HistorySize} despachos)." : "."),
            RequestedBy = System,
        };
        db.FuelRequests.Add(request);
        schedule.LastError = string.Empty;
        await audit.WriteAsync(System, Local, "create", "FuelRequest", request.Id.ToString(), cancellationToken);
        if (!schedule.AutoApprove) return null;
        var (issued, issueError) = await tickets.IssueAsync(request, System, now, cancellationToken);
        if (issueError is not null)
        {
            // La regla de negocio impide la asignación automática: queda para decisión humana.
            request.Notes += $" Aprobación automática no aplicada: {issueError}";
            return null;
        }
        request.Status = RequestStatus.Approved;
        request.DecidedBy = System;
        request.DecidedAt = TicketService.Truncate(now);
        request.DecisionReason = "Asignación automática por programación.";
        await audit.WriteAsync(System, Local, "approve", "FuelRequest", request.Id.ToString(), cancellationToken);
        await audit.WriteAsync(System, Local, "ticket_issued", "Ticket", issued!.Ticket.Id.ToString(), cancellationToken);
        return issued.Ticket.Id;
    }

    private static async Task FailAsync(AppDbContext db, FuelSchedule schedule, DateTimeOffset now, string error)
    {
        schedule.LastError = error.Length <= 500 ? error : error[..500];
        await Notifier.NotifyAsync(db, NotificationKind.ScheduleFailure,
            $"schedule:{schedule.Id:N}:{schedule.NextRunAt.UtcTicks.ToString(CultureInfo.InvariantCulture)}", schedule.Id.ToString(), now,
            $"La programación «{schedule.Name}» no generó su solicitud: {error}");
    }

    // Sin recuperación retroactiva: si el servicio estuvo detenido, se salta a la siguiente fecha futura.
    public static void Advance(FuelSchedule schedule, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        if (schedule.Frequency == ScheduleFrequency.Once) { schedule.Active = false; return; }
        do
        {
            schedule.NextRunAt = schedule.Frequency switch
            {
                ScheduleFrequency.Daily => schedule.NextRunAt.AddDays(1),
                ScheduleFrequency.Weekly => schedule.NextRunAt.AddDays(7),
                _ => schedule.NextRunAt.AddMonths(1),
            };
        } while (schedule.NextRunAt <= now);
        if (schedule.EndsAt is { } end && schedule.NextRunAt > end) schedule.Active = false;
    }

    // Tickets emitidos cuya entrega no llegó a ejecutarse (p. ej. el proceso se detuvo tras el commit).
    private async Task<int> RetryDeliveriesAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        List<Guid> ids;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var cutoff = now.AddMinutes(-2);
            ids = await db.Tickets.AsNoTracking()
                .Where(t => t.Status == TicketStatus.Created && t.IssuedAt < cutoff && !db.TicketDeliveries.Any(d => d.TicketId == t.Id))
                .OrderBy(t => t.IssuedAt).Select(t => t.Id).Take(20).ToListAsync(cancellationToken);
        }
        foreach (var id in ids) await DeliverAsync(id, cancellationToken);
        return ids.Count;
    }

    private async Task DeliverAsync(Guid ticketId, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<TicketService>().DeliverAsync(ticketId, System, Local, cancellationToken);
    }
}

public sealed partial class LifecycleWorker(LifecycleService lifecycle, TimeProvider time, ILogger<LifecycleWorker> logger, IConfiguration configuration)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var seconds = configuration.GetValue("Jobs:IntervalSeconds", 60);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(seconds), time);
        do
        {
            try
            {
                var result = await lifecycle.RunOnceAsync(time.GetUtcNow(), stoppingToken);
                if (result != new LifecycleResult(0, 0, 0, 0)) LogCycle(logger, result);
            }
            // Un fallo del ciclo se registra como error y se reintenta en el siguiente intervalo;
            // detener la API por un fallo del trabajo periódico dejaría sin servicio el despacho.
            catch (Exception e) when (e is not OperationCanceledException)
            {
                LogFailure(logger, e);
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Ciclo de tickets: {Result}")]
    private static partial void LogCycle(ILogger logger, LifecycleResult result);

    [LoggerMessage(Level = LogLevel.Error, Message = "Falló el ciclo periódico de tickets.")]
    private static partial void LogFailure(ILogger logger, Exception exception);
}
