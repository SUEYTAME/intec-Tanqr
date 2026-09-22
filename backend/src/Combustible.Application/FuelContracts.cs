using System.ComponentModel.DataAnnotations;

namespace Combustible.Application;

public sealed record CodeNameRequest(
    [property: Required, StringLength(30)] string Code,
    [property: Required, StringLength(150)] string Name,
    bool Active = true);

public sealed record TankRequest(
    [property: Required, StringLength(30)] string Code,
    Guid StationId,
    Guid FuelTypeId,
    [property: Range(typeof(decimal), "0.001", "999999999.999")] decimal Capacity,
    [property: Range(typeof(decimal), "0", "999999999.999")] decimal CriticalLevel,
    bool Active = true);

public sealed record SettingsRequest(
    [property: Required, RegularExpression("^[A-Z]{2,10}$")] string Prefix,
    bool ResetAnnually,
    [property: Range(1, 90)] int ValidityDays,
    [property: Range(1, 720)] int WarningHours,
    [property: Range(1, 20)] int MaxActiveTicketsPerVehicle,
    [property: Required] string Version);

public sealed record FuelRequestCreate(
    Guid EmployeeId,
    Guid VehicleId,
    Guid DepartmentId,
    Guid FuelTypeId,
    [property: Range(typeof(decimal), "0.001", "999999.999")] decimal AuthorizedQuantity,
    DateTimeOffset? ExpiresAt,
    [property: StringLength(500)] string? Notes);

public sealed record DecisionRequest(
    [property: Required] string Version,
    [property: StringLength(500)] string? Reason);

public sealed record VoidRequest(
    [property: Required] string Version,
    [property: Required, StringLength(500, MinimumLength = 5)] string Reason);

public sealed record ScheduleRequest(
    [property: Required, StringLength(150)] string Name,
    Guid EmployeeId,
    Guid VehicleId,
    Guid DepartmentId,
    Guid FuelTypeId,
    [property: Required, RegularExpression("^(Fixed|History)$")] string Rule,
    [property: Range(typeof(decimal), "0", "999999.999")] decimal Quantity,
    [property: Range(1, 50)] int HistorySize,
    [property: Required, RegularExpression("^(Once|Daily|Weekly|Monthly)$")] string Frequency,
    DateTimeOffset NextRunAt,
    DateTimeOffset? EndsAt,
    bool AutoApprove,
    bool Active = true);

public sealed record QrRequest([property: Required, StringLength(400)] string Qr);

public sealed record DispatchRequest(
    [property: Required, StringLength(400)] string Qr,
    Guid TankId,
    [property: Range(typeof(decimal), "0.001", "999999.999")] decimal Quantity,
    bool IdentityConfirmed,
    [property: Range(0, long.MaxValue)] long? Odometer,
    [property: StringLength(500)] string? DifferenceReason,
    [property: StringLength(500)] string? Observations);

public sealed record ReceiptRequest(
    [property: Required, RegularExpression("^(Receipt|Purchase)$")] string Kind,
    [property: Required, RegularExpression("^([0-9]{9}|[0-9]{11})$")] string SupplierRnc,
    [property: Required, StringLength(200)] string SupplierName,
    [property: Required, StringLength(50)] string Invoice,
    [property: Range(typeof(decimal), "0.001", "999999999.999")] decimal Quantity,
    DateOnly ReceivedOn,
    Guid TankId);

public sealed record TransferRequest(
    Guid FromTankId,
    Guid ToTankId,
    [property: Range(typeof(decimal), "0.001", "999999999.999")] decimal Quantity,
    [property: Required, StringLength(500, MinimumLength = 5)] string Reason);

public sealed record AdjustmentRequest(
    Guid TankId,
    [property: Required, RegularExpression("^(PositiveAdjustment|NegativeAdjustment|Shrinkage)$")] string Kind,
    [property: Range(typeof(decimal), "0.001", "999999999.999")] decimal Quantity,
    [property: Required, StringLength(500, MinimumLength = 5)] string Reason);

public sealed record TankCount(Guid TankId, decimal Counted);

public sealed record DailyCloseRequest(
    Guid StationId,
    DateOnly Day,
    [property: Required] IReadOnlyList<TankCount> Counts,
    [property: StringLength(1000)] string? Notes);
