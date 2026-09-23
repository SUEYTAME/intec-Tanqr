using System.Globalization;
using Combustible.Api.Tickets;
using Combustible.Application;
using Combustible.Domain;
using Combustible.Infrastructure.Data;
using Combustible.Infrastructure.Documents;
using Microsoft.EntityFrameworkCore;

namespace Combustible.Api.Endpoints;

// RF-14 / RF-17: el saldo de un tanque solo cambia aquí, con el tanque bloqueado y un movimiento inmutable.
public static class InventoryLedger
{
    public static Task<Tank?> LockTankAsync(AppDbContext db, Guid id) =>
        db.Tanks.FromSqlInterpolated($"SELECT * FROM \"Tanks\" WHERE \"Id\" = {id} FOR UPDATE").SingleOrDefaultAsync();

    public static async Task<string?> ApplyAsync(AppDbContext db, Tank tank, MovementKind kind, decimal signedQuantity, string actor,
        string reason, DateTimeOffset now, Guid? dispatchId = null, Guid? receiptId = null, Guid? transferId = null)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(tank);
        if (!tank.Active) return $"El tanque {tank.Code} está inactivo.";
        if (signedQuantity == 0 || decimal.Round(signedQuantity, 3) != signedQuantity) return "La cantidad debe ser distinta de cero y tener como máximo 3 decimales.";
        var day = BusinessClock.LocalDay(now);
        // Compartido frente a otros movimientos; exclusivo frente al cierre del mismo día.
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock_shared(hashtextextended({CloseLockKey(tank.StationId, day)}, 3))");
        if (await db.DailyCloses.AnyAsync(x => x.StationId == tank.StationId && x.Day == day))
            return "El día operativo ya fue cerrado para esta estación.";
        var balance = tank.Balance + signedQuantity;
        if (balance < 0) return $"Existencia insuficiente en el tanque {tank.Code}: hay {DocumentRenderer.Gallons(tank.Balance)} gal.";
        if (balance > tank.Capacity) return $"La operación supera la capacidad del tanque {tank.Code} ({DocumentRenderer.Gallons(tank.Capacity)} gal).";
        tank.Balance = balance;
        db.InventoryMovements.Add(new InventoryMovement
        {
            TankId = tank.Id, Kind = kind, Quantity = signedQuantity, BalanceAfter = balance, OccurredAt = TicketService.Truncate(now),
            Actor = actor, Reason = reason, DispatchId = dispatchId, ReceiptId = receiptId, TransferId = transferId,
        });
        if (balance <= tank.CriticalLevel)
            await Notifier.NotifyAsync(db, NotificationKind.LowInventory, $"low:{tank.Id:N}:{day:yyyyMMdd}", tank.Id.ToString(), now,
                $"El tanque {tank.Code} está en nivel crítico: {DocumentRenderer.Gallons(balance)} gal.");
        return null;
    }

    public static string CloseLockKey(Guid stationId, DateOnly day) => $"close:{stationId:N}:{day:yyyyMMdd}";
}

