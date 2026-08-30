using System.Globalization;
using Combustible.Api.Tickets;
using Combustible.Domain;
using Combustible.Infrastructure.Data;
using Combustible.Infrastructure.Documents;
using Microsoft.EntityFrameworkCore;

namespace Combustible.Api.Endpoints;

// RF-19: filtros por fecha, empleado, vehículo, departamento, combustible y estado.
public sealed record ReportFilter(DateOnly? From, DateOnly? To, Guid? EmployeeId, Guid? VehicleId, Guid? DepartmentId,
    Guid? FuelTypeId, string? Status, Guid? TankId, string? Kind, string? GroupBy, string? Format);

public static class ReportEndpoints
{
    private const int JsonLimit = 500;
    private const int ExportLimit = 20000;

    public static string StatusLabel(TicketStatus status) => status switch
    {
        TicketStatus.Created => "Creado", TicketStatus.Sent => "Enviado", TicketStatus.Pending => "Pendiente",
        TicketStatus.NearExpiry => "Próximo a vencer", TicketStatus.Expired => "Vencido", TicketStatus.Consumed => "Consumido",
        TicketStatus.Voided => "Anulado", _ => status.ToString(),
    };

    public static string KindLabel(MovementKind kind) => kind switch
    {
        MovementKind.Receipt => "Recepción", MovementKind.Purchase => "Compra", MovementKind.TransferIn => "Transferencia entrada",
        MovementKind.TransferOut => "Transferencia salida", MovementKind.Dispatch => "Despacho", MovementKind.Shrinkage => "Merma",
        MovementKind.PositiveAdjustment => "Ajuste positivo", MovementKind.NegativeAdjustment => "Ajuste negativo", _ => kind.ToString(),
    };

