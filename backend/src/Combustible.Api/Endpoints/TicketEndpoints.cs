using Combustible.Api.Tickets;
using Combustible.Application;
using Combustible.Domain;
using Combustible.Infrastructure.Data;
using Combustible.Infrastructure.Documents;
using Combustible.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace Combustible.Api.Endpoints;

public static class TicketEndpoints
{
    public static void MapTickets(this WebApplication app)
    {
        MapRequests(app);
        MapTicketQueries(app);
        MapSchedules(app);
        MapSettings(app);
        MapPublic(app);
    }

    private static void MapRequests(WebApplication app)
    {
        var group = app.MapGroup("/api/solicitudes").RequireAuthorization().AddEndpointFilter<RequestValidationFilter>();
        group.MapGet("/", async (AppDbContext db, string? status, int page = 1) =>
        {
            if (page is < 1 or > 100000) return Results.BadRequest();
            var query = from r in db.FuelRequests.AsNoTracking()
                        join e in db.Employees on r.EmployeeId equals e.Id
                        join v in db.Vehicles on r.VehicleId equals v.Id
                        join d in db.Departments on r.DepartmentId equals d.Id
                        join f in db.FuelTypes on r.FuelTypeId equals f.Id
                        select new
                        {
                            r.Id, r.Status, r.Origin, r.AuthorizedQuantity, r.RequestedAt, r.ExpiresAt, r.Notes, r.DecidedAt, r.DecisionReason,
                            r.Version, r.EmployeeId, r.VehicleId, r.DepartmentId, r.FuelTypeId, r.ScheduleId,
                            EmployeeName = e.FullName, VehiclePlate = v.Plate, Department = d.Name, FuelType = f.Name,
                            RequestedBy = db.Users.Where(u => u.Id.ToString() == r.RequestedBy).Select(u => u.DisplayName).FirstOrDefault() ?? r.RequestedBy,
                            DecidedBy = db.Users.Where(u => u.Id.ToString() == r.DecidedBy).Select(u => u.DisplayName).FirstOrDefault() ?? r.DecidedBy,
                            TicketId = db.Tickets.Where(t => t.RequestId == r.Id).Select(t => (Guid?)t.Id).FirstOrDefault(),
                            TicketNumber = db.Tickets.Where(t => t.RequestId == r.Id).Select(t => t.Number).FirstOrDefault(),
                        };
            if (status is not null)
            {
                if (!Enum.TryParse<RequestStatus>(status, out var parsed)) return Results.BadRequest(new { error = "Estado inválido." });
                query = query.Where(x => x.Status == parsed);
            }
            return Results.Ok(new { items = await query.OrderByDescending(x => x.RequestedAt).Skip((page - 1) * 50).Take(50).ToListAsync(), total = await query.CountAsync() });
        });

        group.MapPost("/", async (FuelRequestCreate body, AppDbContext db, TicketService tickets, AuditWriter audit, HttpContext http, TimeProvider time) =>
        {
            var now = time.GetUtcNow();
            var error = await tickets.ValidateRequestAsync(body.EmployeeId, body.VehicleId, body.DepartmentId, body.FuelTypeId,
                body.AuthorizedQuantity, body.ExpiresAt, now);
            if (error is not null) return Results.UnprocessableEntity(new { error });
            var request = new FuelRequest
            {
                EmployeeId = body.EmployeeId, VehicleId = body.VehicleId, DepartmentId = body.DepartmentId, FuelTypeId = body.FuelTypeId,
                AuthorizedQuantity = body.AuthorizedQuantity, ExpiresAt = body.ExpiresAt is { } e ? TicketService.Truncate(e) : null,
                RequestedAt = TicketService.Truncate(now), Origin = RequestOrigin.Manual, Status = RequestStatus.Pending,
                Notes = body.Notes?.Trim() ?? string.Empty, RequestedBy = http.Actor(),
            };
            await using var tx = await db.Database.BeginTransactionAsync();
            db.FuelRequests.Add(request);
            await audit.WriteAsync(http.Actor(), http.Ip(), "create", "FuelRequest", request.Id.ToString());
            await tx.CommitAsync();
            return Results.Created($"/api/solicitudes/{request.Id}", request);
        }).RequireAuthorization("request-create");

        group.MapPost("/{id:guid}/aprobar", async (Guid id, DecisionRequest body, AppDbContext db, TicketService tickets, AuditWriter audit,
            HttpContext http, TimeProvider time) =>
        {
            var now = time.GetUtcNow();
            IssuedTicket issued;
            await using (var tx = await db.Database.BeginTransactionAsync())
            {
                var request = await LockRequestAsync(db, id);
                if (request is null) return Results.NotFound();
                if (request.Version.ToString() != body.Version) return Results.StatusCode(412);
                if (request.Status != RequestStatus.Pending) return Results.Conflict(new { error = "La solicitud ya fue decidida." });
                var (result, error) = await tickets.IssueAsync(request, http.Actor(), now);
                if (error is not null) return Results.UnprocessableEntity(new { error });
                issued = result!;
                request.Status = RequestStatus.Approved;
                request.DecidedBy = http.Actor();
                request.DecidedAt = TicketService.Truncate(now);
                request.DecisionReason = body.Reason?.Trim() ?? string.Empty;
                request.Version = Guid.NewGuid();
                await audit.WriteAsync(http.Actor(), http.Ip(), "approve", "FuelRequest", id.ToString());
                await audit.WriteAsync(http.Actor(), http.Ip(), "ticket_issued", "Ticket", issued.Ticket.Id.ToString());
                await tx.CommitAsync();
            }
            var deliveries = await tickets.DeliverAsync(issued.Ticket.Id, http.Actor(), http.Ip());
            return Results.Ok(new { ticketId = issued.Ticket.Id, number = issued.Ticket.Number, deliveries = deliveries.Select(DeliveryView) });
        }).RequireAuthorization("request-approve");

        group.MapPost("/{id:guid}/rechazar", async (Guid id, DecisionRequest body, AppDbContext db, AuditWriter audit, HttpContext http, TimeProvider time) =>
        {
            if (string.IsNullOrWhiteSpace(body.Reason) || body.Reason.Trim().Length < 5)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["Reason"] = ["Indica el motivo del rechazo (mínimo 5 caracteres)."] });
            return await DecideAsync(db, audit, http, time, id, body, RequestStatus.Rejected, "reject");
        }).RequireAuthorization("request-approve");

        group.MapPost("/{id:guid}/cancelar", async (Guid id, DecisionRequest body, AppDbContext db, AuditWriter audit, HttpContext http, TimeProvider time) =>
            await DecideAsync(db, audit, http, time, id, body, RequestStatus.Cancelled, "cancel")).RequireAuthorization("request-create");
    }

    private static async Task<IResult> DecideAsync(AppDbContext db, AuditWriter audit, HttpContext http, TimeProvider time, Guid id,
        DecisionRequest body, RequestStatus status, string action)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var request = await LockRequestAsync(db, id);
        if (request is null) return Results.NotFound();
        if (request.Version.ToString() != body.Version) return Results.StatusCode(412);
        if (request.Status != RequestStatus.Pending) return Results.Conflict(new { error = "La solicitud ya fue decidida." });
        request.Status = status;
        request.DecidedBy = http.Actor();
        request.DecidedAt = TicketService.Truncate(time.GetUtcNow());
        request.DecisionReason = body.Reason?.Trim() ?? string.Empty;
        request.Version = Guid.NewGuid();
        await audit.WriteAsync(http.Actor(), http.Ip(), action, "FuelRequest", id.ToString());
        await tx.CommitAsync();
        return Results.NoContent();
    }

    private static Task<FuelRequest?> LockRequestAsync(AppDbContext db, Guid id) =>
        db.FuelRequests.FromSqlInterpolated($"SELECT * FROM \"FuelRequests\" WHERE \"Id\" = {id} FOR UPDATE").SingleOrDefaultAsync();

    private static object DeliveryView(TicketDelivery x) => new { x.Channel, x.Destination, x.Result, x.Detail, x.AttemptedAt };

    private static void MapTicketQueries(WebApplication app)
    {
        var group = app.MapGroup("/api/tickets").RequireAuthorization().AddEndpointFilter<RequestValidationFilter>();
        group.MapGet("/", async (AppDbContext db, string? status, string? q, int page = 1) =>
        {
            if (page is < 1 or > 100000) return Results.BadRequest();
            var query = TicketRows(db);
            if (status is not null)
            {
                if (!Enum.TryParse<TicketStatus>(status, out var parsed)) return Results.BadRequest(new { error = "Estado inválido." });
                query = query.Where(x => x.Status == parsed);
            }
            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = q.Trim().ToUpperInvariant();
                query = query.Where(x => x.Number == term || x.ShortCode == term || x.VehiclePlate == term);
            }
            return Results.Ok(new { items = await query.OrderByDescending(x => x.IssuedAt).Skip((page - 1) * 50).Take(50).ToListAsync(), total = await query.CountAsync() });
        });

        group.MapGet("/{id:guid}", async (Guid id, AppDbContext db) =>
        {
            var ticket = await TicketRows(db).SingleOrDefaultAsync(x => x.Id == id);
            if (ticket is null) return Results.NotFound();
            var deliveries = await db.TicketDeliveries.AsNoTracking().Where(x => x.TicketId == id).OrderBy(x => x.Id).ToListAsync();
            var dispatch = await db.Dispatches.AsNoTracking().Where(x => x.TicketId == id)
                .Select(x => new { x.Id, x.Quantity, x.Difference, x.DifferenceReason, x.OccurredAt, x.Observations, x.StationId, x.TankId }).SingleOrDefaultAsync();
            return Results.Ok(new { ticket, deliveries = deliveries.Select(DeliveryView), dispatch });
        });

        group.MapGet("/{id:guid}/pdf", async (Guid id, AppDbContext db, TicketService tickets, AuditWriter audit, HttpContext http) =>
        {
            var ticket = await db.Tickets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
            if (ticket is null) return Results.NotFound();
            if (!TicketService.Active.Contains(ticket.Status)) return Results.Conflict(new { error = "El ticket no está activo." });
            var pdf = DocumentRenderer.TicketPdf(await tickets.DocumentAsync(ticket));
            await using var tx = await db.Database.BeginTransactionAsync();
            await audit.WriteAsync(http.Actor(), http.Ip(), "ticket_pdf", "Ticket", id.ToString());
            await tx.CommitAsync();
            return Results.File(pdf, "application/pdf", $"{ticket.Number}.pdf");
        }).RequireAuthorization("request-approve");

        group.MapPost("/{id:guid}/reenviar", async (Guid id, AppDbContext db, TicketService tickets, HttpContext http) =>
        {
            var ticket = await db.Tickets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
            if (ticket is null) return Results.NotFound();
            if (!TicketService.Active.Contains(ticket.Status)) return Results.Conflict(new { error = "Solo se reenvían tickets activos." });
            var deliveries = await tickets.DeliverAsync(id, http.Actor(), http.Ip());
            return Results.Ok(new { deliveries = deliveries.Select(DeliveryView) });
        }).RequireAuthorization("request-approve");

        group.MapPost("/{id:guid}/anular", async (Guid id, VoidRequest body, AppDbContext db, AuditWriter audit, HttpContext http, TimeProvider time) =>
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            var ticket = await db.Tickets.FromSqlInterpolated($"SELECT * FROM \"Tickets\" WHERE \"Id\" = {id} FOR UPDATE").SingleOrDefaultAsync();
            if (ticket is null) return Results.NotFound();
            if (ticket.Version.ToString() != body.Version) return Results.StatusCode(412);
            if (!TicketService.Active.Contains(ticket.Status)) return Results.Conflict(new { error = "Solo se anulan tickets activos." });
            ticket.Status = TicketStatus.Voided;
            ticket.VoidedAt = TicketService.Truncate(time.GetUtcNow());
            ticket.VoidReason = body.Reason.Trim();
            ticket.Version = Guid.NewGuid();
            await audit.WriteAsync(http.Actor(), http.Ip(), "void", "Ticket", id.ToString());
            await tx.CommitAsync();
            return Results.NoContent();
        }).RequireAuthorization("request-approve");
    }

    public sealed record TicketRow
{
    public Guid Id { get; init; }
    public string Number { get; init; } = string.Empty;
    public string ShortCode { get; init; } = string.Empty;
    public TicketStatus Status { get; init; }
    public decimal AuthorizedQuantity { get; init; }
    public DateTimeOffset IssuedAt { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
    public DateTimeOffset? ConsumedAt { get; init; }
    public DateTimeOffset? VoidedAt { get; init; }
    public string VoidReason { get; init; } = string.Empty;
    public Guid Version { get; init; }
    public Guid RequestId { get; init; }
    public Guid EmployeeId { get; init; }
    public Guid VehicleId { get; init; }
    public Guid DepartmentId { get; init; }
    public Guid FuelTypeId { get; init; }
    public string EmployeeName { get; init; } = string.Empty;
    public string EmployeeCode { get; init; } = string.Empty;
    public string VehiclePlate { get; init; } = string.Empty;
    public string VehicleCode { get; init; } = string.Empty;
    public string Department { get; init; } = string.Empty;
    public string FuelType { get; init; } = string.Empty;
}

