using Combustible.Application;
using Combustible.Domain;
using Combustible.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Combustible.Api.Endpoints;

public static class CatalogEndpoints
{
    public static void MapCatalogs(this WebApplication app)
    {
        Map<Department, DepartmentRequest>(app, "departamentos", (e, r) =>
        {
            e.Code = r.Code.Trim().ToUpperInvariant(); e.Name = r.Name.Trim(); e.Active = r.Active;
        });
        Map<Employee, EmployeeRequest>(app, "empleados", (e, r) =>
        {
            e.Code = r.Code.Trim().ToUpperInvariant(); e.FullName = r.FullName.Trim(); e.NationalId = r.NationalId;
            e.DepartmentId = r.DepartmentId; e.Position = r.Position.Trim(); e.Email = r.Email.Trim();
            e.Mobile = r.Mobile.Trim(); e.Active = r.Active;
        });
        Map<Vehicle, VehicleRequest>(app, "vehiculos", (e, r) =>
        {
            e.Plate = r.Plate.Trim().ToUpperInvariant(); e.InternalCode = r.InternalCode.Trim().ToUpperInvariant();
            e.Make = r.Make.Trim(); e.Model = r.Model.Trim(); e.Year = r.Year; e.Kind = r.Kind.Trim();
            e.DepartmentId = r.DepartmentId; e.TankCapacity = r.TankCapacity; e.Odometer = r.Odometer; e.Active = r.Active;
        });
    }

    private static void Map<TEntity, TRequest>(WebApplication app, string route, Action<TEntity, TRequest> apply)
        where TEntity : CatalogEntity, new() where TRequest : class
    {
        var group = app.MapGroup($"/api/{route}").RequireAuthorization().AddEndpointFilter<RequestValidationFilter>();
        group.MapGet("/", async (AppDbContext db, int page = 1, int pageSize = 50) =>
        {
            if (page < 1 || page > 100000 || pageSize is < 1 or > 100) return Results.BadRequest();
            var query = db.Set<TEntity>().AsNoTracking();
            return Results.Ok(new { items = await query.OrderBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(), total = await query.CountAsync() });
        });
        group.MapGet("/{id:guid}", async (Guid id, AppDbContext db, HttpContext http) =>
        {
            var entity = await db.Set<TEntity>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
            if (entity is null) return Results.NotFound();
            http.Response.Headers.ETag = $"\"{entity.Version}\"";
            return Results.Ok(entity);
        });
        Func<TRequest, AppDbContext, AuditWriter, HttpContext, Task<IResult>> create = async (request, db, audit, http) =>
        {
            var entity = new TEntity(); apply(entity, request);
            if (!await ValidDepartmentAsync(db, entity)) return Results.UnprocessableEntity(new { error = "Departamento inactivo o precisión inválida." });
            await using var tx = await db.Database.BeginTransactionAsync();
            db.Add(entity);
            await audit.WriteAsync(http.Actor(), http.Ip(), "create", route, entity.Id.ToString());
            await tx.CommitAsync();
            return Results.Created($"/api/{route}/{entity.Id}", entity);
        };
        group.MapPost("/", create).RequireAuthorization("catalog-write");
        Func<Guid, TRequest, AppDbContext, AuditWriter, HttpContext, Task<IResult>> update = async (id, request, db, audit, http) =>
        {
            if (!Guid.TryParse(http.Request.Headers.IfMatch.ToString().Trim('"'), out var version))
                return Results.Problem(statusCode: 428, title: "Se requiere If-Match con la versión del registro.");
            await using var tx = await db.Database.BeginTransactionAsync();
            var entity = await db.Set<TEntity>().SingleOrDefaultAsync(x => x.Id == id);
            if (entity is null) return Results.NotFound();
            if (entity.Version != version) return Results.StatusCode(412);
            var oldOdometer = (entity as Vehicle)?.Odometer;
            apply(entity, request);
            if (entity is Vehicle vehicle && vehicle.Odometer < oldOdometer)
                return Results.UnprocessableEntity(new { error = "Odómetro no puede disminuir." });
            if (!await ValidDepartmentAsync(db, entity)) return Results.UnprocessableEntity(new { error = "Departamento inactivo o precisión inválida." });
            entity.Version = Guid.NewGuid();
            await audit.WriteAsync(http.Actor(), http.Ip(), "update", route, id.ToString());
            await tx.CommitAsync();
            return Results.Ok(entity);
        };
        group.MapPut("/{id:guid}", update).RequireAuthorization("catalog-write");
    }

    private static async Task<bool> ValidDepartmentAsync(AppDbContext db, CatalogEntity entity)
    {
        Guid? department = entity switch { Employee e => e.DepartmentId, Vehicle v => v.DepartmentId, _ => null };
        if (entity is Vehicle vehicle && decimal.Round(vehicle.TankCapacity, 3) != vehicle.TankCapacity) return false;
        return department is null || await db.Departments.AnyAsync(x => x.Id == department && (x.Active || !entity.Active));
    }
}
