using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Combustible.Domain;

namespace Combustible.UnitTests;

[Collection("api")]
public sealed class CatalogTests(ApiFixture fixture)
{
    [Fact]
    public async Task Empleados_y_vehiculos_requieren_departamento_y_persisten_campos()
    {
        var (client, _) = await fixture.LoginAsync();
        using (client)
        {
            var response = await client.PostAsJsonAsync("/api/departamentos/", new { code = Guid.NewGuid().ToString("N")[..12], name = "Prueba relaciones" });
            var department = (await response.Content.ReadFromJsonAsync<Department>())!;
            var employee = new { code = "TEST-001", fullName = "Persona de prueba", nationalId = "00000000001", departmentId = department.Id, position = "Prueba", email = "persona@example.test", mobile = "+18095550000", active = true };
            var createdEmployee = await client.PostAsJsonAsync("/api/empleados/", employee);
            Assert.Equal(HttpStatusCode.Created, createdEmployee.StatusCode);
            var savedEmployee = (await createdEmployee.Content.ReadFromJsonAsync<Employee>())!;
            Assert.Equal(employee.email, savedEmployee.Email);
            Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/empleados/", employee)).StatusCode);
            var vehicle = new { plate = "TEST-001", internalCode = "TEST-001", make = "Prueba", model = "Prueba", year = 2026, kind = "Camioneta", departmentId = department.Id, tankCapacity = 20.125m, odometer = 100L, active = true };
            var createdVehicle = await client.PostAsJsonAsync("/api/vehiculos/", vehicle);
            Assert.Equal(HttpStatusCode.Created, createdVehicle.StatusCode);
            var savedVehicle = (await createdVehicle.Content.ReadFromJsonAsync<Vehicle>())!;
            Assert.Equal(20.125m, savedVehicle.TankCapacity);
            var update = new HttpRequestMessage(HttpMethod.Put, $"/api/vehiculos/{savedVehicle.Id}")
            { Content = JsonContent.Create(vehicle with { odometer = 99 }) };
            update.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{savedVehicle.Version}\""));
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.SendAsync(update)).StatusCode);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PostAsJsonAsync("/api/vehiculos/", vehicle with { plate = "X", internalCode = "X", departmentId = Guid.NewGuid() })).StatusCode);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PostAsJsonAsync("/api/vehiculos/", vehicle with { plate = "X", internalCode = "X", tankCapacity = 20.1234m })).StatusCode);
        }
    }
}
