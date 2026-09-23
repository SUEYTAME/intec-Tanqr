using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Combustible.Api.Tickets;
using Combustible.Domain;
using Combustible.Infrastructure.Data;
using Combustible.Infrastructure.Security;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Combustible.UnitTests;

// Fase 2-5: tickets, QR, inventario, despacho, cierre, reportes y alertas contra PostgreSQL real
// y un servidor SMTP real de desarrollo (Mailpit). Cada prueba crea sus propios catálogos ficticios.
[Collection("api")]
public sealed class FuelTests(ApiFixture fixture)
{
    private sealed record World(Guid Department, Guid Employee, Guid Vehicle, Guid Fuel, Guid OtherFuel, Guid Station,
        Guid Tank, Guid OtherTank, Guid SameFuelTank);

    private sealed record Issued(Guid TicketId, string Number, JsonElement Deliveries);

    private static string Code() => Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();

    private static string NationalId() => string.Concat(Enumerable.Range(0, 11).Select(_ => RandomNumberGenerator.GetInt32(10).ToString(CultureInfo.InvariantCulture)));

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>())!;

    private static async Task<Guid> PostIdAsync(HttpClient client, string path, object body)
    {
        var response = await client.PostAsJsonAsync(path, body);
        Assert.True(response.StatusCode == HttpStatusCode.Created, $"{path}: {response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return (await JsonAsync(response)).GetProperty("id").GetGuid();
    }

    private static Task<Guid> AddVehicleAsync(HttpClient admin, Guid department, decimal capacity = 40) =>
        PostIdAsync(admin, "/api/vehiculos/", new
        {
            plate = "P" + Code()[..7], internalCode = Code(), make = "Marca", model = "Modelo", year = 2020, kind = "Camioneta",
            departmentId = department, tankCapacity = capacity, odometer = 1000,
        });

    private static async Task<World> SetupAsync(HttpClient admin)
    {
        var department = await PostIdAsync(admin, "/api/departamentos/", new { code = Code(), name = "QA despacho" });
        var employee = await PostIdAsync(admin, "/api/empleados/", new
        {
            code = Code(), fullName = "Empleado Prueba", nationalId = NationalId(), departmentId = department, position = "Chofer",
            email = $"{Code().ToLowerInvariant()}@example.test", mobile = "8095550101",
        });
        var vehicle = await AddVehicleAsync(admin, department);
        var fuel = await PostIdAsync(admin, "/api/combustibles/", new { code = Code(), name = "Diésel QA" });
        var other = await PostIdAsync(admin, "/api/combustibles/", new { code = Code(), name = "Gasolina QA" });
        var station = await PostIdAsync(admin, "/api/estaciones/", new { code = Code(), name = "Estación QA" });
        var tank = await PostIdAsync(admin, "/api/tanques/", new { code = Code(), stationId = station, fuelTypeId = fuel, capacity = 1000, criticalLevel = 50 });
        var otherTank = await PostIdAsync(admin, "/api/tanques/", new { code = Code(), stationId = station, fuelTypeId = other, capacity = 1000, criticalLevel = 50 });
        var sameFuelTank = await PostIdAsync(admin, "/api/tanques/", new { code = Code(), stationId = station, fuelTypeId = fuel, capacity = 1000, criticalLevel = 50 });
        return new World(department, employee, vehicle, fuel, other, station, tank, otherTank, sameFuelTank);
    }

    private static async Task<(Guid Id, string Version)> RequestAsync(HttpClient client, World world, decimal quantity, Guid? vehicle = null,
        DateTimeOffset? expiresAt = null)
    {
        var response = await client.PostAsJsonAsync("/api/solicitudes/", new
        {
            employeeId = world.Employee, vehicleId = vehicle ?? world.Vehicle, departmentId = world.Department, fuelTypeId = world.Fuel,
            authorizedQuantity = quantity, expiresAt, notes = "Prueba",
        });
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var body = await JsonAsync(response);
        return (body.GetProperty("id").GetGuid(), body.GetProperty("version").GetString()!);
    }

    private static async Task<Issued> IssueAsync(HttpClient client, World world, decimal quantity, Guid? vehicle = null, DateTimeOffset? expiresAt = null)
    {
        var (id, version) = await RequestAsync(client, world, quantity, vehicle, expiresAt);
        var response = await client.PostAsJsonAsync($"/api/solicitudes/{id}/aprobar", new { version });
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = await JsonAsync(response);
        return new Issued(body.GetProperty("ticketId").GetGuid(), body.GetProperty("number").GetString()!, body.GetProperty("deliveries"));
    }

    private (string Token, string Payload) Qr(Guid ticketId)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ticket = db.Tickets.AsNoTracking().Single(x => x.Id == ticketId);
        return scope.ServiceProvider.GetRequiredService<TicketService>().Payload(ticket);
    }

    private async Task<T> DbAsync<T>(Func<AppDbContext, Task<T>> query)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private static Task<HttpResponseMessage> ReceiveAsync(HttpClient client, Guid tank, decimal quantity, string? invoice = null, string kind = "Receipt") =>
        client.PostAsJsonAsync("/api/inventario/recepciones", new
        {
            kind, supplierRnc = "101234567", supplierName = "Suplidor QA", invoice = invoice ?? "F" + Code(), quantity,
            receivedOn = BusinessClock.LocalDay(DateTimeOffset.UtcNow).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), tankId = tank,
        });

    private static Task<HttpResponseMessage> DispatchAsync(HttpClient client, string qr, Guid tank, decimal quantity, bool identity = true,
        string? reason = null, long? odometer = null) =>
        client.PostAsJsonAsync("/api/despachos/", new { qr, tankId = tank, quantity, identityConfirmed = identity, differenceReason = reason, odometer, observations = "QA" });

    [Fact]
    public async Task Emision_numera_firma_y_envia_por_SMTP_real()
    {
        var (admin, _) = await fixture.LoginAsync();
        using (admin)
        {
            var world = await SetupAsync(admin);
            var issued = await IssueAsync(admin, world, 12.5m);
            Assert.Matches(@"^COM-\d{4}-\d{6}$", issued.Number);
            var channels = issued.Deliveries.EnumerateArray().ToDictionary(x => x.GetProperty("channel").GetString()!, x => x.GetProperty("result").GetString()!);
            Assert.Equal("Sent", channels["Email"]);
            // B-01: sin pasarela SMS. El SMS queda en la bandeja local y se informa como tal, nunca como enviado.
            Assert.Equal("Outbox", channels["Sms"]);
            var ticket = await JsonAsync(await admin.GetAsync($"/api/tickets/{issued.TicketId}"));
            Assert.Equal("Sent", ticket.GetProperty("ticket").GetProperty("status").GetString());
            Assert.Equal(12.5m, ticket.GetProperty("ticket").GetProperty("authorizedQuantity").GetDecimal());

            using var mail = new HttpClient { BaseAddress = fixture.MailpitApi };
            var messages = await mail.GetFromJsonAsync<JsonElement>("messages?limit=500");
            var message = messages.GetProperty("messages").EnumerateArray().Single(x => x.GetProperty("Subject").GetString() == $"Ticket de combustible {issued.Number}");
            Assert.True(message.GetProperty("Attachments").GetInt32() >= 1);
            var sms = Directory.GetFiles(fixture.OutboxDirectory, "*.sms.txt").Select(File.ReadAllText).Where(x => x.Contains(issued.Number, StringComparison.Ordinal));
            // "12.500 gal" se leería como doce mil quinientos: el texto para personas no rellena ceros.
            Assert.Contains("12.5 gal", Assert.Single(sms), StringComparison.Ordinal);
            var pdf = await admin.GetAsync($"/api/tickets/{issued.TicketId}/pdf");
            Assert.Equal("application/pdf", pdf.Content.Headers.ContentType!.MediaType);
            Assert.StartsWith("%PDF", Encoding.ASCII.GetString((await pdf.Content.ReadAsByteArrayAsync())[..4]), StringComparison.Ordinal);
            var png = await (await admin.GetAsync($"/api/tickets/{issued.TicketId}/qr.png")).Content.ReadAsByteArrayAsync();
            Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, png[..4]);
            var (dispatcher, _) = await fixture.LoginAsRoleAsync(Roles.Dispatcher);
            using (dispatcher)
                Assert.Equal(HttpStatusCode.Forbidden, (await dispatcher.GetAsync($"/api/tickets/{issued.TicketId}/qr.png")).StatusCode);
        }
    }

    [Fact]
    public async Task Sin_SMTP_la_entrega_queda_pendiente_y_un_fallo_se_notifica()
    {
        var outbox = new Dictionary<string, string?>(fixture.Settings) { ["SMTP_HOST"] = "" };
        var broken = new Dictionary<string, string?>(fixture.Settings) { ["SMTP_HOST"] = "127.0.0.1", ["SMTP_PORT"] = "9" };
        foreach (var (settings, expected) in new[] { (outbox, "Outbox"), (broken, "Failed") })
        {
            await using var factory = ApiFixture.CreateFactory(settings);
            using var client = factory.CreateClient();
            var login = await client.PostAsJsonAsync("/api/auth/login", new { email = "admin@localhost.test", password = fixture.Password });
            var session = await JsonAsync(login);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.GetProperty("accessToken").GetString());
            var world = await SetupAsync(client);
            var issued = await IssueAsync(client, world, 5);
            var email = issued.Deliveries.EnumerateArray().Single(x => x.GetProperty("channel").GetString() == "Email");
            Assert.Equal(expected, email.GetProperty("result").GetString());
            var ticket = await JsonAsync(await client.GetAsync($"/api/tickets/{issued.TicketId}"));
            // ADR-005: sin un canal real que confirme la entrega, el ticket no pasa a Enviado.
            Assert.Equal("Pending", ticket.GetProperty("ticket").GetProperty("status").GetString());
            var alerts = await DbAsync(db => db.Notifications.CountAsync(x => x.Kind == NotificationKind.IntegrationFailure && x.EntityId == issued.TicketId.ToString()));
            Assert.Equal(expected == "Failed" ? 1 : 0, alerts);
        }
    }

    [Fact]
    public async Task QR_alterado_o_ticket_modificado_en_la_base_se_rechaza()
    {
        var (admin, _) = await fixture.LoginAsync();
        var (dispatcher, _) = await fixture.LoginAsRoleAsync(Roles.Dispatcher);
        using (admin)
        using (dispatcher)
        {
            var world = await SetupAsync(admin);
            var issued = await IssueAsync(admin, world, 10);
            var (token, payload) = Qr(issued.TicketId);
            var valid = await JsonAsync(await dispatcher.PostAsJsonAsync("/api/despachos/validar", new { qr = payload }));
            Assert.True(valid.GetProperty("valid").GetBoolean());
            Assert.Equal(issued.Number, valid.GetProperty("ticket").GetProperty("number").GetString());
            var flipped = payload[..^1] + (payload[^1] == 'A' ? 'B' : 'A');
            var otherToken = payload.Replace(token, TicketSigner.NewToken(), StringComparison.Ordinal);
            foreach (var tampered in new[] { flipped, otherToken, "IC1.garbage", issued.Number })
                Assert.Equal(HttpStatusCode.UnprocessableEntity, (await dispatcher.PostAsJsonAsync("/api/despachos/validar", new { qr = tampered })).StatusCode);

            // El usuario SQL de la aplicación no puede alterar la cantidad firmada.
            await using (var app = new NpgsqlConnection(fixture.ApplicationConnection))
            {
                await app.OpenAsync();
                await using var command = new NpgsqlCommand($"UPDATE \"Tickets\" SET \"AuthorizedQuantity\" = 500 WHERE \"Id\" = '{issued.TicketId}'", app);
                Assert.Equal("42501", (await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync())).SqlState);
            }
            // Aunque un superusuario la cambie, la firma deja de verificar (RF-07: no editable).
            await using (var owner = new NpgsqlConnection(fixture.OwnerConnection))
            {
                await owner.OpenAsync();
                await using var command = new NpgsqlCommand($"UPDATE \"Tickets\" SET \"AuthorizedQuantity\" = 500 WHERE \"Id\" = '{issued.TicketId}'", owner);
                Assert.Equal(1, await command.ExecuteNonQueryAsync());
            }
            var afterTamper = await dispatcher.PostAsJsonAsync("/api/despachos/validar", new { qr = payload });
            Assert.Equal(HttpStatusCode.UnprocessableEntity, afterTamper.StatusCode);
            Assert.Contains("firma", (await JsonAsync(afterTamper)).GetProperty("error").GetString()!, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Despacho_atomico_descuenta_inventario_y_consume_el_ticket()
    {
        var (admin, _) = await fixture.LoginAsync();
        var (dispatcher, _) = await fixture.LoginAsRoleAsync(Roles.Dispatcher);
        using (admin)
        using (dispatcher)
        {
            var world = await SetupAsync(admin);
            Assert.Equal(HttpStatusCode.Created, (await ReceiveAsync(admin, world.Tank, 500)).StatusCode);
            var issued = await IssueAsync(admin, world, 12);
            var qr = Qr(issued.TicketId).Payload;
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await DispatchAsync(dispatcher, qr, world.Tank, 10, identity: false, reason: "x")).StatusCode);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await DispatchAsync(dispatcher, qr, world.Tank, 12.001m)).StatusCode);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await DispatchAsync(dispatcher, qr, world.Tank, 10)).StatusCode);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await DispatchAsync(dispatcher, qr, world.OtherTank, 10, reason: "Tanque lleno")).StatusCode);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await DispatchAsync(dispatcher, qr, world.Tank, 10, reason: "Tanque lleno", odometer: 10)).StatusCode);
            var response = await DispatchAsync(dispatcher, qr, world.Tank, 10, reason: "Tanque del vehículo lleno", odometer: 1500);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var body = await JsonAsync(response);
            Assert.Equal(490m, body.GetProperty("tankBalance").GetDecimal());
            Assert.Equal(2m, body.GetProperty("difference").GetDecimal());

            // CA-3: el saldo, el movimiento, el despacho y el estado del ticket se confirmaron juntos.
            var dispatchId = body.GetProperty("id").GetGuid();
            var (tank, movement, ticket, vehicle) = await DbAsync(async db => (
                await db.Tanks.AsNoTracking().SingleAsync(x => x.Id == world.Tank),
                await db.InventoryMovements.AsNoTracking().SingleAsync(x => x.DispatchId == dispatchId),
                await db.Tickets.AsNoTracking().SingleAsync(x => x.Id == issued.TicketId),
                await db.Vehicles.AsNoTracking().SingleAsync(x => x.Id == world.Vehicle)));
            Assert.Equal(490m, tank.Balance);
            Assert.Equal(-10m, movement.Quantity);
            Assert.Equal(490m, movement.BalanceAfter);
            Assert.Equal(TicketStatus.Consumed, ticket.Status);
            Assert.Equal(1500, vehicle.Odometer);

            var reuse = await DispatchAsync(dispatcher, qr, world.Tank, 1, reason: "Reintento");
            Assert.Equal(HttpStatusCode.UnprocessableEntity, reuse.StatusCode);
            Assert.Contains("consumido", (await JsonAsync(reuse)).GetProperty("error").GetString()!, StringComparison.Ordinal);
            var list = await JsonAsync(await admin.GetAsync($"/api/despachos/?stationId={world.Station}"));
            Assert.Equal(issued.Number, list.GetProperty("items")[0].GetProperty("ticketNumber").GetString());
            Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/auditoria/verificar")).StatusCode);
        }
    }

    [Fact]
    public async Task Despachos_concurrentes_no_sobregiran_el_tanque()
    {
        var (admin, _) = await fixture.LoginAsync();
        var (dispatcher, _) = await fixture.LoginAsRoleAsync(Roles.Dispatcher);
        using (admin)
        using (dispatcher)
        {
            var world = await SetupAsync(admin);
            Assert.Equal(HttpStatusCode.Created, (await ReceiveAsync(admin, world.Tank, 25)).StatusCode);
            var tickets = new List<string>();
            for (var i = 0; i < 3; i++)
                tickets.Add(Qr((await IssueAsync(admin, world, 10, await AddVehicleAsync(admin, world.Department))).TicketId).Payload);
            var results = await Task.WhenAll(tickets.Select(qr => DispatchAsync(dispatcher, qr, world.Tank, 10)));
            Assert.Equal(2, results.Count(x => x.StatusCode == HttpStatusCode.Created));
            Assert.Equal(1, results.Count(x => x.StatusCode == HttpStatusCode.UnprocessableEntity));
            var (balance, sum) = await DbAsync(async db => (
                (await db.Tanks.AsNoTracking().SingleAsync(x => x.Id == world.Tank)).Balance,
                await db.InventoryMovements.Where(x => x.TankId == world.Tank).SumAsync(x => x.Quantity)));
            Assert.Equal(5m, balance);
            Assert.Equal(balance, sum);
        }
    }

    [Fact]
    public async Task Emision_concurrente_no_duplica_ni_salta_numeros()
    {
        var (admin, _) = await fixture.LoginAsync();
        using (admin)
        {
            var world = await SetupAsync(admin);
            var requests = new List<(Guid Id, string Version)>();
            for (var i = 0; i < 20; i++) requests.Add(await RequestAsync(admin, world, 5, await AddVehicleAsync(admin, world.Department)));
            var responses = await Task.WhenAll(requests.Select(r => admin.PostAsJsonAsync($"/api/solicitudes/{r.Id}/aprobar", new { version = r.Version })));
            Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
            var numbers = new List<string>();
            foreach (var response in responses) numbers.Add((await JsonAsync(response)).GetProperty("number").GetString()!);
            Assert.Equal(20, numbers.Distinct().Count());
            var sequences = numbers.Select(n => long.Parse(n[^6..], CultureInfo.InvariantCulture)).Order().ToList();
            // CA-1: únicos y consecutivos, sin huecos.
            Assert.Equal(19, sequences[^1] - sequences[0]);
        }
    }

    [Fact]
    public async Task Proceso_periodico_marca_proximo_a_vencer_y_vencido()
    {
        var (admin, _) = await fixture.LoginAsync();
        var (dispatcher, _) = await fixture.LoginAsRoleAsync(Roles.Dispatcher);
        using (admin)
        using (dispatcher)
        {
            var world = await SetupAsync(admin);
            var now = DateTimeOffset.UtcNow;
            var issued = await IssueAsync(admin, world, 8, expiresAt: now.AddHours(2));
            var lifecycle = fixture.Factory.Services.GetRequiredService<LifecycleService>();
            await lifecycle.RunOnceAsync(now);
            Assert.Equal(TicketStatus.NearExpiry, await DbAsync(db => db.Tickets.Where(x => x.Id == issued.TicketId).Select(x => x.Status).SingleAsync()));
            Assert.True(await DbAsync(db => db.Notifications.AnyAsync(x => x.Kind == NotificationKind.TicketNearExpiry && x.EntityId == issued.TicketId.ToString())));
            await lifecycle.RunOnceAsync(now.AddHours(3));
            Assert.Equal(TicketStatus.Expired, await DbAsync(db => db.Tickets.Where(x => x.Id == issued.TicketId).Select(x => x.Status).SingleAsync()));
            Assert.True(await DbAsync(db => db.Notifications.AnyAsync(x => x.Kind == NotificationKind.TicketExpired && x.EntityId == issued.TicketId.ToString())));
            var qr = Qr(issued.TicketId).Payload;
            var validation = await JsonAsync(await dispatcher.PostAsJsonAsync("/api/despachos/validar", new { qr }));
            Assert.False(validation.GetProperty("valid").GetBoolean());
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await DispatchAsync(dispatcher, qr, world.Tank, 8)).StatusCode);
        }
    }

    [Fact]
    public async Task Programaciones_generan_solicitudes_y_asignan_automaticamente()
    {
        var (admin, _) = await fixture.LoginAsync();
        using (admin)
        {
            var world = await SetupAsync(admin);
            var now = DateTimeOffset.UtcNow;
            var due = TicketService.Truncate(now.AddMinutes(-1));
            object Schedule(string rule, string frequency, bool autoApprove, Guid vehicle) => new
            {
                name = "QA " + Code(), employeeId = world.Employee, vehicleId = vehicle, departmentId = world.Department, fuelTypeId = world.Fuel,
                rule, quantity = rule == "Fixed" ? 8 : 0, historySize = 5, frequency, nextRunAt = due, endsAt = (DateTimeOffset?)null, autoApprove,
            };
            var recurring = await PostIdAsync(admin, "/api/programaciones/", Schedule("Fixed", "Daily", true, world.Vehicle));
            var history = await PostIdAsync(admin, "/api/programaciones/", Schedule("History", "Weekly", true, await AddVehicleAsync(admin, world.Department)));
            var once = await PostIdAsync(admin, "/api/programaciones/", Schedule("Fixed", "Once", false, await AddVehicleAsync(admin, world.Department)));
            await fixture.Factory.Services.GetRequiredService<LifecycleService>().RunOnceAsync(now);

            var (schedules, requests) = await DbAsync(async db => (
                await db.FuelSchedules.AsNoTracking().Where(x => x.Id == recurring || x.Id == history || x.Id == once).ToDictionaryAsync(x => x.Id),
                await db.FuelRequests.AsNoTracking().Where(x => x.ScheduleId == recurring || x.ScheduleId == history || x.ScheduleId == once).ToListAsync()));
            Assert.Equal(due.AddDays(1), schedules[recurring].NextRunAt);
            var auto = requests.Single(x => x.ScheduleId == recurring);
            Assert.Equal(RequestStatus.Approved, auto.Status);
            Assert.Equal(RequestOrigin.Recurring, auto.Origin);
            Assert.True(await DbAsync(db => db.Tickets.AnyAsync(x => x.RequestId == auto.Id && x.Status == TicketStatus.Sent)));
            // Regla por historial sin historial: no inventa una cantidad; registra el error y alerta.
            Assert.DoesNotContain(requests, x => x.ScheduleId == history);
            Assert.NotEmpty(schedules[history].LastError);
            Assert.True(await DbAsync(db => db.Notifications.AnyAsync(x => x.Kind == NotificationKind.ScheduleFailure && x.EntityId == history.ToString())));
            var manual = requests.Single(x => x.ScheduleId == once);
            Assert.Equal(RequestStatus.Pending, manual.Status);
            Assert.Equal(RequestOrigin.Scheduled, manual.Origin);
            Assert.False(schedules[once].Active);
        }
    }

    [Fact]
    public async Task Anulacion_rechazo_y_cancelacion_respetan_version_y_estado()
    {
        var (admin, _) = await fixture.LoginAsync();
        var (dispatcher, _) = await fixture.LoginAsRoleAsync(Roles.Dispatcher);
        using (admin)
        using (dispatcher)
        {
            var world = await SetupAsync(admin);
            var issued = await IssueAsync(admin, world, 6);
            Assert.Equal(HttpStatusCode.PreconditionFailed, (await admin.PostAsJsonAsync($"/api/tickets/{issued.TicketId}/anular", new { version = Guid.NewGuid().ToString(), reason = "Error de captura" })).StatusCode);
            var version = (await JsonAsync(await admin.GetAsync($"/api/tickets/{issued.TicketId}"))).GetProperty("ticket").GetProperty("version").GetString();
            Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync($"/api/tickets/{issued.TicketId}/anular", new { version, reason = "Error de captura" })).StatusCode);
            var qr = Qr(issued.TicketId).Payload;
            var validation = await JsonAsync(await dispatcher.PostAsJsonAsync("/api/despachos/validar", new { qr }));
            Assert.Contains("anulado", validation.GetProperty("error").GetString()!, StringComparison.Ordinal);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await DispatchAsync(dispatcher, qr, world.Tank, 6)).StatusCode);

            var (rejectId, rejectVersion) = await RequestAsync(admin, world, 3, await AddVehicleAsync(admin, world.Department));
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/solicitudes/{rejectId}/rechazar", new { version = rejectVersion })).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync($"/api/solicitudes/{rejectId}/rechazar", new { version = rejectVersion, reason = "Sin presupuesto" })).StatusCode);
            Assert.Equal(HttpStatusCode.PreconditionFailed, (await admin.PostAsJsonAsync($"/api/solicitudes/{rejectId}/aprobar", new { version = rejectVersion })).StatusCode);
            var fresh = (await JsonAsync(await admin.GetAsync("/api/solicitudes/?status=Rejected"))).GetProperty("items").EnumerateArray()
                .Single(x => x.GetProperty("id").GetGuid() == rejectId).GetProperty("version").GetString();
            Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync($"/api/solicitudes/{rejectId}/aprobar", new { version = fresh })).StatusCode);
        }
    }

    [Theory]
    [InlineData(Roles.Viewer, true, false, false, true)]
    [InlineData(Roles.Dispatcher, false, false, true, false)]
    [InlineData(Roles.Auditor, false, false, false, true)]
    [InlineData(Roles.Supervisor, true, true, false, true)]
    [InlineData(Roles.Administrator, true, true, false, true)]
    public async Task RBAC_de_solicitudes_despacho_y_reportes(string role, bool canRequest, bool canApprove, bool canDispatch, bool canReport)
    {
        var (client, _) = await fixture.LoginAsRoleAsync(role);
        using (client)
        {
            static bool Allowed(HttpResponseMessage r) => r.StatusCode != HttpStatusCode.Forbidden;
            Assert.Equal(canRequest, Allowed(await client.PostAsJsonAsync("/api/solicitudes/", new { employeeId = Guid.NewGuid(), vehicleId = Guid.NewGuid(), departmentId = Guid.NewGuid(), fuelTypeId = Guid.NewGuid(), authorizedQuantity = 1 })));
            Assert.Equal(canApprove, Allowed(await client.PostAsJsonAsync($"/api/solicitudes/{Guid.NewGuid()}/aprobar", new { version = "x" })));
            Assert.Equal(canDispatch, Allowed(await client.PostAsJsonAsync("/api/despachos/validar", new { qr = "IC1.x" })));
            Assert.Equal(canReport, Allowed(await client.GetAsync("/api/reportes/tickets")));
            Assert.Equal(canApprove, Allowed(await client.PostAsJsonAsync("/api/inventario/ajustes", new { tankId = Guid.NewGuid(), kind = "Shrinkage", quantity = 1, reason = "Prueba" })));
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/inventario/")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/tickets/")).StatusCode);
        }
    }

    [Fact]
    public async Task Solo_existe_una_ruta_de_despacho_y_la_base_impide_atajos()
    {
        // CA-2: ninguna otra ruta escribe despachos.
        var endpoints = fixture.Factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText!.StartsWith("/api/despachos", StringComparison.OrdinalIgnoreCase))
            .SelectMany(e => (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? []).Select(m => $"{m} {e.RoutePattern.RawText}"))
            .Order().ToList();
        Assert.Equal(["GET /api/despachos/", "POST /api/despachos/", "POST /api/despachos/validar"], endpoints);

        var (admin, _) = await fixture.LoginAsync();
        Guid tank;
        using (admin) tank = (await SetupAsync(admin)).Tank;
        await using var connection = new NpgsqlConnection(fixture.ApplicationConnection);
        await connection.OpenAsync();
        var cases = new (string Sql, string State)[]
        {
            ($"INSERT INTO \"InventoryMovements\" (\"TankId\", \"Kind\", \"Quantity\", \"BalanceAfter\", \"OccurredAt\", \"Actor\", \"Reason\") VALUES ('{tank}', 'Dispatch', -1, 0, now(), 'x', 'atajo')", "23514"),
            ($"INSERT INTO \"Dispatches\" (\"Id\", \"TicketId\", \"TankId\", \"StationId\", \"Quantity\", \"Difference\", \"DifferenceReason\", \"IdentityConfirmed\", \"OperatorId\", \"OccurredAt\", \"Observations\") VALUES (gen_random_uuid(), gen_random_uuid(), '{tank}', gen_random_uuid(), 1, 0, '', true, gen_random_uuid(), now(), '')", "23503"),
            ("UPDATE \"Dispatches\" SET \"Quantity\" = 1", "42501"),
            ("DELETE FROM \"InventoryMovements\"", "42501"),
            ("UPDATE \"InventoryMovements\" SET \"Quantity\" = 1", "42501"),
            ("DELETE FROM \"Tickets\"", "42501"),
        };
        foreach (var (sql, state) in cases)
        {
            await using var command = new NpgsqlCommand(sql, connection);
            Assert.Equal(state, (await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync())).SqlState);
        }
    }

    [Fact]
    public async Task Recepcion_transferencia_ajustes_y_existencia_en_tiempo_real()
    {
        var (admin, _) = await fixture.LoginAsync();
        using (admin)
        {
            var world = await SetupAsync(admin);
            var invoice = "F" + Code();
            Assert.Equal(HttpStatusCode.Created, (await ReceiveAsync(admin, world.Tank, 100, invoice, "Purchase")).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await ReceiveAsync(admin, world.Tank, 100, invoice)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/inventario/recepciones", new { kind = "Receipt", supplierRnc = "12", supplierName = "X", invoice = "F1", quantity = 1, receivedOn = "2026-01-01", tankId = world.Tank })).StatusCode);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.PostAsJsonAsync("/api/inventario/recepciones", new { kind = "Receipt", supplierRnc = "101234567", supplierName = "X", invoice = "F" + Code(), quantity = 1, receivedOn = "2999-01-01", tankId = world.Tank })).StatusCode);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await ReceiveAsync(admin, world.Tank, 901)).StatusCode);

            var transfer = await admin.PostAsJsonAsync("/api/inventario/transferencias", new { fromTankId = world.Tank, toTankId = world.SameFuelTank, quantity = 30, reason = "Balanceo de tanques" });
            Assert.Equal(HttpStatusCode.OK, transfer.StatusCode);
            Assert.Equal(70m, (await JsonAsync(transfer)).GetProperty("fromBalance").GetDecimal());
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.PostAsJsonAsync("/api/inventario/transferencias", new { fromTankId = world.Tank, toTankId = world.OtherTank, quantity = 1, reason = "Combustible distinto" })).StatusCode);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.PostAsJsonAsync("/api/inventario/transferencias", new { fromTankId = world.Tank, toTankId = world.Tank, quantity = 1, reason = "Mismo tanque" })).StatusCode);

            var shrink = await admin.PostAsJsonAsync("/api/inventario/ajustes", new { tankId = world.Tank, kind = "Shrinkage", quantity = 25, reason = "Evaporación medida" });
            Assert.Equal(45m, (await JsonAsync(shrink)).GetProperty("balance").GetDecimal());
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.PostAsJsonAsync("/api/inventario/ajustes", new { tankId = world.Tank, kind = "NegativeAdjustment", quantity = 46, reason = "Excede la existencia" })).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync("/api/inventario/ajustes", new { tankId = world.Tank, kind = "PositiveAdjustment", quantity = 5, reason = "Corrección de medición" })).StatusCode);
            Assert.True(await DbAsync(db => db.Notifications.AnyAsync(x => x.Kind == NotificationKind.InventoryAdjustment && x.EntityId == world.Tank.ToString())));
            // 45 + 5 = 50 = nivel crítico.
            Assert.True(await DbAsync(db => db.Notifications.AnyAsync(x => x.Kind == NotificationKind.LowInventory && x.EntityId == world.Tank.ToString())));

            var stock = await JsonAsync(await admin.GetAsync("/api/inventario/"));
            var tank = stock.GetProperty("tanks").EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == world.Tank);
            Assert.Equal(50m, tank.GetProperty("balance").GetDecimal());
            Assert.True(tank.GetProperty("critical").GetBoolean());
            await IssueAsync(admin, world, 20);
            stock = await JsonAsync(await admin.GetAsync("/api/inventario/"));
            var fuel = stock.GetProperty("fuels").EnumerateArray().Single(x => x.GetProperty("fuelTypeId").GetGuid() == world.Fuel);
            Assert.Equal(80m, fuel.GetProperty("stock").GetDecimal());
            Assert.Equal(20m, fuel.GetProperty("committed").GetDecimal());
            Assert.Equal(60m, fuel.GetProperty("available").GetDecimal());

            var movements = await JsonAsync(await admin.GetAsync($"/api/inventario/movimientos?tankId={world.Tank}"));
            var kinds = movements.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("kind").GetString()).ToHashSet();
            Assert.Superset(new HashSet<string?> { "Purchase", "TransferOut", "Shrinkage", "PositiveAdjustment" }, kinds);
        }
    }

    [Fact]
    public async Task Cierre_diario_genera_acta_y_bloquea_movimientos_del_dia()
    {
        var (admin, _) = await fixture.LoginAsync();
        var (dispatcher, _) = await fixture.LoginAsRoleAsync(Roles.Dispatcher);
        using (admin)
        using (dispatcher)
        {
            var world = await SetupAsync(admin);
            Assert.Equal(HttpStatusCode.Created, (await ReceiveAsync(admin, world.Tank, 200)).StatusCode);
            var issued = await IssueAsync(admin, world, 10);
            Assert.Equal(HttpStatusCode.Created, (await DispatchAsync(dispatcher, Qr(issued.TicketId).Payload, world.Tank, 10)).StatusCode);
            var today = BusinessClock.LocalDay(DateTimeOffset.UtcNow).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var preview = await JsonAsync(await dispatcher.GetAsync($"/api/cierres/previo?stationId={world.Station}&day={today}"));
            Assert.Equal(1, preview.GetProperty("dispatchCount").GetInt32());
            var line = preview.GetProperty("lines").EnumerateArray().Single(x => x.GetProperty("tankId").GetGuid() == world.Tank);
            Assert.Equal(200m, line.GetProperty("inputs").GetDecimal());
            Assert.Equal(10m, line.GetProperty("outputs").GetDecimal());
            Assert.Equal(190m, line.GetProperty("expected").GetDecimal());

            object Counts(decimal counted) => new
            {
                stationId = world.Station, day = today, notes = "Cierre QA",
                counts = new[] { new { tankId = world.Tank, counted }, new { tankId = world.OtherTank, counted = 0m }, new { tankId = world.SameFuelTank, counted = 0m } },
            };
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await dispatcher.PostAsJsonAsync("/api/cierres/", new { stationId = world.Station, day = today, counts = new[] { new { tankId = world.Tank, counted = 1m } } })).StatusCode);
            var close = await dispatcher.PostAsJsonAsync("/api/cierres/", Counts(189));
            Assert.Equal(HttpStatusCode.Created, close.StatusCode);
            var closeId = (await JsonAsync(close)).GetProperty("id").GetGuid();
            Assert.Equal(HttpStatusCode.Conflict, (await dispatcher.PostAsJsonAsync("/api/cierres/", Counts(189))).StatusCode);
            var stored = await JsonAsync(await admin.GetAsync($"/api/cierres/{closeId}"));
            Assert.Equal(-1m, stored.GetProperty("lines").EnumerateArray().Single(x => x.GetProperty("tankId").GetGuid() == world.Tank).GetProperty("difference").GetDecimal());
            var blocked = await ReceiveAsync(admin, world.Tank, 5);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, blocked.StatusCode);
            Assert.Contains("cerrado", (await JsonAsync(blocked)).GetProperty("error").GetString()!, StringComparison.Ordinal);
            var pdf = await admin.GetAsync($"/api/cierres/{closeId}/pdf");
            Assert.StartsWith("%PDF", Encoding.ASCII.GetString((await pdf.Content.ReadAsByteArrayAsync())[..4]), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Reportes_filtran_y_exportan_excel_csv_y_pdf()
    {
        var (admin, _) = await fixture.LoginAsync();
        var (dispatcher, _) = await fixture.LoginAsRoleAsync(Roles.Dispatcher);
        var (auditor, _) = await fixture.LoginAsRoleAsync(Roles.Auditor);
        using (admin)
        using (dispatcher)
        using (auditor)
        {
            var world = await SetupAsync(admin);
            Assert.Equal(HttpStatusCode.Created, (await ReceiveAsync(admin, world.Tank, 100)).StatusCode);
            var issued = await IssueAsync(admin, world, 9);
            Assert.Equal(HttpStatusCode.Created, (await DispatchAsync(dispatcher, Qr(issued.TicketId).Payload, world.Tank, 9)).StatusCode);
            var filter = $"vehicleId={world.Vehicle}&departmentId={world.Department}&fuelTypeId={world.Fuel}&employeeId={world.Employee}";
            var json = await JsonAsync(await auditor.GetAsync($"/api/reportes/tickets?{filter}&status=Consumed"));
            Assert.Equal(issued.Number, json.GetProperty("items").EnumerateArray().Single().GetProperty("number").GetString());
            var csv = await auditor.GetAsync($"/api/reportes/despachos?{filter}&format=csv");
            Assert.Equal("text/csv", csv.Content.Headers.ContentType!.MediaType);
            var csvBytes = await csv.Content.ReadAsByteArrayAsync();
            Assert.Equal(Encoding.UTF8.GetPreamble(), csvBytes[..3]);
            Assert.Contains(issued.Number, Encoding.UTF8.GetString(csvBytes), StringComparison.Ordinal);
            var xlsx = await (await auditor.GetAsync($"/api/reportes/tickets?{filter}&format=xlsx")).Content.ReadAsByteArrayAsync();
            Assert.Equal("PK", Encoding.ASCII.GetString(xlsx[..2]));
            foreach (var path in new[] { $"tickets?{filter}", $"despachos?{filter}", $"movimientos?tankId={world.Tank}", "consumo?groupBy=vehiculo" })
            {
                var pdf = await auditor.GetAsync($"/api/reportes/{path}&format=pdf");
                Assert.True(pdf.IsSuccessStatusCode, path);
                Assert.StartsWith("%PDF", Encoding.ASCII.GetString((await pdf.Content.ReadAsByteArrayAsync())[..4]), StringComparison.Ordinal);
            }
            var consumption = await JsonAsync(await auditor.GetAsync($"/api/reportes/consumo?departmentId={world.Department}"));
            Assert.Equal(9m, consumption.GetProperty("items")[0].GetProperty("volume").GetDecimal());
            Assert.Equal(HttpStatusCode.BadRequest, (await auditor.GetAsync("/api/reportes/tickets?format=doc")).StatusCode);
            Assert.True(await DbAsync(db => db.AuditEvents.AnyAsync(x => x.Action == "report_export" && x.EntityId == "tickets.xlsx")));
        }
    }

    [Fact]
    public async Task Dashboard_y_bandeja_de_notificaciones()
    {
        var (admin, _) = await fixture.LoginAsync();
        using (admin)
        {
            var world = await SetupAsync(admin);
            await IssueAsync(admin, world, 4);
            var dashboard = await JsonAsync(await admin.GetAsync("/api/dashboard"));
            Assert.True(dashboard.GetProperty("activeTickets").GetInt32() >= 1);
            foreach (var key in new[] { "inventory", "dispatchedToday", "dispatchedMonth", "expiredTickets", "byDepartment", "byVehicle" })
                Assert.True(dashboard.TryGetProperty(key, out _), key);
            await admin.PostAsJsonAsync("/api/inventario/ajustes", new { tankId = world.Tank, kind = "PositiveAdjustment", quantity = 1, reason = "Genera alerta" });
            var inbox = await JsonAsync(await admin.GetAsync("/api/notificaciones/?unread=true"));
            var unread = inbox.GetProperty("unread").GetInt32();
            Assert.True(unread >= 1);
            var first = inbox.GetProperty("items")[0].GetProperty("id").GetInt64();
            Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/notificaciones/{first}/leida", null)).StatusCode);
            Assert.Equal(unread - 1, (await JsonAsync(await admin.GetAsync("/api/notificaciones/"))).GetProperty("unread").GetInt32());
            Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync("/api/notificaciones/leidas", null)).StatusCode);
            Assert.Equal(0, (await JsonAsync(await admin.GetAsync("/api/notificaciones/"))).GetProperty("unread").GetInt32());
        }
    }

    [Fact]
    public async Task Enlace_publico_muestra_el_ticket_y_su_QR_descargable()
    {
        var (admin, _) = await fixture.LoginAsync();
        using (admin)
        {
            var world = await SetupAsync(admin);
            var issued = await IssueAsync(admin, world, 7);
            var (token, _) = Qr(issued.TicketId);
            using var anonymous = fixture.CreateClient();
            var key = $"{issued.TicketId:N}.{token}";
            var view = await JsonAsync(await anonymous.GetAsync($"/api/publico/tickets/{key}"));
            Assert.Equal(issued.Number, view.GetProperty("number").GetString());
            Assert.True(view.GetProperty("active").GetBoolean());
            var png = await (await anonymous.GetAsync($"/api/publico/tickets/{key}/qr.png")).Content.ReadAsByteArrayAsync();
            Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, png[..4]);
            var pdf = await (await anonymous.GetAsync($"/api/publico/tickets/{key}/pdf")).Content.ReadAsByteArrayAsync();
            Assert.Equal("%PDF", Encoding.ASCII.GetString(pdf[..4]));
            Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/publico/tickets/{issued.TicketId:N}.{TicketSigner.NewToken()}")).StatusCode);
        }
    }

    [Fact]
    public async Task Parametros_exigen_version_y_rol_administrador()
    {
        var (admin, _) = await fixture.LoginAsync();
        var (supervisor, _) = await fixture.LoginAsRoleAsync(Roles.Supervisor);
        using (admin)
        using (supervisor)
        {
            var current = await JsonAsync(await admin.GetAsync("/api/parametros/"));
            object Body(int validity, string version) => new
            {
                prefix = current.GetProperty("prefix").GetString(), resetAnnually = current.GetProperty("resetAnnually").GetBoolean(),
                validityDays = validity, warningHours = current.GetProperty("warningHours").GetInt32(),
                maxActiveTicketsPerVehicle = current.GetProperty("maxActiveTicketsPerVehicle").GetInt32(), version,
            };
            var original = current.GetProperty("validityDays").GetInt32();
            var version = current.GetProperty("version").GetString()!;
            Assert.Equal(HttpStatusCode.Forbidden, (await supervisor.PutAsJsonAsync("/api/parametros/", Body(original, version))).StatusCode);
            var updated = await admin.PutAsJsonAsync("/api/parametros/", Body(original + 1, version));
            Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
            Assert.Equal(HttpStatusCode.PreconditionFailed, (await admin.PutAsJsonAsync("/api/parametros/", Body(original, version))).StatusCode);
            var restored = await admin.PutAsJsonAsync("/api/parametros/", Body(original, (await JsonAsync(updated)).GetProperty("version").GetString()!));
            Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
        }
    }
}
