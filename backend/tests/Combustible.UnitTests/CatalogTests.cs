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

    // Cédula, correo y móvil solo para Administrador y Supervisor (ADR-016); los demás ven al empleado sin esos campos.
    [Theory]
    [InlineData(Roles.Supervisor, true)]
    [InlineData(Roles.Dispatcher, false)]
    [InlineData(Roles.Auditor, false)]
    [InlineData(Roles.Viewer, false)]
    public async Task Datos_personales_del_empleado_solo_para_administrador_y_supervisor(string role, bool seesPersonalData)
    {
        var (admin, _) = await fixture.LoginAsync();
        var (client, _) = await fixture.LoginAsRoleAsync(role);
        using (admin)
        using (client)
        {
            var department = (await (await admin.PostAsJsonAsync("/api/departamentos/", new { code = Guid.NewGuid().ToString("N")[..12], name = "Prueba privacidad" }))
                .Content.ReadFromJsonAsync<Department>())!;
            var created = await admin.PostAsJsonAsync("/api/empleados/", new
            {
                code = "PII-" + Digits(8), fullName = "Persona privada", nationalId = Digits(11), departmentId = department.Id, position = "Prueba",
                email = "privada@example.test", mobile = "+18095550000", active = true,
            });
            var id = (await created.Content.ReadFromJsonAsync<Employee>())!.Id;
            var detail = await client.GetFromJsonAsync<System.Text.Json.JsonElement>($"/api/empleados/{id}");
            Assert.Equal("Persona privada", detail.GetProperty("fullName").GetString());
            foreach (var field in new[] { "nationalId", "email", "mobile" })
                Assert.Equal(seesPersonalData, detail.TryGetProperty(field, out _));
            var list = await client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/empleados/?pageSize=100");
            Assert.All(list.GetProperty("items").EnumerateArray(), e => Assert.Equal(seesPersonalData, e.TryGetProperty("email", out _)));
        }
    }

    private static string Digits(int length) => string.Concat(Enumerable.Range(0, length).Select(_ => Random.Shared.Next(10)));

    private static Task<HttpResponseMessage> PutAsync(HttpClient client, string path, Guid version, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, path) { Content = JsonContent.Create(body) };
        request.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{version}\""));
        return client.SendAsync(request);
    }

    // RF-02/RF-03: editar recifra los datos personales y recalcula el índice ciego de la cédula (RS-03).
    [Fact]
    public async Task Edicion_de_empleado_y_vehiculo_recifra_y_conserva_unicidad()
    {
        var (client, _) = await fixture.LoginAsync();
        using (client)
        {
            var department = (await (await client.PostAsJsonAsync("/api/departamentos/", new { code = Guid.NewGuid().ToString("N")[..12], name = "Prueba edición" }))
                .Content.ReadFromJsonAsync<Department>())!;
            object EmployeeBody(string code, string nationalId, string email) => new
            {
                code, fullName = "Persona de prueba", nationalId, departmentId = department.Id, position = "Prueba", email, mobile = "+18095550000", active = true,
            };
            var (firstId, secondId, newId) = (Digits(11), Digits(11), Digits(11));
            var first = (await (await client.PostAsJsonAsync("/api/empleados/", EmployeeBody("ED-" + Digits(8), firstId, "a@example.test"))).Content.ReadFromJsonAsync<Employee>())!;
            var second = (await (await client.PostAsJsonAsync("/api/empleados/", EmployeeBody("ED-" + Digits(8), secondId, "b@example.test"))).Content.ReadFromJsonAsync<Employee>())!;

            var edited = await PutAsync(client, $"/api/empleados/{first.Id}", first.Version, EmployeeBody(first.Code, newId, "nuevo@example.test"));
            Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
            var reloaded = (await client.GetFromJsonAsync<Employee>($"/api/empleados/{first.Id}"))!;
            Assert.Equal(("nuevo@example.test", newId), (reloaded.Email, reloaded.NationalId));
            // El índice ciego sigue a la cédula editada: la nueva queda ocupada y la anterior libre.
            Assert.Equal(HttpStatusCode.Conflict, (await PutAsync(client, $"/api/empleados/{second.Id}", second.Version, EmployeeBody(second.Code, newId, "b@example.test"))).StatusCode);
            Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/empleados/", EmployeeBody("ED-" + Digits(8), firstId, "c@example.test"))).StatusCode);

            var vehicle = new { plate = "E" + Digits(6), internalCode = "E" + Digits(6), make = "Prueba", model = "Prueba", year = 2026, kind = "Camioneta", departmentId = department.Id, tankCapacity = 20m, odometer = 100L, active = true };
            var saved = (await (await client.PostAsJsonAsync("/api/vehiculos/", vehicle)).Content.ReadFromJsonAsync<Vehicle>())!;
            var updated = await PutAsync(client, $"/api/vehiculos/{saved.Id}", saved.Version, vehicle with { make = "Corregida", odometer = 150L, active = false });
            Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
            var vehicleAfter = (await updated.Content.ReadFromJsonAsync<Vehicle>())!;
            Assert.Equal(("Corregida", 150L, false), (vehicleAfter.Make, vehicleAfter.Odometer, vehicleAfter.Active));
            Assert.Equal(HttpStatusCode.PreconditionFailed, (await PutAsync(client, $"/api/vehiculos/{saved.Id}", saved.Version, vehicle)).StatusCode);
        }
    }
}