    public static IQueryable<TicketRow> TicketRows(AppDbContext db) =>
        from t in db.Tickets.AsNoTracking()
        join e in db.Employees on t.EmployeeId equals e.Id
        join v in db.Vehicles on t.VehicleId equals v.Id
        join d in db.Departments on t.DepartmentId equals d.Id
        join f in db.FuelTypes on t.FuelTypeId equals f.Id
        select new TicketRow
        {
            Id = t.Id,
            Number = t.Number,
            ShortCode = t.ShortCode,
            Status = t.Status,
            AuthorizedQuantity = t.AuthorizedQuantity,
            IssuedAt = t.IssuedAt,
            ExpiresAt = t.ExpiresAt,
            ConsumedAt = t.ConsumedAt,
            VoidedAt = t.VoidedAt,
            VoidReason = t.VoidReason,
            Version = t.Version,
            RequestId = t.RequestId,
            EmployeeId = t.EmployeeId,
            VehicleId = t.VehicleId,
            DepartmentId = t.DepartmentId,
            FuelTypeId = t.FuelTypeId,
            EmployeeName = e.FullName,
            EmployeeCode = e.Code,
            VehiclePlate = v.Plate,
            VehicleCode = v.InternalCode,
            Department = d.Name,
            FuelType = f.Name,
        };

