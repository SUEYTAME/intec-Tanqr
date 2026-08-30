namespace Combustible.Domain;

public sealed class FuelType : CatalogEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public sealed class Station : CatalogEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

// ADR-007 H-01: N tanques por estación. El saldo solo cambia mediante InventoryMovement.
public sealed class Tank : CatalogEntity
{
    public string Code { get; set; } = string.Empty;
    public Guid StationId { get; set; }
    public Guid FuelTypeId { get; set; }
    public decimal Capacity { get; set; }
    public decimal CriticalLevel { get; set; }
    public decimal Balance { get; set; }
}

// Fila única de parámetros (RF-08, H-03). Id fijo para que exista una sola.
public sealed class TicketSettings
{
    public static readonly Guid SingletonId = new("00000000-0000-0000-0000-000000000001");
    public Guid Id { get; set; } = SingletonId;
    public string Prefix { get; set; } = "COM";
    public bool ResetAnnually { get; set; } = true;
    public int ValidityDays { get; set; } = 7;
    public int WarningHours { get; set; } = 24;
    public int MaxActiveTicketsPerVehicle { get; set; } = 1;
    public Guid Version { get; set; } = Guid.NewGuid();
}

public sealed class TicketSequence
{
    public string Scope { get; set; } = string.Empty;
    public long Last { get; set; }
}

public enum RequestOrigin { Manual, Scheduled, Recurring }
public enum RequestStatus { Pending, Approved, Rejected, Cancelled }

public sealed class FuelRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EmployeeId { get; set; }
    public Guid VehicleId { get; set; }
    public Guid DepartmentId { get; set; }
    public Guid FuelTypeId { get; set; }
    public decimal AuthorizedQuantity { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public RequestOrigin Origin { get; set; }
    public Guid? ScheduleId { get; set; }
    public RequestStatus Status { get; set; }
    public string Notes { get; set; } = string.Empty;
    public string RequestedBy { get; set; } = string.Empty;
    public string? DecidedBy { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public string DecisionReason { get; set; } = string.Empty;
    public Guid Version { get; set; } = Guid.NewGuid();
}

// RF-10. Created: emitido; Sent: entregado por un canal real; Pending: entrega pendiente
// o fallida (solo bandeja local o error). NearExpiry/Expired los fija el proceso periódico.
public enum TicketStatus { Created, Sent, Pending, NearExpiry, Expired, Consumed, Voided }

public sealed class Ticket
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Number { get; set; } = string.Empty;
    public string Scope { get; set; } = string.Empty;
    public long Sequence { get; set; }
    public string ShortCode { get; set; } = string.Empty;
    public Guid RequestId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid VehicleId { get; set; }
    public Guid DepartmentId { get; set; }
    public Guid FuelTypeId { get; set; }
    public decimal AuthorizedQuantity { get; set; }
    public DateTimeOffset IssuedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public TicketStatus Status { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public string TokenCipher { get; set; } = string.Empty;
    public string Digest { get; set; } = string.Empty;
    public string Signature { get; set; } = string.Empty;
    public string IssuedBy { get; set; } = string.Empty;
    public DateTimeOffset? ConsumedAt { get; set; }
    public DateTimeOffset? VoidedAt { get; set; }
    public string VoidReason { get; set; } = string.Empty;
    public Guid Version { get; set; } = Guid.NewGuid();
}

public enum DeliveryChannel { Email, Sms }
// Outbox = escrito a la bandeja local de desarrollo; NUNCA equivale a enviado (ADR-005).
public enum DeliveryResult { Sent, Outbox, Failed }

public sealed class TicketDelivery
{
    public long Id { get; set; }
    public Guid TicketId { get; set; }
    public DeliveryChannel Channel { get; set; }
    public string Destination { get; set; } = string.Empty;
    public DeliveryResult Result { get; set; }
    public string Detail { get; set; } = string.Empty;
    public DateTimeOffset AttemptedAt { get; set; }
    public string Actor { get; set; } = string.Empty;
}

public enum ScheduleFrequency { Once, Daily, Weekly, Monthly }
// Fixed: cantidad fija. History: promedio de los últimos despachos del vehículo (RF-11).
public enum QuantityRule { Fixed, History }

public sealed class FuelSchedule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public Guid EmployeeId { get; set; }
    public Guid VehicleId { get; set; }
    public Guid DepartmentId { get; set; }
    public Guid FuelTypeId { get; set; }
    public QuantityRule Rule { get; set; }
    public decimal Quantity { get; set; }
    public int HistorySize { get; set; } = 5;
    public ScheduleFrequency Frequency { get; set; }
    public DateTimeOffset NextRunAt { get; set; }
    public DateTimeOffset? EndsAt { get; set; }
    public bool AutoApprove { get; set; }
    public bool Active { get; set; } = true;
    public string LastError { get; set; } = string.Empty;
    public DateTimeOffset? LastRunAt { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
}

// ADR-007 H-02/H-04/H-05: un despacho por ticket, sin excedente, identidad confirmada.
public sealed class Dispatch
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TicketId { get; set; }
    public Guid TankId { get; set; }
    public Guid StationId { get; set; }
    public decimal Quantity { get; set; }
    public decimal Difference { get; set; }
    public string DifferenceReason { get; set; } = string.Empty;
    public long? Odometer { get; set; }
    public bool IdentityConfirmed { get; set; }
    public Guid OperatorId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string Observations { get; set; } = string.Empty;
}

