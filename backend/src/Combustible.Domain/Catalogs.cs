namespace Combustible.Domain;

public abstract class CatalogEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid Version { get; set; } = Guid.NewGuid();
    public bool Active { get; set; } = true;
}

public sealed class Department : CatalogEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public sealed class Employee : CatalogEntity
{
    public string Code { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string NationalId { get; set; } = string.Empty;
    public Guid DepartmentId { get; set; }
    public string Position { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Mobile { get; set; } = string.Empty;
}

public sealed class Vehicle : CatalogEntity
{
    public string Plate { get; set; } = string.Empty;
    public string InternalCode { get; set; } = string.Empty;
    public string Make { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public int Year { get; set; }
    public string Kind { get; set; } = string.Empty;
    public Guid DepartmentId { get; set; }
    public decimal TankCapacity { get; set; }
    public long Odometer { get; set; }
}

public static class Roles
{
    public const string Administrator = "Administrador";
    public const string Supervisor = "Supervisor";
    public const string Dispatcher = "Despachador";
    public const string Auditor = "Auditor";
    public const string Viewer = "Consulta";
    public static IReadOnlyList<string> All { get; } =
        [Administrator, Supervisor, Dispatcher, Auditor, Viewer];
}