    private static void MapSchedules(WebApplication app)
    {
        var group = app.MapGroup("/api/programaciones").RequireAuthorization().AddEndpointFilter<RequestValidationFilter>();
        group.MapGet("/", async (AppDbContext db) =>
            Results.Ok(new { items = await db.FuelSchedules.AsNoTracking().OrderBy(x => x.Name).ToListAsync() }));

        group.MapPost("/", async (ScheduleRequest body, AppDbContext db, TicketService tickets, AuditWriter audit, HttpContext http, TimeProvider time) =>
        {
            var schedule = new FuelSchedule();
            var error = await ApplyScheduleAsync(schedule, body, tickets, time.GetUtcNow());
            if (error is not null) return Results.UnprocessableEntity(new { error });
            await using var tx = await db.Database.BeginTransactionAsync();
            db.FuelSchedules.Add(schedule);
            await audit.WriteAsync(http.Actor(), http.Ip(), "create", "FuelSchedule", schedule.Id.ToString());
            await tx.CommitAsync();
            return Results.Created($"/api/programaciones/{schedule.Id}", schedule);
        }).RequireAuthorization("request-approve");

        group.MapPut("/{id:guid}", async (Guid id, ScheduleRequest body, AppDbContext db, TicketService tickets, AuditWriter audit,
            HttpContext http, TimeProvider time) =>
        {
            if (!Guid.TryParse(http.Request.Headers.IfMatch.ToString().Trim('"'), out var version))
                return Results.Problem(statusCode: 428, title: "Se requiere If-Match con la versión del registro.");
            await using var tx = await db.Database.BeginTransactionAsync();
            var schedule = await db.FuelSchedules.FromSqlInterpolated($"SELECT * FROM \"FuelSchedules\" WHERE \"Id\" = {id} FOR UPDATE").SingleOrDefaultAsync();
            if (schedule is null) return Results.NotFound();
            if (schedule.Version != version) return Results.StatusCode(412);
            var error = await ApplyScheduleAsync(schedule, body, tickets, time.GetUtcNow());
            if (error is not null) return Results.UnprocessableEntity(new { error });
            schedule.Version = Guid.NewGuid();
            await audit.WriteAsync(http.Actor(), http.Ip(), "update", "FuelSchedule", id.ToString());
            await tx.CommitAsync();
            return Results.Ok(schedule);
        }).RequireAuthorization("request-approve");
    }