// RF-23: una alerta por clave; los duplicados se ignoran en la base sin abortar la operación.
public static class Notifier
{
    public static Task NotifyAsync(AppDbContext db, NotificationKind kind, string dedupKey, string entityId, DateTimeOffset now, string message)
    {
        ArgumentNullException.ThrowIfNull(db);
        return db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Notifications" ("Kind", "DedupKey", "Message", "EntityId", "CreatedAt")
            VALUES ({kind.ToString()}, {dedupKey}, {message}, {entityId}, {TicketService.Truncate(now)})
            ON CONFLICT ("DedupKey") DO NOTHING
            """);
    }
}

public static class InventoryEndpoints
{
    public static void MapInventory(this WebApplication app)
    {
        MapStock(app);
        MapDispatch(app);
        MapCloses(app);
    }

    private static void MapStock(WebApplication app)
    {
        var group = app.MapGroup("/api/inventario").RequireAuthorization().AddEndpointFilter<RequestValidationFilter>();

        // RF-15: existencia, disponibilidad (existencia menos lo comprometido en tickets activos),
        // consumo diario y mensual, nivel crítico.
        group.MapGet("/", async (AppDbContext db, TimeProvider time) =>
        {
            var now = time.GetUtcNow();
            var today = BusinessClock.LocalDay(now);
            var (dayStart, dayEnd) = BusinessClock.DayBounds(today);
            var (monthStart, _) = BusinessClock.DayBounds(new DateOnly(today.Year, today.Month, 1));
            var daily = await db.InventoryMovements.AsNoTracking().Where(m => m.Kind == MovementKind.Dispatch && m.OccurredAt >= dayStart && m.OccurredAt < dayEnd)
                .GroupBy(m => m.TankId).Select(g => new { g.Key, Total = -g.Sum(x => x.Quantity) }).ToDictionaryAsync(x => x.Key, x => x.Total);
            var monthly = await db.InventoryMovements.AsNoTracking().Where(m => m.Kind == MovementKind.Dispatch && m.OccurredAt >= monthStart && m.OccurredAt < dayEnd)
                .GroupBy(m => m.TankId).Select(g => new { g.Key, Total = -g.Sum(x => x.Quantity) }).ToDictionaryAsync(x => x.Key, x => x.Total);
            var tanks = await (from t in db.Tanks.AsNoTracking()
                               join s in db.Stations on t.StationId equals s.Id
                               join f in db.FuelTypes on t.FuelTypeId equals f.Id
                               orderby s.Code, t.Code
                               select new { t.Id, t.Code, t.Active, t.Capacity, t.Balance, t.CriticalLevel, t.StationId, Station = s.Name, t.FuelTypeId, FuelType = f.Name })
                .ToListAsync();
            var committed = await db.Tickets.AsNoTracking().Where(t => TicketService.Active.Contains(t.Status) && t.ExpiresAt > now)
                .GroupBy(t => t.FuelTypeId).Select(g => new { g.Key, Total = g.Sum(x => x.AuthorizedQuantity) }).ToDictionaryAsync(x => x.Key, x => x.Total);
            var fuels = tanks.Where(t => t.Active).GroupBy(t => new { t.FuelTypeId, t.FuelType }).Select(g =>
            {
                var stock = g.Sum(x => x.Balance);
                var reserved = committed.GetValueOrDefault(g.Key.FuelTypeId);
                return new { g.Key.FuelTypeId, g.Key.FuelType, Stock = stock, Committed = reserved, Available = stock - reserved };
            });
            return Results.Ok(new
            {
                tanks = tanks.Select(t => new
                {
                    t.Id, t.Code, t.Active, t.Capacity, t.Balance, t.CriticalLevel, t.StationId, t.Station, t.FuelTypeId, t.FuelType,
                    Critical = t.Balance <= t.CriticalLevel, FreeSpace = t.Capacity - t.Balance,
                    DailyConsumption = daily.GetValueOrDefault(t.Id), MonthlyConsumption = monthly.GetValueOrDefault(t.Id),
                }),
                fuels,
            });
        });

        group.MapGet("/movimientos", async (AppDbContext db, Guid? tankId, string? kind, DateOnly? from, DateOnly? to, int page = 1) =>
        {
            if (page is < 1 or > 100000) return Results.BadRequest();
            var query = MovementRows(db);
            if (tankId is { } tank) query = query.Where(x => x.TankId == tank);
            if (kind is not null)
            {
                if (!Enum.TryParse<MovementKind>(kind, out var parsed)) return Results.BadRequest(new { error = "Tipo de movimiento inválido." });
                query = query.Where(x => x.Kind == parsed);
            }
            if (from is { } start) query = query.Where(x => x.OccurredAt >= BusinessClock.DayBounds(start).Start);
            if (to is { } end) query = query.Where(x => x.OccurredAt < BusinessClock.DayBounds(end).End);
            return Results.Ok(new { items = await query.OrderByDescending(x => x.Id).Skip((page - 1) * 50).Take(50).ToListAsync(), total = await query.CountAsync() });
        });

        group.MapGet("/recepciones", async (AppDbContext db, int page = 1) =>
        {
            if (page is < 1 or > 100000) return Results.BadRequest();
            var query = from r in db.FuelReceipts.AsNoTracking()
                        join t in db.Tanks on r.TankId equals t.Id
                        select new { r.Id, r.Kind, r.SupplierRnc, r.SupplierName, r.Invoice, r.Quantity, r.ReceivedOn, r.TankId, Tank = t.Code, r.RecordedAt };
            return Results.Ok(new { items = await query.OrderByDescending(x => x.RecordedAt).Skip((page - 1) * 50).Take(50).ToListAsync(), total = await query.CountAsync() });
        });

        // RF-16: recepción con RNC, suplidor, factura, volumen, fecha y tanque; impacta el inventario.
        group.MapPost("/recepciones", async (ReceiptRequest body, AppDbContext db, AuditWriter audit, HttpContext http, TimeProvider time) =>
        {
            var now = time.GetUtcNow();
            if (body.ReceivedOn > BusinessClock.LocalDay(now)) return Results.UnprocessableEntity(new { error = "La fecha de recepción no puede ser futura." });
            await using var tx = await db.Database.BeginTransactionAsync();
            var tank = await InventoryLedger.LockTankAsync(db, body.TankId);
            if (tank is null) return Results.UnprocessableEntity(new { error = "Tanque inexistente." });
            var kind = Enum.Parse<MovementKind>(body.Kind);
            var receipt = new FuelReceipt
            {
                Kind = kind, SupplierRnc = body.SupplierRnc, SupplierName = body.SupplierName.Trim(), Invoice = body.Invoice.Trim().ToUpperInvariant(),
                Quantity = body.Quantity, ReceivedOn = body.ReceivedOn, TankId = tank.Id, Actor = http.Actor(), RecordedAt = TicketService.Truncate(now),
            };
            db.FuelReceipts.Add(receipt);
            var error = await InventoryLedger.ApplyAsync(db, tank, kind, body.Quantity, http.Actor(),
                $"Recepción factura {receipt.Invoice} de {receipt.SupplierName} (RNC {receipt.SupplierRnc})", now, receiptId: receipt.Id);
            if (error is not null) return Results.UnprocessableEntity(new { error });
            await audit.WriteAsync(http.Actor(), http.Ip(), "receipt", "FuelReceipt", receipt.Id.ToString());
            await tx.CommitAsync();
            return Results.Created($"/api/inventario/recepciones/{receipt.Id}", new { receipt.Id, balance = tank.Balance });
        }).RequireAuthorization("inventory-write");

        group.MapPost("/transferencias", async (TransferRequest body, AppDbContext db, AuditWriter audit, HttpContext http, TimeProvider time) =>
        {
            if (body.FromTankId == body.ToTankId) return Results.UnprocessableEntity(new { error = "El tanque de origen y destino deben ser distintos." });
            var now = time.GetUtcNow();
            await using var tx = await db.Database.BeginTransactionAsync();
            // Orden fijo de bloqueo para evitar interbloqueos entre transferencias cruzadas.
            var ids = new[] { body.FromTankId, body.ToTankId }.Order().ToArray();
            var first = await InventoryLedger.LockTankAsync(db, ids[0]);
            var second = await InventoryLedger.LockTankAsync(db, ids[1]);
            if (first is null || second is null) return Results.UnprocessableEntity(new { error = "Tanque inexistente." });
            var source = first.Id == body.FromTankId ? first : second;
            var target = source == first ? second : first;
            if (source.FuelTypeId != target.FuelTypeId) return Results.UnprocessableEntity(new { error = "Solo se transfiere entre tanques del mismo combustible." });
            var transferId = Guid.NewGuid();
            var reason = body.Reason.Trim();
            var error = await InventoryLedger.ApplyAsync(db, source, MovementKind.TransferOut, -body.Quantity, http.Actor(), reason, now, transferId: transferId)
                ?? await InventoryLedger.ApplyAsync(db, target, MovementKind.TransferIn, body.Quantity, http.Actor(), reason, now, transferId: transferId);
            if (error is not null) return Results.UnprocessableEntity(new { error });
            await audit.WriteAsync(http.Actor(), http.Ip(), "transfer", "Tank", transferId.ToString());
            await tx.CommitAsync();
            return Results.Ok(new { transferId, fromBalance = source.Balance, toBalance = target.Balance });
        }).RequireAuthorization("inventory-write");

        // RF-14: ajustes positivos y negativos y mermas, siempre con motivo.
        group.MapPost("/ajustes", async (AdjustmentRequest body, AppDbContext db, AuditWriter audit, HttpContext http, TimeProvider time) =>
        {
            var now = time.GetUtcNow();
            var kind = Enum.Parse<MovementKind>(body.Kind);
            var signed = kind == MovementKind.PositiveAdjustment ? body.Quantity : -body.Quantity;
            await using var tx = await db.Database.BeginTransactionAsync();
            var tank = await InventoryLedger.LockTankAsync(db, body.TankId);
            if (tank is null) return Results.UnprocessableEntity(new { error = "Tanque inexistente." });
            var error = await InventoryLedger.ApplyAsync(db, tank, kind, signed, http.Actor(), body.Reason.Trim(), now);
            if (error is not null) return Results.UnprocessableEntity(new { error });
            var label = kind switch { MovementKind.PositiveAdjustment => "Ajuste positivo", MovementKind.NegativeAdjustment => "Ajuste negativo", _ => "Merma" };
            await Notifier.NotifyAsync(db, NotificationKind.InventoryAdjustment, $"adjustment:{Guid.NewGuid():N}", tank.Id.ToString(), now,
                $"{label} de {DocumentRenderer.Gallons(body.Quantity)} gal en el tanque {tank.Code}: {body.Reason.Trim()}");
            await audit.WriteAsync(http.Actor(), http.Ip(), "adjustment", "Tank", tank.Id.ToString());
            await tx.CommitAsync();
            return Results.Ok(new { balance = tank.Balance });
        }).RequireAuthorization("inventory-write");
    }

    public sealed record MovementRow
{
    public long Id { get; init; }
    public Guid TankId { get; init; }
    public string Tank { get; init; } = string.Empty;
    public string Station { get; init; } = string.Empty;
    public string FuelType { get; init; } = string.Empty;
    public MovementKind Kind { get; init; }
    public decimal Quantity { get; init; }
    public decimal BalanceAfter { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
    public string Actor { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public Guid? DispatchId { get; init; }
    public Guid? ReceiptId { get; init; }
    public Guid? TransferId { get; init; }
}

    public static IQueryable<MovementRow> MovementRows(AppDbContext db) =>
        from m in db.InventoryMovements.AsNoTracking()
        join t in db.Tanks on m.TankId equals t.Id
        join s in db.Stations on t.StationId equals s.Id
        join f in db.FuelTypes on t.FuelTypeId equals f.Id
        select new MovementRow
        {
            Id = m.Id,
            TankId = m.TankId,
            Tank = t.Code,
            Station = s.Name,
            FuelType = f.Name,
            Kind = m.Kind,
            Quantity = m.Quantity,
            BalanceAfter = m.BalanceAfter,
            OccurredAt = m.OccurredAt,
            Actor = db.Users.Where(u => u.Id.ToString() == m.Actor).Select(u => u.DisplayName).FirstOrDefault() ?? m.Actor,
            Reason = m.Reason,
            DispatchId = m.DispatchId,
            ReceiptId = m.ReceiptId,
            TransferId = m.TransferId,
        };

    // RF-12 / CA-2: esta es la única ruta que crea despachos, y exige un QR con firma válida.
    private static void MapDispatch(WebApplication app)
    {
        var group = app.MapGroup("/api/despachos").RequireAuthorization().AddEndpointFilter<RequestValidationFilter>();

        group.MapPost("/validar", async (QrRequest body, AppDbContext db, TicketService tickets, AuditWriter audit, HttpContext http, TimeProvider time) =>
        {
            var check = await tickets.VerifyQrAsync(body.Qr, false, time.GetUtcNow());
            await using (var tx = await db.Database.BeginTransactionAsync())
            {
                await audit.WriteAsync(http.Actor(), http.Ip(), check.Error is null ? "qr_validated" : "qr_rejected", "Ticket", check.Ticket?.Id.ToString() ?? string.Empty);
                await tx.CommitAsync();
            }
            if (check.Ticket is null) return Results.UnprocessableEntity(new { valid = false, error = check.Error });
            var row = await TicketEndpoints.TicketRows(db).SingleAsync(x => x.Id == check.Ticket.Id);
            var nationalId = await db.Employees.AsNoTracking().Where(x => x.Id == row.EmployeeId).Select(x => x.NationalId).SingleAsync();
            return Results.Ok(new
            {
                valid = check.Error is null,
                error = check.Error,
                ticket = new
                {
                    row.Id, row.Number, row.Status, row.AuthorizedQuantity, row.IssuedAt, row.ExpiresAt, row.EmployeeName, row.EmployeeCode,
                    NationalIdLast4 = nationalId[^4..], row.VehiclePlate, row.VehicleCode, row.Department, row.FuelType, row.FuelTypeId,
                },
            });
        }).RequireAuthorization("dispatch");

        group.MapPost("/", async (DispatchRequest body, AppDbContext db, TicketService tickets, AuditWriter audit, HttpContext http, TimeProvider time) =>
        {
            if (!body.IdentityConfirmed)
                return Results.UnprocessableEntity(new { error = "Confirma la identidad del portador con su cédula antes de despachar." });
            var now = time.GetUtcNow();
            await using var tx = await db.Database.BeginTransactionAsync();
            var check = await tickets.VerifyQrAsync(body.Qr, true, now);
            if (check.Ticket is null || check.Error is not null)
            {
                await audit.WriteAsync(http.Actor(), http.Ip(), "dispatch_rejected", "Ticket", check.Ticket?.Id.ToString() ?? string.Empty);
                await tx.CommitAsync();
                return Results.UnprocessableEntity(new { error = check.Error });
            }
            var ticket = check.Ticket;
            if (body.Quantity > ticket.AuthorizedQuantity)
                return Results.UnprocessableEntity(new { error = $"No se puede despachar más de lo autorizado ({DocumentRenderer.Gallons(ticket.AuthorizedQuantity)} gal)." });
            var difference = ticket.AuthorizedQuantity - body.Quantity;
            if (difference > 0 && string.IsNullOrWhiteSpace(body.DifferenceReason))
                return Results.UnprocessableEntity(new { error = "El despacho es menor que lo autorizado: indica el motivo de la diferencia." });
            if (!await db.Employees.AnyAsync(x => x.Id == ticket.EmployeeId && x.Active))
                return Results.UnprocessableEntity(new { error = "El empleado del ticket está inactivo." });
            var tank = await InventoryLedger.LockTankAsync(db, body.TankId);
            if (tank is null) return Results.UnprocessableEntity(new { error = "Tanque inexistente." });
            if (tank.FuelTypeId != ticket.FuelTypeId) return Results.UnprocessableEntity(new { error = "El tanque no contiene el combustible del ticket." });
            if (!await db.Stations.AnyAsync(x => x.Id == tank.StationId && x.Active)) return Results.UnprocessableEntity(new { error = "La estación está inactiva." });
            var vehicle = await db.Vehicles.FromSqlInterpolated($"SELECT * FROM \"Vehicles\" WHERE \"Id\" = {ticket.VehicleId} FOR UPDATE").SingleAsync();
            if (!vehicle.Active) return Results.UnprocessableEntity(new { error = "El vehículo del ticket está inactivo." });
            if (body.Odometer is { } odometer)
            {
                if (odometer < vehicle.Odometer) return Results.UnprocessableEntity(new { error = $"El odómetro no puede ser menor que el registrado ({vehicle.Odometer} km)." });
                vehicle.Odometer = odometer;
                vehicle.Version = Guid.NewGuid();
            }
            var dispatch = new Dispatch
            {
                TicketId = ticket.Id, TankId = tank.Id, StationId = tank.StationId, Quantity = body.Quantity, Difference = difference,
                DifferenceReason = difference > 0 ? body.DifferenceReason!.Trim() : string.Empty, Odometer = body.Odometer, IdentityConfirmed = true,
                OperatorId = Guid.Parse(http.Actor()), OccurredAt = TicketService.Truncate(now), Observations = body.Observations?.Trim() ?? string.Empty,
            };
            db.Dispatches.Add(dispatch);
            var error = await InventoryLedger.ApplyAsync(db, tank, MovementKind.Dispatch, -body.Quantity, http.Actor(), $"Despacho ticket {ticket.Number}", now, dispatchId: dispatch.Id);
            if (error is not null) return Results.UnprocessableEntity(new { error });
            ticket.Status = TicketStatus.Consumed;
            ticket.ConsumedAt = dispatch.OccurredAt;
            ticket.Version = Guid.NewGuid();
            await audit.WriteAsync(http.Actor(), http.Ip(), "dispatch", "Dispatch", dispatch.Id.ToString());
            await tx.CommitAsync();
            return Results.Created($"/api/despachos/{dispatch.Id}", new { dispatch.Id, ticketNumber = ticket.Number, dispatch.Quantity, dispatch.Difference, tankBalance = tank.Balance });
        }).RequireAuthorization("dispatch");

        group.MapGet("/", async (AppDbContext db, Guid? stationId, DateOnly? from, DateOnly? to, int page = 1) =>
        {
            if (page is < 1 or > 100000) return Results.BadRequest();
            var query = DispatchRows(db);
            if (stationId is { } station) query = query.Where(x => x.StationId == station);
            if (from is { } start) query = query.Where(x => x.OccurredAt >= BusinessClock.DayBounds(start).Start);
            if (to is { } end) query = query.Where(x => x.OccurredAt < BusinessClock.DayBounds(end).End);
            return Results.Ok(new { items = await query.OrderByDescending(x => x.OccurredAt).Skip((page - 1) * 50).Take(50).ToListAsync(), total = await query.CountAsync() });
        });
    }

    public sealed record DispatchRow
{
    public Guid Id { get; init; }
    public Guid TicketId { get; init; }
    public string TicketNumber { get; init; } = string.Empty;
    public TicketStatus TicketStatus { get; init; }
    public decimal AuthorizedQuantity { get; init; }
    public decimal Quantity { get; init; }
    public decimal Difference { get; init; }
    public string DifferenceReason { get; init; } = string.Empty;
    public DateTimeOffset OccurredAt { get; init; }
    public Guid StationId { get; init; }
    public string Station { get; init; } = string.Empty;
    public Guid TankId { get; init; }
    public string Tank { get; init; } = string.Empty;
    public Guid EmployeeId { get; init; }
    public string EmployeeName { get; init; } = string.Empty;
    public Guid VehicleId { get; init; }
    public string VehiclePlate { get; init; } = string.Empty;
    public Guid DepartmentId { get; init; }
    public string Department { get; init; } = string.Empty;
    public Guid FuelTypeId { get; init; }
    public string FuelType { get; init; } = string.Empty;
    public string Operator { get; init; } = string.Empty;
    public long? Odometer { get; init; }
    public string Observations { get; init; } = string.Empty;
}

    public static IQueryable<DispatchRow> DispatchRows(AppDbContext db) =>
        from d in db.Dispatches.AsNoTracking()
        join t in db.Tickets on d.TicketId equals t.Id
        join e in db.Employees on t.EmployeeId equals e.Id
        join v in db.Vehicles on t.VehicleId equals v.Id
        join dep in db.Departments on t.DepartmentId equals dep.Id
        join f in db.FuelTypes on t.FuelTypeId equals f.Id
        join s in db.Stations on d.StationId equals s.Id
        join k in db.Tanks on d.TankId equals k.Id
        join u in db.Users on d.OperatorId equals u.Id
        select new DispatchRow
        {
            Id = d.Id,
            TicketId = t.Id,
            TicketNumber = t.Number,
            TicketStatus = t.Status,
            AuthorizedQuantity = t.AuthorizedQuantity,
            Quantity = d.Quantity,
            Difference = d.Difference,
            DifferenceReason = d.DifferenceReason,
            OccurredAt = d.OccurredAt,
            StationId = s.Id,
            Station = s.Name,
            TankId = k.Id,
            Tank = k.Code,
            EmployeeId = e.Id,
            EmployeeName = e.FullName,
            VehicleId = v.Id,
            VehiclePlate = v.Plate,
            DepartmentId = dep.Id,
            Department = dep.Name,
            FuelTypeId = f.Id,
            FuelType = f.Name,
            Operator = u.DisplayName,
            Odometer = d.Odometer,
            Observations = d.Observations,
        };

    // RF-18: cierre diario por estación con acta digital y PDF.
    private static void MapCloses(WebApplication app)
    {
        var group = app.MapGroup("/api/cierres").RequireAuthorization().AddEndpointFilter<RequestValidationFilter>();

        group.MapGet("/previo", async (Guid stationId, DateOnly day, AppDbContext db) =>
        {
            if (!await db.Stations.AnyAsync(x => x.Id == stationId)) return Results.NotFound();
            var (lines, count, volume) = await ComputeAsync(db, stationId, day);
            return Results.Ok(new { stationId, day, dispatchCount = count, dispatchedVolume = volume, lines });
        });

        group.MapPost("/", async (DailyCloseRequest body, AppDbContext db, AuditWriter audit, HttpContext http, TimeProvider time) =>
        {
            var now = time.GetUtcNow();
            if (body.Day > BusinessClock.LocalDay(now)) return Results.UnprocessableEntity(new { error = "No se puede cerrar un día futuro." });
            if (body.Counts.Any(x => x.Counted < 0 || decimal.Round(x.Counted, 3) != x.Counted))
                return Results.UnprocessableEntity(new { error = "Las mediciones deben ser positivas y con máximo 3 decimales." });
            if (body.Counts.Select(x => x.TankId).Distinct().Count() != body.Counts.Count)
                return Results.UnprocessableEntity(new { error = "Cada tanque se mide una sola vez." });
            await using var tx = await db.Database.BeginTransactionAsync();
            // Exclusivo: espera a los movimientos en curso de la estación para ese día.
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({InventoryLedger.CloseLockKey(body.StationId, body.Day)}, 3))");
            if (!await db.Stations.AnyAsync(x => x.Id == body.StationId)) return Results.UnprocessableEntity(new { error = "Estación inexistente." });
            if (await db.DailyCloses.AnyAsync(x => x.StationId == body.StationId && x.Day == body.Day))
                return Results.Conflict(new { error = "Ese día ya fue cerrado para la estación." });
            var (lines, count, volume) = await ComputeAsync(db, body.StationId, body.Day);
            var counts = body.Counts.ToDictionary(x => x.TankId, x => x.Counted);
            if (lines.Any(l => !counts.ContainsKey(l.TankId)) || counts.Keys.Any(k => lines.All(l => l.TankId != k)))
                return Results.UnprocessableEntity(new { error = "Debes medir exactamente los tanques de la estación." });
            var close = new DailyClose
            {
                StationId = body.StationId, Day = body.Day, DispatchCount = count, DispatchedVolume = volume, Actor = http.Actor(),
                ClosedAt = TicketService.Truncate(now), Notes = body.Notes?.Trim() ?? string.Empty,
                Lines = lines.Select(l => new DailyCloseLine
                {
                    TankId = l.TankId, Opening = l.Opening, Inputs = l.Inputs, Outputs = l.Outputs, Expected = l.Expected,
                    Counted = counts[l.TankId], Difference = counts[l.TankId] - l.Expected,
                }).ToList(),
            };
            db.DailyCloses.Add(close);
            foreach (var line in close.Lines.Where(x => x.Difference != 0))
                await Notifier.NotifyAsync(db, NotificationKind.InventoryAdjustment, $"close-diff:{close.Id:N}:{line.TankId:N}", line.TankId.ToString(), now,
                    $"Cierre {body.Day:yyyy-MM-dd}: diferencia de {DocumentRenderer.Gallons(line.Difference)} gal en un tanque. Registra un ajuste si corresponde.");
            await audit.WriteAsync(http.Actor(), http.Ip(), "daily_close", "DailyClose", close.Id.ToString());
            await tx.CommitAsync();
            return Results.Created($"/api/cierres/{close.Id}", new { close.Id });
        }).RequireAuthorization("close");

        group.MapGet("/", async (AppDbContext db, Guid? stationId, int page = 1) =>
        {
            if (page is < 1 or > 100000) return Results.BadRequest();
            var query = from c in db.DailyCloses.AsNoTracking()
                        join s in db.Stations on c.StationId equals s.Id
                        select new { c.Id, c.StationId, Station = s.Name, c.Day, c.DispatchCount, c.DispatchedVolume, c.ClosedAt, c.Notes };
            if (stationId is { } station) query = query.Where(x => x.StationId == station);
            return Results.Ok(new { items = await query.OrderByDescending(x => x.Day).Skip((page - 1) * 50).Take(50).ToListAsync(), total = await query.CountAsync() });
        });

        group.MapGet("/{id:guid}", async (Guid id, AppDbContext db) =>
        {
            var close = await db.DailyCloses.AsNoTracking().Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == id);
            return close is null ? Results.NotFound() : Results.Ok(close);
        });

        group.MapGet("/{id:guid}/pdf", async (Guid id, AppDbContext db) =>
        {
            var close = await db.DailyCloses.AsNoTracking().Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == id);
            if (close is null) return Results.NotFound();
            var station = await db.Stations.AsNoTracking().SingleAsync(x => x.Id == close.StationId);
            var actor = await db.Users.AsNoTracking().Where(u => u.Id.ToString() == close.Actor).Select(u => u.DisplayName).SingleOrDefaultAsync() ?? close.Actor;
            var tanks = await (from t in db.Tanks.AsNoTracking() join f in db.FuelTypes on t.FuelTypeId equals f.Id select new { t.Id, t.Code, Fuel = f.Name })
                .ToDictionaryAsync(x => x.Id);
            var (start, end) = BusinessClock.DayBounds(close.Day);
            var dispatches = await DispatchRows(db).Where(x => x.StationId == close.StationId && x.OccurredAt >= start && x.OccurredAt < end)
                .OrderBy(x => x.OccurredAt).ToListAsync();
            var header = new TableDocument($"Acta de cierre diario — {station.Name} — {close.Day:yyyy-MM-dd}",
                [("Estación", $"{station.Name} ({station.Code})"), ("Día operativo", close.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                 ("Despachos confirmados", close.DispatchCount.ToString(CultureInfo.InvariantCulture)),
                 ("Volumen despachado", DocumentRenderer.Gallons(close.DispatchedVolume) + " gal"),
                 ("Cerrado por", actor), ("Cerrado el", BusinessClock.Format(close.ClosedAt)), ("Observaciones", close.Notes.Length > 0 ? close.Notes : "—")],
                ["Tanque", "Combustible", "Apertura", "Entradas", "Salidas", "Esperado", "Medido", "Diferencia"],
                close.Lines.Select(l => (IReadOnlyList<object?>)[tanks[l.TankId].Code, tanks[l.TankId].Fuel, l.Opening, l.Inputs, l.Outputs, l.Expected, l.Counted, l.Difference]).ToList());
            var detail = new TableDocument("Despachos confirmados", [], ["Hora", "Ticket", "Empleado", "Vehículo", "Tanque", "Galones", "Operador"],
                dispatches.Select(d => (IReadOnlyList<object?>)[d.OccurredAt, d.TicketNumber, d.EmployeeName, d.VehiclePlate, d.Tank, d.Quantity, d.Operator]).ToList());
            return Results.File(DocumentRenderer.TablePdf(header, detail), "application/pdf", $"cierre-{station.Code}-{close.Day:yyyyMMdd}.pdf");
        });
    }

    public sealed record CloseLine(Guid TankId, string Tank, string FuelType, decimal Opening, decimal Inputs, decimal Outputs, decimal Expected);

    // Apertura = suma de movimientos previos al día (todo saldo nace de movimientos); esperado = apertura + entradas - salidas.
    private static async Task<(List<CloseLine> Lines, int Count, decimal Volume)> ComputeAsync(AppDbContext db, Guid stationId, DateOnly day)
    {
        var (start, end) = BusinessClock.DayBounds(day);
        var tanks = await (from t in db.Tanks.AsNoTracking()
                           join f in db.FuelTypes on t.FuelTypeId equals f.Id
                           where t.StationId == stationId
                           orderby t.Code
                           select new { t.Id, t.Code, t.Active, Fuel = f.Name }).ToListAsync();
        var ids = tanks.Select(t => t.Id).ToList();
        var before = await db.InventoryMovements.AsNoTracking().Where(m => ids.Contains(m.TankId) && m.OccurredAt < start)
            .GroupBy(m => m.TankId).Select(g => new { g.Key, Total = g.Sum(x => x.Quantity) }).ToDictionaryAsync(x => x.Key, x => x.Total);
        var during = await db.InventoryMovements.AsNoTracking().Where(m => ids.Contains(m.TankId) && m.OccurredAt >= start && m.OccurredAt < end)
            .GroupBy(m => m.TankId).Select(g => new { g.Key, In = g.Where(x => x.Quantity > 0).Sum(x => x.Quantity), Out = -g.Where(x => x.Quantity < 0).Sum(x => x.Quantity) })
            .ToDictionaryAsync(x => x.Key);
        var lines = tanks.Where(t => t.Active || during.ContainsKey(t.Id)).Select(t =>
        {
            var opening = before.GetValueOrDefault(t.Id);
            var inputs = during.TryGetValue(t.Id, out var d) ? d.In : 0;
            var outputs = d?.Out ?? 0;
            return new CloseLine(t.Id, t.Code, t.Fuel, opening, inputs, outputs, opening + inputs - outputs);
        }).ToList();
        var dispatches = db.Dispatches.AsNoTracking().Where(x => x.StationId == stationId && x.OccurredAt >= start && x.OccurredAt < end);
        return (lines, await dispatches.CountAsync(), await dispatches.SumAsync(x => (decimal?)x.Quantity) ?? 0);
    }
}