    public static void MapReports(this WebApplication app)
    {
        var group = app.MapGroup("/api/reportes").RequireAuthorization("reports");

        group.MapGet("/tickets", async ([AsParameters] ReportFilter filter, AppDbContext db, AuditWriter audit, HttpContext http) =>
        {
            var query = TicketEndpoints.TicketRows(db);
            if (filter.From is { } from) query = query.Where(x => x.IssuedAt >= BusinessClock.DayBounds(from).Start);
            if (filter.To is { } to) query = query.Where(x => x.IssuedAt < BusinessClock.DayBounds(to).End);
            if (filter.EmployeeId is { } employee) query = query.Where(x => x.EmployeeId == employee);
            if (filter.VehicleId is { } vehicle) query = query.Where(x => x.VehicleId == vehicle);
            if (filter.DepartmentId is { } department) query = query.Where(x => x.DepartmentId == department);
            if (filter.FuelTypeId is { } fuel) query = query.Where(x => x.FuelTypeId == fuel);
            if (filter.Status is not null)
            {
                if (!Enum.TryParse<TicketStatus>(filter.Status, out var status)) return Results.BadRequest(new { error = "Estado inválido." });
                query = query.Where(x => x.Status == status);
            }
            var rows = query.OrderBy(x => x.IssuedAt).Select(x => new
            {
                x.Id, x.Number, x.Status, x.IssuedAt, x.ExpiresAt, x.EmployeeName, x.VehiclePlate, x.Department, x.FuelType, x.AuthorizedQuantity,
                Dispatched = db.Dispatches.Where(d => d.TicketId == x.Id).Select(d => (decimal?)d.Quantity).FirstOrDefault(),
            });
            return await RespondAsync(filter, rows, "Reporte de tickets", "tickets", db, audit, http,
                ["Número", "Estado", "Emitido", "Vence", "Empleado", "Vehículo", "Departamento", "Combustible", "Autorizado (gal)", "Despachado (gal)"],
                x => [x.Number, StatusLabel(x.Status), x.IssuedAt, x.ExpiresAt, x.EmployeeName, x.VehiclePlate, x.Department, x.FuelType, x.AuthorizedQuantity, x.Dispatched]);
        });

        group.MapGet("/despachos", async ([AsParameters] ReportFilter filter, AppDbContext db, AuditWriter audit, HttpContext http) =>
        {
            var query = InventoryEndpoints.DispatchRows(db);
            if (filter.From is { } from) query = query.Where(x => x.OccurredAt >= BusinessClock.DayBounds(from).Start);
            if (filter.To is { } to) query = query.Where(x => x.OccurredAt < BusinessClock.DayBounds(to).End);
            if (filter.EmployeeId is { } employee) query = query.Where(x => x.EmployeeId == employee);
            if (filter.VehicleId is { } vehicle) query = query.Where(x => x.VehicleId == vehicle);
            if (filter.DepartmentId is { } department) query = query.Where(x => x.DepartmentId == department);
            if (filter.FuelTypeId is { } fuel) query = query.Where(x => x.FuelTypeId == fuel);
            if (filter.Status is not null)
            {
                if (!Enum.TryParse<TicketStatus>(filter.Status, out var status)) return Results.BadRequest(new { error = "Estado inválido." });
                query = query.Where(x => x.TicketStatus == status);
            }
            return await RespondAsync(filter, query.OrderBy(x => x.OccurredAt), "Reporte de despachos", "despachos", db, audit, http,
                ["Fecha y hora", "Ticket", "Estación", "Tanque", "Empleado", "Vehículo", "Departamento", "Combustible", "Autorizado (gal)", "Servido (gal)", "Diferencia (gal)", "Operador"],
                x => [x.OccurredAt, x.TicketNumber, x.Station, x.Tank, x.EmployeeName, x.VehiclePlate, x.Department, x.FuelType, x.AuthorizedQuantity, x.Quantity, x.Difference, x.Operator]);
        });

        group.MapGet("/movimientos", async ([AsParameters] ReportFilter filter, AppDbContext db, AuditWriter audit, HttpContext http) =>
        {
            var query = InventoryEndpoints.MovementRows(db);
            if (filter.From is { } from) query = query.Where(x => x.OccurredAt >= BusinessClock.DayBounds(from).Start);
            if (filter.To is { } to) query = query.Where(x => x.OccurredAt < BusinessClock.DayBounds(to).End);
            if (filter.TankId is { } tank) query = query.Where(x => x.TankId == tank);
            if (filter.Kind is not null)
            {
                if (!Enum.TryParse<MovementKind>(filter.Kind, out var kind)) return Results.BadRequest(new { error = "Tipo de movimiento inválido." });
                query = query.Where(x => x.Kind == kind);
            }
            return await RespondAsync(filter, query.OrderBy(x => x.Id), "Movimientos de inventario", "movimientos", db, audit, http,
                ["Fecha y hora", "Estación", "Tanque", "Combustible", "Tipo", "Cantidad (gal)", "Saldo resultante (gal)", "Usuario", "Motivo"],
                x => [x.OccurredAt, x.Station, x.Tank, x.FuelType, KindLabel(x.Kind), x.Quantity, x.BalanceAfter, x.Actor, x.Reason]);
        });

        // Consumo agregado por departamento o por vehículo en el periodo.
        group.MapGet("/consumo", async ([AsParameters] ReportFilter filter, AppDbContext db, AuditWriter audit, HttpContext http) =>
        {
            var query = InventoryEndpoints.DispatchRows(db);
            if (filter.From is { } from) query = query.Where(x => x.OccurredAt >= BusinessClock.DayBounds(from).Start);
            if (filter.To is { } to) query = query.Where(x => x.OccurredAt < BusinessClock.DayBounds(to).End);
            if (filter.DepartmentId is { } department) query = query.Where(x => x.DepartmentId == department);
            if (filter.FuelTypeId is { } fuel) query = query.Where(x => x.FuelTypeId == fuel);
            if (filter.VehicleId is { } vehicle) query = query.Where(x => x.VehicleId == vehicle);
            if (filter.EmployeeId is { } employee) query = query.Where(x => x.EmployeeId == employee);
            var byVehicle = string.Equals(filter.GroupBy, "vehiculo", StringComparison.OrdinalIgnoreCase);
            var grouped = byVehicle
                ? query.GroupBy(x => x.VehiclePlate).Select(g => new ConsumptionRow { Name = g.Key, Count = g.Count(), Volume = g.Sum(x => x.Quantity) })
                : query.GroupBy(x => x.Department).Select(g => new ConsumptionRow { Name = g.Key, Count = g.Count(), Volume = g.Sum(x => x.Quantity) });
            return await RespondAsync(filter, grouped.OrderByDescending(x => x.Volume), byVehicle ? "Consumo por vehículo" : "Consumo por departamento",
                "consumo", db, audit, http, [byVehicle ? "Vehículo" : "Departamento", "Despachos", "Volumen (gal)"], x => [x.Name, x.Count, x.Volume]);
        });

        // RF-22: dashboard ejecutivo.
        app.MapGet("/api/dashboard", async (AppDbContext db, TimeProvider time) =>
        {
            var now = time.GetUtcNow();
            var today = BusinessClock.LocalDay(now);
            var (dayStart, dayEnd) = BusinessClock.DayBounds(today);
            var (monthStart, _) = BusinessClock.DayBounds(new DateOnly(today.Year, today.Month, 1));
            var tanks = await (from t in db.Tanks.AsNoTracking()
                               join s in db.Stations on t.StationId equals s.Id
                               join f in db.FuelTypes on t.FuelTypeId equals f.Id
                               where t.Active
                               orderby s.Code, t.Code
                               select new { t.Id, t.Code, Station = s.Name, FuelType = f.Name, t.Balance, t.Capacity, t.CriticalLevel, Critical = t.Balance <= t.CriticalLevel })
                .ToListAsync();
            var dispatches = db.Dispatches.AsNoTracking();
            var month = InventoryEndpoints.DispatchRows(db).Where(x => x.OccurredAt >= monthStart && x.OccurredAt < dayEnd);
            return Results.Ok(new
            {
                generatedAt = now,
                inventory = tanks,
                dispatchedToday = await dispatches.Where(x => x.OccurredAt >= dayStart && x.OccurredAt < dayEnd).SumAsync(x => (decimal?)x.Quantity) ?? 0,
                dispatchedMonth = await month.SumAsync(x => (decimal?)x.Quantity) ?? 0,
                activeTickets = await db.Tickets.CountAsync(t => TicketService.Active.Contains(t.Status) && t.ExpiresAt > now),
                nearExpiryTickets = await db.Tickets.CountAsync(t => t.Status == TicketStatus.NearExpiry && t.ExpiresAt > now),
                expiredTickets = await db.Tickets.CountAsync(t => t.Status == TicketStatus.Expired),
                expiredThisMonth = await db.Tickets.CountAsync(t => t.Status == TicketStatus.Expired && t.ExpiresAt >= monthStart),
                pendingRequests = await db.FuelRequests.CountAsync(r => r.Status == RequestStatus.Pending),
                byDepartment = await month.GroupBy(x => x.Department).Select(g => new { name = g.Key, volume = g.Sum(x => x.Quantity), count = g.Count() })
                    .OrderByDescending(x => x.volume).ToListAsync(),
                byVehicle = await month.GroupBy(x => x.VehiclePlate).Select(g => new { name = g.Key, volume = g.Sum(x => x.Quantity), count = g.Count() })
                    .OrderByDescending(x => x.volume).Take(10).ToListAsync(),
            });
        }).RequireAuthorization();

        // RF-23: bandeja de alertas por usuario.
        var notifications = app.MapGroup("/api/notificaciones").RequireAuthorization();
        notifications.MapGet("/", async (AppDbContext db, HttpContext http, bool unread = false, int page = 1) =>
        {
            if (page is < 1 or > 100000) return Results.BadRequest();
            var user = Guid.Parse(http.Actor());
            var query = db.Notifications.AsNoTracking().Select(n => new
            {
                n.Id, n.Kind, n.Message, n.EntityId, n.CreatedAt,
                Read = db.NotificationReads.Any(r => r.NotificationId == n.Id && r.UserId == user),
            });
            if (unread) query = query.Where(x => !x.Read);
            return Results.Ok(new
            {
                items = await query.OrderByDescending(x => x.Id).Skip((page - 1) * 50).Take(50).ToListAsync(),
                total = await query.CountAsync(),
                unread = await db.Notifications.CountAsync(n => !db.NotificationReads.Any(r => r.NotificationId == n.Id && r.UserId == user)),
            });
        });
        notifications.MapPost("/{id:long}/leida", async (long id, AppDbContext db, HttpContext http) =>
        {
            if (!await db.Notifications.AnyAsync(x => x.Id == id)) return Results.NotFound();
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "NotificationReads" ("NotificationId", "UserId", "ReadAt") VALUES ({id}, {Guid.Parse(http.Actor())}, {DateTimeOffset.UtcNow})
                ON CONFLICT DO NOTHING
                """);
            return Results.NoContent();
        });
        notifications.MapPost("/leidas", async (AppDbContext db, HttpContext http) =>
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "NotificationReads" ("NotificationId", "UserId", "ReadAt")
                SELECT "Id", {Guid.Parse(http.Actor())}, {DateTimeOffset.UtcNow} FROM "Notifications"
                ON CONFLICT DO NOTHING
                """);
            return Results.NoContent();
        });
    }

    public sealed record ConsumptionRow
    {
        public string Name { get; init; } = string.Empty;
        public int Count { get; init; }
        public decimal Volume { get; init; }
    }

    private static async Task<IResult> RespondAsync<T>(ReportFilter filter, IQueryable<T> rows, string title, string name, AppDbContext db,
        AuditWriter audit, HttpContext http, IReadOnlyList<string> headers, Func<T, IReadOnlyList<object?>> project)
    {
        var format = (filter.Format ?? "json").ToLowerInvariant();
        if (format is not ("json" or "csv" or "xlsx" or "pdf")) return Results.BadRequest(new { error = "Formato inválido: json, csv, xlsx o pdf." });
        var total = await rows.CountAsync();
        if (format == "json")
            return Results.Ok(new { items = await rows.Take(JsonLimit).ToListAsync(), total, truncated = total > JsonLimit });
        if (total > ExportLimit)
            return Results.UnprocessableEntity(new { error = $"El reporte tiene {total} filas; acota los filtros a {ExportLimit} o menos." });
        var data = (await rows.ToListAsync()).Select(project).ToList();
        var summary = new List<(string, string)>
        {
            ("Generado", BusinessClock.Format(DateTimeOffset.UtcNow)),
            ("Periodo", $"{filter.From?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "inicio"} a {filter.To?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "hoy"}"),
            ("Registros", total.ToString(CultureInfo.InvariantCulture)),
        };
        if (filter.Status is not null && Enum.TryParse<TicketStatus>(filter.Status, out var status)) summary.Add(("Estado", StatusLabel(status)));
        var document = new TableDocument(title, summary, headers, data);
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            await audit.WriteAsync(http.Actor(), http.Ip(), "report_export", "Report", $"{name}.{format}");
            await tx.CommitAsync();
        }
        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture);
        return format switch
        {
            "csv" => Results.File(DocumentRenderer.Csv(document), "text/csv; charset=utf-8", $"{name}-{stamp}.csv"),
            "xlsx" => Results.File(DocumentRenderer.Xlsx(document), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"{name}-{stamp}.xlsx"),
            _ => Results.File(DocumentRenderer.TablePdf(document), "application/pdf", $"{name}-{stamp}.pdf"),
        };
    }
}