    private static async Task<string?> ApplyScheduleAsync(FuelSchedule schedule, ScheduleRequest body, TicketService tickets, DateTimeOffset now)
    {
        var rule = Enum.Parse<QuantityRule>(body.Rule);
        if (rule == QuantityRule.Fixed && body.Quantity <= 0) return "Una programación de cantidad fija necesita cantidad mayor que cero.";
        if (body.EndsAt is { } end && end <= body.NextRunAt) return "La fecha final debe ser posterior a la próxima ejecución.";
        // Para la regla por historial la cantidad se calcula al ejecutar; aquí se validan los catálogos.
        var error = await tickets.ValidateRequestAsync(body.EmployeeId, body.VehicleId, body.DepartmentId, body.FuelTypeId,
            rule == QuantityRule.Fixed ? body.Quantity : 0.001m, null, now);
        if (error is not null) return error;
        schedule.Name = body.Name.Trim();
        schedule.EmployeeId = body.EmployeeId; schedule.VehicleId = body.VehicleId;
        schedule.DepartmentId = body.DepartmentId; schedule.FuelTypeId = body.FuelTypeId;
        schedule.Rule = rule; schedule.Quantity = rule == QuantityRule.Fixed ? body.Quantity : 0;
        schedule.HistorySize = body.HistorySize; schedule.Frequency = Enum.Parse<ScheduleFrequency>(body.Frequency);
        schedule.NextRunAt = TicketService.Truncate(body.NextRunAt); schedule.EndsAt = body.EndsAt is { } e ? TicketService.Truncate(e) : null;
        schedule.AutoApprove = body.AutoApprove; schedule.Active = body.Active;
        return null;
    }