public enum MovementKind
{
    Receipt, Purchase, TransferIn, TransferOut, Dispatch, Shrinkage, PositiveAdjustment, NegativeAdjustment
}

public sealed class InventoryMovement
{
    public long Id { get; set; }
    public Guid TankId { get; set; }
    public MovementKind Kind { get; set; }
    // Con signo: entradas positivas, salidas negativas. numeric, nunca coma flotante.
    public decimal Quantity { get; set; }
    public decimal BalanceAfter { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string Actor { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public Guid? DispatchId { get; set; }
    public Guid? ReceiptId { get; set; }
    public Guid? TransferId { get; set; }
}

public sealed class FuelReceipt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public MovementKind Kind { get; set; }
    public string SupplierRnc { get; set; } = string.Empty;
    public string SupplierName { get; set; } = string.Empty;
    public string Invoice { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public DateOnly ReceivedOn { get; set; }
    public Guid TankId { get; set; }
    public string Actor { get; set; } = string.Empty;
    public DateTimeOffset RecordedAt { get; set; }
}

public sealed class DailyClose
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid StationId { get; set; }
    public DateOnly Day { get; set; }
    public int DispatchCount { get; set; }
    public decimal DispatchedVolume { get; set; }
    public string Actor { get; set; } = string.Empty;
    public DateTimeOffset ClosedAt { get; set; }
    public string Notes { get; set; } = string.Empty;
    public List<DailyCloseLine> Lines { get; set; } = [];
}

public sealed class DailyCloseLine
{
    public long Id { get; set; }
    public Guid DailyCloseId { get; set; }
    public Guid TankId { get; set; }
    public decimal Opening { get; set; }
    public decimal Inputs { get; set; }
    public decimal Outputs { get; set; }
    public decimal Expected { get; set; }
    public decimal Counted { get; set; }
    public decimal Difference { get; set; }
}

// Las cinco de RF-23 más ScheduleFailure: una programación que no pudo generar su solicitud.
public enum NotificationKind { TicketNearExpiry, TicketExpired, LowInventory, IntegrationFailure, InventoryAdjustment, ScheduleFailure }

public sealed class Notification
{
    public long Id { get; set; }
    public NotificationKind Kind { get; set; }
    public string DedupKey { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

// ADR-007 H-03: se persiste en UTC; el día operativo y la presentación usan Santo Domingo.
public static class BusinessClock
{
    public static TimeZoneInfo Zone { get; } = TimeZoneInfo.FindSystemTimeZoneById("America/Santo_Domingo");

    public static DateTimeOffset ToLocal(DateTimeOffset instant) => TimeZoneInfo.ConvertTime(instant, Zone);

    public static DateOnly LocalDay(DateTimeOffset instant) => DateOnly.FromDateTime(ToLocal(instant).DateTime);

    public static (DateTimeOffset Start, DateTimeOffset End) DayBounds(DateOnly day)
    {
        var start = day.ToDateTime(TimeOnly.MinValue);
        var offset = Zone.GetUtcOffset(start);
        var local = new DateTimeOffset(start, offset);
        return (local.ToUniversalTime(), local.AddDays(1).ToUniversalTime());
    }

    public static string Format(DateTimeOffset instant) =>
        ToLocal(instant).ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture);
}

public sealed class NotificationRead
{
    public long NotificationId { get; set; }
    public Guid UserId { get; set; }
    public DateTimeOffset ReadAt { get; set; }
}
