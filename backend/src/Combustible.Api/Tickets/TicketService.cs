using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using Combustible.Api.Endpoints;
using Combustible.Application;
using Combustible.Domain;
using Combustible.Infrastructure.Data;
using Combustible.Infrastructure.Documents;
using Combustible.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace Combustible.Api.Tickets;

public sealed record PublicLinks(string BaseUrl)
{
    public string Ticket(Guid id, string token) => $"{BaseUrl.TrimEnd('/')}/ticket/{id:N}.{token}";
}

public sealed record IssuedTicket(Ticket Ticket, string Token);

public sealed record QrCheck(Ticket? Ticket, string? Error);

public sealed class TicketService(AppDbContext db, TicketSigner signer, FieldProtector protector, IEmailSender email,
    ISmsSender sms, AuditWriter audit, PublicLinks links)
{
    public static readonly TicketStatus[] Active = [TicketStatus.Created, TicketStatus.Sent, TicketStatus.Pending, TicketStatus.NearExpiry];
    private const string ShortCodeAlphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    public static string TokenPurpose(Guid ticketId) => $"Ticket.Token:{ticketId:N}";

    // PostgreSQL guarda microsegundos: se trunca antes de firmar para que la firma sea reproducible.
    public static DateTimeOffset Truncate(DateTimeOffset value) => new(value.UtcTicks / 10 * 10, TimeSpan.Zero);

    // RF-05 / RF-11: reglas comunes a solicitudes manuales, programadas y recurrentes.
    public async Task<string?> ValidateRequestAsync(Guid employeeId, Guid vehicleId, Guid departmentId, Guid fuelTypeId,
        decimal quantity, DateTimeOffset? expiresAt, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        if (quantity <= 0 || decimal.Round(quantity, 3) != quantity) return "La cantidad debe ser mayor que cero y tener como máximo 3 decimales.";
        var employee = await db.Employees.AsNoTracking().SingleOrDefaultAsync(x => x.Id == employeeId, cancellationToken);
        if (employee is not { Active: true }) return "Empleado inexistente o inactivo.";
        var vehicle = await db.Vehicles.AsNoTracking().SingleOrDefaultAsync(x => x.Id == vehicleId, cancellationToken);
        if (vehicle is not { Active: true }) return "Vehículo inexistente o inactivo.";
        if (!await db.Departments.AnyAsync(x => x.Id == departmentId && x.Active, cancellationToken)) return "Departamento inexistente o inactivo.";
        if (departmentId != employee.DepartmentId && departmentId != vehicle.DepartmentId)
            return "El departamento debe ser el del empleado o el del vehículo.";
        if (!await db.FuelTypes.AnyAsync(x => x.Id == fuelTypeId && x.Active, cancellationToken)) return "Tipo de combustible inexistente o inactivo.";
        if (quantity > vehicle.TankCapacity) return "La cantidad supera la capacidad del tanque del vehículo.";
        if (expiresAt is { } expiry && (expiry <= now.AddHours(1) || expiry > now.AddDays(90)))
            return "El vencimiento debe estar entre 1 hora y 90 días desde ahora.";
        return null;
    }

    // Se ejecuta dentro de la transacción del llamador; la numeración queda bloqueada hasta el commit.
    public async Task<(IssuedTicket? Issued, string? Error)> IssueAsync(FuelRequest request, string actor, DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("La emisión requiere una transacción.");
        var error = await ValidateRequestAsync(request.EmployeeId, request.VehicleId, request.DepartmentId, request.FuelTypeId,
            request.AuthorizedQuantity, request.ExpiresAt, now, cancellationToken);
        if (error is not null) return (null, error);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({request.VehicleId.ToString()}, 2))", cancellationToken);
        var settings = await db.TicketSettings.AsNoTracking().SingleAsync(cancellationToken);
        var activeCount = await db.Tickets.CountAsync(t => t.VehicleId == request.VehicleId && Active.Contains(t.Status) && t.ExpiresAt > now, cancellationToken);
        if (activeCount >= settings.MaxActiveTicketsPerVehicle)
            return (null, $"El vehículo ya tiene {activeCount} ticket(s) activo(s); el máximo configurado es {settings.MaxActiveTicketsPerVehicle}.");
        var year = BusinessClock.ToLocal(now).Year;
        var scope = settings.ResetAnnually ? $"{settings.Prefix}-{year}" : settings.Prefix;
        // Fila de tabla y no SEQUENCE: un rollback devuelve el número y no quedan huecos (RF-08).
        var sequence = (await db.Database.SqlQuery<long>(
            $"INSERT INTO \"TicketSequences\" (\"Scope\", \"Last\") VALUES ({scope}, 1) ON CONFLICT (\"Scope\") DO UPDATE SET \"Last\" = \"TicketSequences\".\"Last\" + 1 RETURNING \"Last\" AS \"Value\"")
            .ToListAsync(cancellationToken)).Single();
        var token = TicketSigner.NewToken();
        var ticket = new Ticket
        {
            Number = $"{settings.Prefix}-{year}-{sequence.ToString("D6", CultureInfo.InvariantCulture)}",
            Scope = scope,
            Sequence = sequence,
            ShortCode = NewShortCode(),
            RequestId = request.Id,
            EmployeeId = request.EmployeeId,
            VehicleId = request.VehicleId,
            DepartmentId = request.DepartmentId,
            FuelTypeId = request.FuelTypeId,
            AuthorizedQuantity = request.AuthorizedQuantity,
            IssuedAt = Truncate(now),
            ExpiresAt = Truncate(request.ExpiresAt ?? now.AddDays(settings.ValidityDays)),
            Status = TicketStatus.Created,
            TokenHash = TicketSigner.HashToken(token),
            IssuedBy = actor,
        };
        ticket.TokenCipher = protector.Protect(token, TokenPurpose(ticket.Id));
        signer.Sign(ticket);
        db.Tickets.Add(ticket);
        return (new IssuedTicket(ticket, token), null);
    }

    // RF-07: firma válida + token correcto + fila sin alterar. La cantidad se lee de la base, nunca del QR.
    public async Task<QrCheck> VerifyQrAsync(string payload, bool forUpdate, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        if (!TicketSigner.TryParsePayload(payload, out var id, out var token, out var signature)) return new QrCheck(null, "El código QR no tiene un formato válido.");
        var ticket = forUpdate
            ? await db.Tickets.FromSqlInterpolated($"SELECT * FROM \"Tickets\" WHERE \"Id\" = {id} FOR UPDATE").SingleOrDefaultAsync(cancellationToken)
            : await db.Tickets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (ticket is null || !signer.Verify(ticket, token, signature)) return new QrCheck(null, "La firma del código QR no es válida.");
        return ticket.Status switch
        {
            TicketStatus.Consumed => new QrCheck(ticket, "El ticket ya fue consumido."),
            TicketStatus.Voided => new QrCheck(ticket, "El ticket fue anulado."),
            TicketStatus.Expired => new QrCheck(ticket, "El ticket está vencido."),
            _ when ticket.ExpiresAt <= now => new QrCheck(ticket, "El ticket está vencido."),
            _ => new QrCheck(ticket, null),
        };
    }

    public (string Token, string Payload) Payload(Ticket ticket)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        var token = protector.Unprotect(ticket.TokenCipher, TokenPurpose(ticket.Id));
        return (token, TicketSigner.Payload(ticket, token));
    }

    public async Task<TicketDocument> DocumentAsync(Ticket ticket, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        var (token, payload) = Payload(ticket);
        var employee = await db.Employees.AsNoTracking().SingleAsync(x => x.Id == ticket.EmployeeId, cancellationToken);
        var vehicle = await db.Vehicles.AsNoTracking().SingleAsync(x => x.Id == ticket.VehicleId, cancellationToken);
        var department = await db.Departments.AsNoTracking().SingleAsync(x => x.Id == ticket.DepartmentId, cancellationToken);
        var fuel = await db.FuelTypes.AsNoTracking().SingleAsync(x => x.Id == ticket.FuelTypeId, cancellationToken);
        return new TicketDocument(ticket.Number, ticket.ShortCode, employee.FullName, employee.Code, vehicle.Plate, vehicle.InternalCode,
            department.Name, fuel.Name, ticket.AuthorizedQuantity, ticket.IssuedAt, ticket.ExpiresAt, payload, links.Ticket(ticket.Id, token));
    }

    // RF-06 / RF-09: entrega por correo y SMS fuera de la transacción de emisión.
    public async Task<IReadOnlyList<TicketDelivery>> DeliverAsync(Guid ticketId, string actor, string ip, CancellationToken cancellationToken = default)
    {
        // El llamador pudo dejar la misma fila rastreada con valores previos al commit.
        db.ChangeTracker.Clear();
        var snapshot = await db.Tickets.AsNoTracking().SingleAsync(x => x.Id == ticketId, cancellationToken);
        if (!Active.Contains(snapshot.Status)) throw new InvalidOperationException("Solo se entregan tickets activos.");
        var employee = await db.Employees.AsNoTracking().SingleAsync(x => x.Id == snapshot.EmployeeId, cancellationToken);
        var document = await DocumentAsync(snapshot, cancellationToken);
        var pdf = DocumentRenderer.TicketPdf(document);
        var png = DocumentRenderer.QrPng(document.QrPayload);
        var quantity = DocumentRenderer.Gallons(document.Quantity);
        var expires = BusinessClock.Format(document.ExpiresAt);
        static string H(string value) => WebUtility.HtmlEncode(value);
        var html = $"""
            <p>Hola {H(document.EmployeeName)},</p>
            <p>Se emitió el ticket de combustible <strong>{H(document.Number)}</strong>.</p>
            <ul><li>Vehículo: {H(document.VehiclePlate)} (ficha {H(document.VehicleCode)})</li>
            <li>Combustible: {H(document.FuelType)}</li><li>Cantidad autorizada: {quantity} galones</li>
            <li>Código corto: {H(document.ShortCode)}</li><li>Vence: {expires} (hora de Santo Domingo)</li></ul>
            <p><img src="cid:qr" alt="Código QR del ticket" width="240" height="240"></p>
            <p>Este código lo lee el escáner de la estación; con la cámara del teléfono no abre ninguna página.</p>
            <p>Para ver el ticket en su teléfono: <a href="{H(document.Url)}">{H(document.Url)}</a></p>
            <p>Ticket de un solo uso. Presente el QR y su cédula en la estación.</p>
            """;
        var text = $"Ticket {document.Number}: {quantity} gal de {document.FuelType} para {document.VehiclePlate}. " +
            $"Código {document.ShortCode}. Vence {expires}. QR: {document.Url}";
        var emailReport = await email.SendAsync(new EmailMessage(employee.Email, $"Ticket de combustible {document.Number}", html, text,
            [new EmailAttachment($"{document.Number}.pdf", "application/pdf", pdf), new EmailAttachment("qr.png", "image/png", png, "qr")]),
            cancellationToken);
        var smsReport = await sms.SendAsync(employee.Mobile,
            $"INTEC Combustible: ticket {document.Number}, {quantity} gal. Codigo {document.ShortCode}. Vence {expires}. QR: {document.Url}",
            cancellationToken);
        var now = DateTimeOffset.UtcNow;
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        var ticket = await db.Tickets.FromSqlInterpolated($"SELECT * FROM \"Tickets\" WHERE \"Id\" = {ticketId} FOR UPDATE").SingleAsync(cancellationToken);
        var deliveries = new List<TicketDelivery>
        {
            new() { TicketId = ticketId, Channel = DeliveryChannel.Email, Destination = MaskEmail(employee.Email), Result = emailReport.Result, Detail = DeliveryText.ForPerson(DeliveryChannel.Email, emailReport.Result, emailReport.Detail), AttemptedAt = now, Actor = actor },
            new() { TicketId = ticketId, Channel = DeliveryChannel.Sms, Destination = MaskPhone(employee.Mobile), Result = smsReport.Result, Detail = DeliveryText.ForPerson(DeliveryChannel.Sms, smsReport.Result, smsReport.Detail), AttemptedAt = now, Actor = actor },
        };
        db.TicketDeliveries.AddRange(deliveries);
        var anySent = deliveries.Any(x => x.Result == DeliveryResult.Sent);
        if (ticket.Status is TicketStatus.Created or TicketStatus.Pending)
            ticket.Status = anySent ? TicketStatus.Sent : TicketStatus.Pending;
        ticket.Version = Guid.NewGuid();
        foreach (var failed in deliveries.Where(x => x.Result == DeliveryResult.Failed))
            db.Notifications.Add(new Notification
            {
                Kind = NotificationKind.IntegrationFailure, DedupKey = $"delivery:{Guid.NewGuid():N}", EntityId = ticketId.ToString(), CreatedAt = now,
                Message = $"Falló el envío por {(failed.Channel == DeliveryChannel.Email ? "correo" : "SMS")} del ticket {ticket.Number}.",
            });
        await audit.WriteAsync(actor, ip, "ticket_delivered", "Ticket", ticketId.ToString(), cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return deliveries;
    }

    private static string NewShortCode() =>
        string.Create(8, 0, (span, _) => { for (var i = 0; i < span.Length; i++) span[i] = ShortCodeAlphabet[RandomNumberGenerator.GetInt32(ShortCodeAlphabet.Length)]; });

    public static string MaskEmail(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var at = value.IndexOf('@', StringComparison.Ordinal);
        return at <= 1 ? "***" + value[Math.Max(at, 0)..] : value[0] + new string('*', at - 1) + value[at..];
    }

    public static string MaskPhone(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value.Length <= 4 ? "****" : new string('*', value.Length - 4) + value[^4..];
    }
}
