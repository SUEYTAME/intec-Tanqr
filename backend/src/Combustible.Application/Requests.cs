using System.ComponentModel.DataAnnotations;

namespace Combustible.Application;

public sealed record DepartmentRequest(
    [property: Required, StringLength(30)] string Code,
    [property: Required, StringLength(150)] string Name,
    bool Active = true);

public sealed record EmployeeRequest(
    [property: Required, StringLength(30)] string Code,
    [property: Required, StringLength(200)] string FullName,
    [property: Required, RegularExpression(@"^[0-9]{11}$")] string NationalId,
    Guid DepartmentId,
    [property: Required, StringLength(100)] string Position,
    [property: Required, EmailAddress, StringLength(254)] string Email,
    [property: Required, Phone, StringLength(25)] string Mobile,
    bool Active = true);

public sealed record VehicleRequest(
    [property: Required, StringLength(20)] string Plate,
    [property: Required, StringLength(30)] string InternalCode,
    [property: Required, StringLength(80)] string Make,
    [property: Required, StringLength(80)] string Model,
    [property: Range(1900, 2200)] int Year,
    [property: Required, StringLength(80)] string Kind,
    Guid DepartmentId,
    [property: Range(typeof(decimal), "0.001", "999999.999")] decimal TankCapacity,
    [property: Range(0, long.MaxValue)] long Odometer,
    bool Active = true);

public sealed record LoginRequest(
    [property: Required, StringLength(254)] string Email,
    [property: Required, StringLength(128)] string Password,
    string? Code = null,
    [property: StringLength(30)] string? RecoveryCode = null);
public sealed record RefreshRequest([property: Required, StringLength(256)] string RefreshToken);
public sealed record UserRequest(
    [property: Required, EmailAddress, StringLength(254)] string Email,
    [property: Required, StringLength(150)] string DisplayName,
    [property: Required] string Role,
    [property: Required, StringLength(128, MinimumLength = 15)] string Password);
public sealed record UserAccessRequest([property: Required] string Role, bool Active);
public sealed record UserUpdateRequest(
    [property: Required, EmailAddress, StringLength(254)] string Email,
    [property: Required, StringLength(150)] string DisplayName,
    [property: Required] string Role, bool Active,
    [property: Required] string Version);
public sealed record PasswordRequest([property: Required, StringLength(128, MinimumLength = 15)] string Password);
public sealed record MfaRequest([property: Required, StringLength(128)] string Password, string? Code = null);