    private static void MapSettings(WebApplication app)
    {
        var group = app.MapGroup("/api/parametros").RequireAuthorization().AddEndpointFilter<RequestValidationFilter>();
        group.MapGet("/", async (AppDbContext db) => Results.Ok(await db.TicketSettings.AsNoTracking().SingleAsync()));
        group.MapPut("/", async (SettingsRequest body, AppDbContext db, AuditWriter audit, HttpContext http) =>
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            var settings = await db.TicketSettings.FromSqlRaw("SELECT * FROM \"TicketSettings\" FOR UPDATE").SingleAsync();
            if (settings.Version.ToString() != body.Version) return Results.StatusCode(412);
            settings.Prefix = body.Prefix; settings.ResetAnnually = body.ResetAnnually; settings.ValidityDays = body.ValidityDays;
            settings.WarningHours = body.WarningHours; settings.MaxActiveTicketsPerVehicle = body.MaxActiveTicketsPerVehicle;
            settings.Version = Guid.NewGuid();
            await audit.WriteAsync(http.Actor(), http.Ip(), "update", "TicketSettings", settings.Id.ToString());
            await tx.CommitAsync();
            return Results.Ok(settings);
        }).RequireAuthorization("admin");
    }

    // RF-09: URL segura con QR descargable. El secreto es el token del ticket; se compara por hash.
    private static void MapPublic(WebApplication app)
    {
        var group = app.MapGroup("/api/publico/tickets").RequireRateLimiting("public");
        group.MapGet("/{key}", async (string key, AppDbContext db) =>
        {
            var ticket = await FindPublicAsync(db, key);
            if (ticket is null) return Results.NotFound();
            var row = await TicketRows(db).SingleAsync(x => x.Id == ticket.Id);
            return Results.Ok(new
            {
                row.Number, row.ShortCode, row.Status, row.AuthorizedQuantity, row.IssuedAt, row.ExpiresAt, row.EmployeeName,
                row.VehiclePlate, row.FuelType, row.Department, Active = TicketService.Active.Contains(row.Status),
            });
        });
        group.MapGet("/{key}/qr.png", async (string key, AppDbContext db, TicketService tickets) =>
        {
            var ticket = await FindPublicAsync(db, key);
            if (ticket is null) return Results.NotFound();
            if (!TicketService.Active.Contains(ticket.Status)) return Results.StatusCode(410);
            return Results.File(DocumentRenderer.QrPng(tickets.Payload(ticket).Payload), "image/png", $"{ticket.Number}.png");
        });
        group.MapGet("/{key}/pdf", async (string key, AppDbContext db, TicketService tickets) =>
        {
            var ticket = await FindPublicAsync(db, key);
            if (ticket is null) return Results.NotFound();
            if (!TicketService.Active.Contains(ticket.Status)) return Results.StatusCode(410);
            return Results.File(DocumentRenderer.TicketPdf(await tickets.DocumentAsync(ticket)), "application/pdf", $"{ticket.Number}.pdf");
        });
    }

    private static async Task<Ticket?> FindPublicAsync(AppDbContext db, string key)
    {
        var parts = key.Split('.');
        if (parts.Length != 2 || parts[1].Length != 22 || !Guid.TryParseExact(parts[0], "N", out var id)) return null;
        var ticket = await db.Tickets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
        if (ticket is null) return null;
        var expected = System.Text.Encoding.ASCII.GetBytes(ticket.TokenHash);
        var actual = System.Text.Encoding.ASCII.GetBytes(TicketSigner.HashToken(parts[1]));
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(expected, actual) ? ticket : null;
    }
}
