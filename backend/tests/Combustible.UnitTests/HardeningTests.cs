using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Combustible.Api;
using Combustible.Api.Security;
using Combustible.Domain;
using Combustible.Infrastructure.Data;
using Combustible.Infrastructure.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Combustible.UnitTests;

// Fase 6: RS-03 (reposo y tránsito), RS-05 (OAuth 2.0), RS-06 (ancla externa) y versión en cambios de acceso.
[Collection("api")]
public sealed class HardeningTests(ApiFixture fixture)
{
    private static string Code() => Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();

    private static string NationalId() => string.Concat(Enumerable.Range(0, 11).Select(_ => RandomNumberGenerator.GetInt32(10).ToString(System.Globalization.CultureInfo.InvariantCulture)));

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => (await response.Content.ReadFromJsonAsync<JsonElement>())!;

    private async Task<(string NationalId, string Email, string Mobile)> RawEmployeeAsync(Guid id)
    {
        await using var owner = new NpgsqlConnection(fixture.OwnerConnection);
        await owner.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT \"NationalId\", \"Email\", \"Mobile\" FROM \"Employees\" WHERE \"Id\" = @id", owner);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetString(0), reader.GetString(1), reader.GetString(2));
    }

    [Fact]
    public async Task Datos_personales_del_empleado_se_guardan_cifrados_y_la_cedula_sigue_siendo_unica()
    {
        var (admin, _) = await fixture.LoginAsync();
        using (admin)
        {
            var department = (await JsonAsync(await admin.PostAsJsonAsync("/api/departamentos/", new { code = Code(), name = "QA cifrado" }))).GetProperty("id").GetGuid();
            var nationalId = NationalId();
            var email = $"{Code().ToLowerInvariant()}@example.test";
            var body = new { code = Code(), fullName = "Empleado Cifrado", nationalId, departmentId = department, position = "Chofer", email, mobile = "8095550199" };
            var created = await admin.PostAsJsonAsync("/api/empleados/", body);
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var id = (await JsonAsync(created)).GetProperty("id").GetGuid();

            var raw = await RawEmployeeAsync(id);
            foreach (var value in new[] { raw.NationalId, raw.Email, raw.Mobile })
            {
                Assert.StartsWith("v1:", value, StringComparison.Ordinal);
                Assert.DoesNotContain(nationalId, value, StringComparison.Ordinal);
                Assert.DoesNotContain(email, value, StringComparison.Ordinal);
            }
            var read = await JsonAsync(await admin.GetAsync($"/api/empleados/{id}"));
            Assert.Equal(nationalId, read.GetProperty("nationalId").GetString());
            Assert.Equal(email, read.GetProperty("email").GetString());
            // Otra persona con la misma cédula: el índice ciego lo detecta aunque el texto cifrado difiera.
            Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync("/api/empleados/", body with { code = Code(), email = "otro@example.test" })).StatusCode);
        }
    }

    [Fact]
    public async Task Empleados_anteriores_al_cifrado_se_cifran_al_inicializar()
    {
        var (admin, _) = await fixture.LoginAsync();
        using (admin)
        {
            var department = (await JsonAsync(await admin.PostAsJsonAsync("/api/departamentos/", new { code = Code(), name = "QA migración" }))).GetProperty("id").GetGuid();
            var id = Guid.NewGuid();
            var nationalId = NationalId();
            await using (var owner = new NpgsqlConnection(fixture.OwnerConnection))
            {
                await owner.OpenAsync();
                await using var insert = new NpgsqlCommand("""
                    INSERT INTO "Employees" ("Id", "Version", "Active", "Code", "FullName", "NationalId", "DepartmentId", "Position", "Email", "Mobile")
                    VALUES (@id, gen_random_uuid(), true, @code, 'Empleado Heredado', @n, @d, 'Chofer', 'heredado@example.test', '8095550100')
                    """, owner);
                insert.Parameters.AddWithValue("id", id);
                insert.Parameters.AddWithValue("code", Code());
                insert.Parameters.AddWithValue("n", nationalId);
                insert.Parameters.AddWithValue("d", department);
                await insert.ExecuteNonQueryAsync();
            }
            var protector = fixture.Factory.Services.GetRequiredService<FieldProtector>();
            Assert.Equal(1, await DatabaseBootstrap.EncryptEmployeesAsync(fixture.OwnerConnection, protector));
            Assert.StartsWith("v1:", (await RawEmployeeAsync(id)).NationalId, StringComparison.Ordinal);
            var read = await JsonAsync(await admin.GetAsync($"/api/empleados/{id}"));
            Assert.Equal(nationalId, read.GetProperty("nationalId").GetString());
            Assert.Equal("heredado@example.test", read.GetProperty("email").GetString());
            Assert.Equal(0, await DatabaseBootstrap.EncryptEmployeesAsync(fixture.OwnerConnection, protector));
        }
    }

    [Fact]
    public async Task Kestrel_rechaza_TLS_1_2_y_negocia_TLS_1_3()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1));
        using var certificate = X509CertificateLoader.LoadPkcs12(generated.Export(X509ContentType.Pfx), null);
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.ConfigureKestrel(options =>
        {
            TransportSecurity.Configure(options);
            options.Listen(IPAddress.Loopback, 0, listen => listen.UseHttps(certificate));
        });
        await using var app = builder.Build();
        app.MapGet("/", () => "ok");
        await app.StartAsync();
        var port = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()).Port;

        async Task<SslProtocols> HandshakeAsync(SslProtocols protocol)
        {
            using var tcp = new TcpClient();
            await tcp.ConnectAsync(IPAddress.Loopback, port);
            await using var ssl = new SslStream(tcp.GetStream(), false, (_, cert, _, _) => cert?.GetCertHashString() == certificate.GetCertHashString());
            await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = "localhost", EnabledSslProtocols = protocol });
            return ssl.SslProtocol;
        }

        var refused = await Record.ExceptionAsync(() => HandshakeAsync(SslProtocols.Tls12));
        Assert.True(refused is AuthenticationException or IOException, $"TLS 1.2 debía rechazarse: {refused?.GetType().Name}");
        Assert.Equal(SslProtocols.Tls13, await HandshakeAsync(SslProtocols.Tls13));
        await app.StopAsync();
    }

    [Fact]
    public async Task Ancla_externa_detecta_cadena_reconstruida_por_un_superusuario()
    {
        var (admin, _) = await fixture.LoginAsync();
        using (admin)
        {
            var anchor = await JsonAsync(await admin.GetAsync("/api/auditoria/ancla"));
            Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync("/api/auditoria/ancla/verificar", anchor)).StatusCode);
            var forged = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(anchor.GetRawText())!;
            forged["count"] = JsonSerializer.SerializeToElement(anchor.GetProperty("count").GetInt64() + 1);
            Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync("/api/auditoria/ancla/verificar", forged)).StatusCode);
            Assert.StartsWith("-----BEGIN PUBLIC KEY-----", await (await admin.GetAsync("/api/auditoria/clave-publica")).Content.ReadAsStringAsync(), StringComparison.Ordinal);

            // Superusuario: altera un evento anterior al ancla y recalcula TODA la cadena para que cuadre.
            await using (var owner = new NpgsqlConnection(fixture.OwnerConnection))
            {
                await owner.OpenAsync();
                await using var tx = await owner.BeginTransactionAsync();
                await using (var lockAudit = new NpgsqlCommand("SELECT pg_advisory_xact_lock(710210)", owner, tx)) await lockAudit.ExecuteNonQueryAsync();
                var target = anchor.GetProperty("lastId").GetInt64() - 2;
                await using (var tamper = new NpgsqlCommand("UPDATE \"AuditEvents\" SET \"Action\" = 'manipulado' WHERE \"Id\" = @id", owner, tx))
                {
                    tamper.Parameters.AddWithValue("id", target);
                    Assert.Equal(1, await tamper.ExecuteNonQueryAsync());
                }
                var events = new List<AuditEvent>();
                await using (var select = new NpgsqlCommand("SELECT \"Id\", \"OccurredAt\", \"Actor\", \"Ip\", \"Action\", \"Entity\", \"EntityId\" FROM \"AuditEvents\" ORDER BY \"Id\"", owner, tx))
                await using (var reader = await select.ExecuteReaderAsync())
                    while (await reader.ReadAsync())
                        events.Add(new AuditEvent
                        {
                            Id = reader.GetInt64(0), OccurredAt = reader.GetFieldValue<DateTimeOffset>(1), Actor = reader.GetString(2), Ip = reader.GetString(3),
                            Action = reader.GetString(4), Entity = reader.GetString(5), EntityId = reader.GetString(6),
                        });
                var previous = string.Empty;
                foreach (var entry in events)
                {
                    entry.PreviousHash = previous;
                    entry.Hash = AuditWriter.ComputeHash(entry);
                    previous = entry.Hash;
                    await using var rewrite = new NpgsqlCommand("UPDATE \"AuditEvents\" SET \"PreviousHash\" = @p, \"Hash\" = @h WHERE \"Id\" = @id", owner, tx);
                    rewrite.Parameters.AddWithValue("p", entry.PreviousHash);
                    rewrite.Parameters.AddWithValue("h", entry.Hash);
                    rewrite.Parameters.AddWithValue("id", entry.Id);
                    await rewrite.ExecuteNonQueryAsync();
                }
                await tx.CommitAsync();
            }
            // La verificación interna no lo detecta (límite documentado en ADR-006/007)...
            Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/auditoria/verificar")).StatusCode);
            // ...pero el ancla guardada fuera del sistema sí.
            var verdict = await admin.PostAsJsonAsync("/api/auditoria/ancla/verificar", anchor);
            Assert.Equal(HttpStatusCode.Conflict, verdict.StatusCode);
            Assert.Contains("reconstruida", (await JsonAsync(verdict)).GetProperty("reason").GetString()!, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Cambio_de_acceso_de_usuario_exige_la_version_vista()
    {
        var (admin, _) = await fixture.LoginAsync();
        var (target, email) = await fixture.LoginAsRoleAsync(Roles.Viewer);
        target.Dispose();
        using (admin)
        {
            var users = await JsonAsync(await admin.GetAsync("/api/usuarios/?page=1"));
            var page = 1;
            JsonElement? user = null;
            while (user is null)
            {
                user = users.GetProperty("items").EnumerateArray().Cast<JsonElement?>().FirstOrDefault(x => x!.Value.GetProperty("email").GetString() == email);
                if (user is null) users = await JsonAsync(await admin.GetAsync($"/api/usuarios/?page={++page}"));
            }
            var id = user.Value.GetProperty("id").GetGuid();
            var version = user.Value.GetProperty("version").GetString();
            Assert.Equal(HttpStatusCode.PreconditionFailed, (await admin.PutAsJsonAsync($"/api/usuarios/{id}/acceso", new { role = Roles.Auditor, active = true, version = Guid.NewGuid().ToString() })).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"/api/usuarios/{id}/acceso", new { role = Roles.Auditor, active = true, version })).StatusCode);
            Assert.Equal(HttpStatusCode.PreconditionFailed, (await admin.PutAsJsonAsync($"/api/usuarios/{id}/acceso", new { role = Roles.Viewer, active = true, version })).StatusCode);
        }
    }

    private static async Task<HttpResponseMessage> TokenAsync(HttpClient client, string clientId, string secret, string grant = "client_credentials") =>
        await client.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = grant, ["client_id"] = clientId, ["client_secret"] = secret, ["scope"] = "combustible.api",
            ["username"] = "admin@localhost.test", ["password"] = "irrelevante",
        }));

    private static async Task<(string ClientId, string Secret)> RegisterClientAsync(HttpClient admin, string role)
    {
        var created = await admin.PostAsJsonAsync("/api/integraciones/", new { displayName = "Sistema QA " + Code(), role });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await JsonAsync(created);
        return (body.GetProperty("clientId").GetString()!, body.GetProperty("clientSecret").GetString()!);
    }

    [Fact]
    public async Task OAuth2_client_credentials_emite_JWT_de_acceso_sometido_al_RBAC()
    {
        var (admin, _) = await fixture.LoginAsync();
        using (admin)
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/integraciones/", new { displayName = "Sistema QA", role = Roles.Administrator })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/integraciones/", new { displayName = "Sistema QA", role = Roles.Dispatcher })).StatusCode);
            var (clientId, secret) = await RegisterClientAsync(admin, Roles.Viewer);
            using var anonymous = fixture.CreateClient();
            var tokenResponse = await TokenAsync(anonymous, clientId, secret);
            Assert.Equal(HttpStatusCode.OK, tokenResponse.StatusCode);
            var token = await JsonAsync(tokenResponse);
            Assert.Equal("Bearer", token.GetProperty("token_type").GetString());
            var accessToken = token.GetProperty("access_token").GetString()!;
            var header = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(Pad(accessToken.Split('.')[0])))).RootElement;
            Assert.Equal("at+jwt", header.GetProperty("typ").GetString());

            using var integration = fixture.CreateClient();
            integration.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            Assert.Equal(HttpStatusCode.OK, (await integration.GetAsync("/api/tickets/")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await integration.GetAsync("/api/inventario/")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await integration.GetAsync("/api/reportes/despachos")).StatusCode);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await integration.PostAsJsonAsync("/api/solicitudes/", new { employeeId = Guid.NewGuid(), vehicleId = Guid.NewGuid(), departmentId = Guid.NewGuid(), fuelTypeId = Guid.NewGuid(), authorizedQuantity = 1 })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await integration.PostAsJsonAsync($"/api/solicitudes/{Guid.NewGuid()}/aprobar", new { version = "x" })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await integration.GetAsync("/api/usuarios/")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await integration.GetAsync("/api/notificaciones/")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await integration.GetAsync("/api/auth/me")).StatusCode);

            // Un cliente con rol Supervisor tampoco despacha: el despacho exige una persona con sesión.
            var (supervisorId, supervisorSecret) = await RegisterClientAsync(admin, Roles.Supervisor);
            var supervisorToken = (await JsonAsync(await TokenAsync(anonymous, supervisorId, supervisorSecret))).GetProperty("access_token").GetString();
            using var supervisorClient = fixture.CreateClient();
            supervisorClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", supervisorToken);
            Assert.Equal(HttpStatusCode.Forbidden, (await supervisorClient.PostAsJsonAsync("/api/despachos/validar", new { qr = "IC1.x" })).StatusCode);
            // ADR-016: aprobar, anular, inventario y catálogos exigen persona con sesión; la integración solo consulta y solicita.
            Assert.Equal(HttpStatusCode.Forbidden, (await supervisorClient.PostAsJsonAsync($"/api/solicitudes/{Guid.NewGuid()}/aprobar", new { version = "x" })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await supervisorClient.PostAsJsonAsync("/api/inventario/ajustes", new { tankId = Guid.NewGuid(), kind = "Shrinkage", quantity = 1, reason = "Prueba" })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await supervisorClient.PostAsJsonAsync("/api/departamentos/", new { code = "OAUTH", name = "No permitido" })).StatusCode);
            var employees = await JsonAsync(await supervisorClient.GetAsync("/api/empleados/"));
            Assert.All(employees.GetProperty("items").EnumerateArray(), e => Assert.False(e.TryGetProperty("nationalId", out _)));

            var list = await JsonAsync(await admin.GetAsync("/api/integraciones/"));
            Assert.Contains(list.GetProperty("items").EnumerateArray(), x => x.GetProperty("clientId").GetString() == clientId && x.GetProperty("role").GetString() == Roles.Viewer);
        }
    }

    [Fact]
    public async Task OAuth2_rechaza_secreto_invalido_concesion_password_y_cliente_revocado()
    {
        var (admin, _) = await fixture.LoginAsync();
        using (admin)
        {
            var (clientId, secret) = await RegisterClientAsync(admin, Roles.Auditor);
            using var anonymous = fixture.CreateClient();
            var wrong = await TokenAsync(anonymous, clientId, secret + "x");
            Assert.False(wrong.IsSuccessStatusCode);
            Assert.Equal("invalid_client", (await JsonAsync(wrong)).GetProperty("error").GetString());
            var password = await TokenAsync(anonymous, clientId, secret, "password");
            Assert.False(password.IsSuccessStatusCode);
            Assert.Equal("unsupported_grant_type", (await JsonAsync(password)).GetProperty("error").GetString());

            var accessToken = (await JsonAsync(await TokenAsync(anonymous, clientId, secret))).GetProperty("access_token").GetString();
            using var integration = fixture.CreateClient();
            integration.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            Assert.Equal(HttpStatusCode.OK, (await integration.GetAsync("/api/reportes/tickets")).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/integraciones/{clientId}")).StatusCode);
            // El token emitido deja de valer de inmediato, no al vencer.
            Assert.Equal(HttpStatusCode.Unauthorized, (await integration.GetAsync("/api/reportes/tickets")).StatusCode);
            Assert.False((await TokenAsync(anonymous, clientId, secret)).IsSuccessStatusCode);
        }
    }

    [Fact]
    public async Task Un_solo_origen_sirve_la_interfaz_con_CSP_y_la_API_sigue_intacta()
    {
        var root = Directory.CreateTempSubdirectory("web-root-").FullName;
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "index.html"), "<!doctype html><title>INTEC</title>");
            Directory.CreateDirectory(Path.Combine(root, "assets"));
            await File.WriteAllTextAsync(Path.Combine(root, "assets", "app.js"), "console.log(1)");
            var values = new Dictionary<string, string?>(fixture.Settings) { ["WEB_ROOT"] = root };
            await using var factory = ApiFixture.CreateFactory(values);
            using var client = factory.CreateClient();

            foreach (var path in new[] { "/", "/ticket/" + new string('a', 32) + "." + new string('B', 22) })
            {
                var page = await client.GetAsync(path);
                Assert.Equal(HttpStatusCode.OK, page.StatusCode);
                Assert.Contains("<title>INTEC</title>", await page.Content.ReadAsStringAsync(), StringComparison.Ordinal);
                var csp = string.Join(';', page.Headers.GetValues("Content-Security-Policy"));
                Assert.Contains("frame-ancestors 'none'", csp, StringComparison.Ordinal);
                Assert.Contains("'wasm-unsafe-eval'", csp, StringComparison.Ordinal);
                Assert.Equal("DENY", page.Headers.GetValues("X-Frame-Options").Single());
            }
            var script = await client.GetAsync("/assets/app.js");
            Assert.Equal(HttpStatusCode.OK, script.StatusCode);
            Assert.Equal("console.log(1)", await script.Content.ReadAsStringAsync());
            // Un archivo inexistente no se disfraza de index.html, y la API nunca cae en el SPA.
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/assets/falta.js")).StatusCode);
            var api = await client.GetAsync("/api/no-existe");
            Assert.Equal(HttpStatusCode.NotFound, api.StatusCode);
            Assert.False(api.Headers.Contains("Content-Security-Policy"));
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/tickets/")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);

            var empty = Directory.CreateTempSubdirectory("web-empty-").FullName;
            await using var broken = ApiFixture.CreateFactory(new Dictionary<string, string?>(fixture.Settings) { ["WEB_ROOT"] = empty });
            var error = Assert.ThrowsAny<Exception>(() => broken.CreateClient());
            Assert.Contains("WEB_ROOT", error.ToString(), StringComparison.Ordinal);
            Directory.Delete(empty);
        }
        finally { Directory.Delete(root, true); }
    }

    private static string Pad(string base64Url)
    {
        var s = base64Url.Replace('-', '+').Replace('_', '/');
        return s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=');
    }
}
