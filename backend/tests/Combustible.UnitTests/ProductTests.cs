using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Combustible.Api.Security;
using Combustible.Domain;
using Combustible.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Combustible.UnitTests;

[Collection("api")]
public sealed class ProductTests(ApiFixture fixture)
{
    [Theory]
    [InlineData("/api/departamentos/")]
    [InlineData("/api/empleados/")]
    [InlineData("/api/vehiculos/")]
    [InlineData("/api/usuarios/")]
    [InlineData("/api/auditoria")]
    public async Task Sin_token_no_hay_acceso(string path)
    {
        using var client = fixture.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task Persistencia_versiones_y_auditoria_transaccional()
    {
        var (client, _) = await fixture.LoginAsync();
        using (client)
        {
            var code = Guid.NewGuid().ToString("N")[..12];
            var response = await client.PostAsJsonAsync("/api/departamentos/", new { code, name = "Departamento de prueba" });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var item = (await response.Content.ReadFromJsonAsync<Department>())!;
            using var fresh = fixture.Factory.Services.CreateScope();
            var db = fresh.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.True(await db.Departments.AnyAsync(x => x.Id == item.Id));
            Assert.True(await db.AuditEvents.AnyAsync(x => x.EntityId == item.Id.ToString() && x.Action == "create"));
            Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/departamentos/", new { code, name = "Duplicado" })).StatusCode);
            var count = await db.AuditEvents.CountAsync(x => x.Entity == "departamentos");
            var update = new HttpRequestMessage(HttpMethod.Put, $"/api/departamentos/{item.Id}")
            { Content = JsonContent.Create(new { code, name = "Nombre corregido", active = false }) };
            update.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{item.Version}\""));
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(update)).StatusCode);
            var stale = new HttpRequestMessage(HttpMethod.Put, $"/api/departamentos/{item.Id}")
            { Content = JsonContent.Create(new { code, name = "Sobrescritura" }) };
            stale.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{item.Version}\""));
            Assert.Equal(HttpStatusCode.PreconditionFailed, (await client.SendAsync(stale)).StatusCode);
            Assert.Equal(count + 1, await db.AuditEvents.CountAsync(x => x.Entity == "departamentos"));
        }
    }

    [Fact]
    public async Task Refresh_rota_y_logout_invalida_acceso()
    {
        var (client, session) = await fixture.LoginAsync();
        using (client)
        {
            var response = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = session.RefreshToken });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var renewed = (await response.Content.ReadFromJsonAsync<SessionResponse>())!;
            Assert.NotEqual(session.RefreshToken, renewed.RefreshToken);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = session.RefreshToken })).StatusCode);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", renewed.AccessToken);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
        }
        var (second, _) = await fixture.LoginAsync();
        using (second)
        {
            Assert.Equal(HttpStatusCode.NoContent, (await second.PostAsync("/api/auth/logout", null)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await second.GetAsync("/api/auth/me")).StatusCode);
        }
    }

    [Theory]
    [InlineData(Roles.Viewer, false, false)]
    [InlineData(Roles.Auditor, false, true)]
    [InlineData(Roles.Dispatcher, false, false)]
    [InlineData(Roles.Supervisor, true, false)]
    public async Task RBAC_limita_escritura_y_auditoria(string role, bool canWrite, bool canAudit)
    {
        var (admin, _) = await fixture.LoginAsync();
        using (admin)
        {
            var email = $"{Guid.NewGuid():N}@example.test";
            var password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
            var create = await admin.PostAsJsonAsync("/api/usuarios/", new { email, password, role, displayName = "Prueba RBAC" });
            Assert.Equal(HttpStatusCode.Created, create.StatusCode);
            var (client, _) = await fixture.LoginAsync(email, password);
            using (client)
            {
                Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/departamentos/")).StatusCode);
                Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/usuarios/")).StatusCode);
                Assert.Equal(canAudit ? HttpStatusCode.OK : HttpStatusCode.Forbidden, (await client.GetAsync("/api/auditoria")).StatusCode);
                Assert.Equal(canWrite ? HttpStatusCode.Created : HttpStatusCode.Forbidden,
                    (await client.PostAsJsonAsync("/api/departamentos/", new { code = Guid.NewGuid().ToString("N")[..12], name = "Prueba permisos" })).StatusCode);
            }
        }
    }

    [Fact]
    public async Task Refresh_concurrente_no_emite_dos_sesiones_validas()
    {
        var (_, session) = await fixture.LoginAsync();
        using var first = fixture.CreateClient();
        using var second = fixture.CreateClient();
        var results = await Task.WhenAll(
            first.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = session.RefreshToken }),
            second.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = session.RefreshToken }));
        Assert.Single(results, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Single(results, x => x.StatusCode == HttpStatusCode.Unauthorized);
        var winner = (await results.Single(x => x.IsSuccessStatusCode).Content.ReadFromJsonAsync<SessionResponse>())!;
        first.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", winner.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await first.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Escrituras_concurrentes_conservan_la_cadena_de_auditoria()
    {
        var (client, _) = await fixture.LoginAsync();
        using (client)
        {
            var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
                client.PostAsJsonAsync("/api/departamentos/", new { code = Guid.NewGuid().ToString("N")[..15], name = "Prueba concurrente" })));
            Assert.All(responses, response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auditoria/verificar")).StatusCode);
        }
    }

    [Fact]
    public async Task SQL_no_permite_modificar_borrar_ni_truncar_auditoria()
    {
        await using var connection = new NpgsqlConnection(fixture.ApplicationConnection);
        await connection.OpenAsync();
        foreach (var sql in new[] { "UPDATE \"AuditEvents\" SET \"Action\" = 'tamper'", "DELETE FROM \"AuditEvents\"", "TRUNCATE \"AuditEvents\"" })
        {
            await using var command = new NpgsqlCommand(sql, connection);
            var error = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
            Assert.Equal("42501", error.SqlState);
        }
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await db.AuditEvents.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
        var previous = string.Empty;
        foreach (var row in rows)
        {
            Assert.Equal(previous, row.PreviousHash);
            Assert.Equal(AuditWriter.ComputeHash(row), row.Hash);
            previous = row.Hash;
        }
        Assert.NotEmpty(rows);
        var (admin, _) = await fixture.LoginAsync();
        using (admin)
        {
            Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/auditoria/verificar")).StatusCode);
        }
    }

    [Fact]
    public async Task Rechaza_contrasena_corta_y_campos_invalidos()
    {
        var (client, _) = await fixture.LoginAsync();
        using (client)
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/usuarios/", new { email = "invalid@example.test", password = "short", role = Roles.Viewer, displayName = "Prueba" })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/departamentos/", new { code = "  ", name = "" })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/departamentos/?page=-1")).StatusCode);
        }
    }
}
