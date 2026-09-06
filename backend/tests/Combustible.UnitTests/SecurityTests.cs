using System.Buffers.Binary;
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Combustible.Domain;
using Combustible.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Combustible.UnitTests;

[Collection("api")]
public sealed class SecurityTests(ApiFixture fixture)
{
    private async Task<(Guid Id, string Email, string Password)> CreateUserAsync()
    {
        var (admin, _) = await fixture.LoginAsync();
        using (admin)
        {
            var email = $"{Guid.NewGuid():N}@example.test";
            var password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
            var response = await admin.PostAsJsonAsync("/api/usuarios/", new { email, password, role = Roles.Viewer, displayName = "Prueba seguridad" });
            response.EnsureSuccessStatusCode();
            var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
            return (payload.GetProperty("id").GetGuid(), email, password);
        }
    }

    // La lista de usuarios pagina de 50 en 50 por correo; con muchas cuentas de prueba el usuario puede no estar en la primera página.
    private static async Task<JsonElement> FindUserAsync(HttpClient admin, Guid id)
    {
        for (var page = 1; ; page++)
        {
            var list = await admin.GetFromJsonAsync<JsonElement>($"/api/usuarios/?page={page}");
            var items = list.GetProperty("items").EnumerateArray().ToList();
            if (items.Count == 0) throw new InvalidOperationException($"Usuario {id} no aparece en la lista.");
            foreach (var item in items) if (item.GetProperty("id").GetGuid() == id) return item;
        }
    }

    [Fact]
    public async Task Desactivar_cuenta_revoca_JWT_y_refresh()
    {
        var user = await CreateUserAsync();
        var (client, session) = await fixture.LoginAsync(user.Email, user.Password);
        var (admin, _) = await fixture.LoginAsync();
        using (client) using (admin)
        {
            var version = (await FindUserAsync(admin, user.Id)).GetProperty("version").GetString();
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync($"/api/usuarios/{user.Id}/acceso", new { role = Roles.Viewer, active = false })).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"/api/usuarios/{user.Id}/acceso", new { role = Roles.Viewer, active = false, version })).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = session.RefreshToken })).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login", new { email = user.Email, password = user.Password })).StatusCode);
        }
    }

    [Fact]
    public async Task Edicion_de_usuario_es_atomica_y_reset_revoca_sesiones()
    {
        var user = await CreateUserAsync();
        var (admin, _) = await fixture.LoginAsync();
        var (client, _) = await fixture.LoginAsync(user.Email, user.Password);
        using (admin) using (client)
        {
            var current = await FindUserAsync(admin, user.Id);
            var version = current.GetProperty("version").GetString();
            var update = new { email = user.Email, displayName = "Perfil corregido", role = Roles.Auditor, active = true, version };
            Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"/api/usuarios/{user.Id}", update)).StatusCode);
            Assert.Equal(HttpStatusCode.PreconditionFailed, (await admin.PutAsJsonAsync($"/api/usuarios/{user.Id}", update)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
            var (renewed, _) = await fixture.LoginAsync(user.Email, user.Password);
            using (renewed)
            {
                Assert.Equal(HttpStatusCode.OK, (await renewed.GetAsync("/api/auditoria")).StatusCode);
                var newPassword = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
                Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync($"/api/usuarios/{user.Id}/password", new { password = newPassword })).StatusCode);
                Assert.Equal(HttpStatusCode.Unauthorized, (await renewed.GetAsync("/api/auth/me")).StatusCode);
                using var anonymous = fixture.CreateClient();
                Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/auth/login", new { email = user.Email, password = user.Password })).StatusCode);
                Assert.Equal(HttpStatusCode.OK, (await anonymous.PostAsJsonAsync("/api/auth/login", new { email = user.Email, password = newPassword })).StatusCode);
            }
        }
    }

    [Fact]
    public async Task Cinco_fallos_bloquean_incluso_la_contrasena_correcta()
    {
        var user = await CreateUserAsync();
        using var client = fixture.CreateClient();
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login", new { email = user.Email, password = "Esta frase es incorrecta" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login", new { email = user.Email, password = user.Password })).StatusCode);
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True((await db.Users.SingleAsync(x => x.Id == user.Id)).LockoutEnd > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task MFA_necesita_codigo_valido_y_revoca_sesiones_anteriores()
    {
        var user = await CreateUserAsync();
        var (client, _) = await fixture.LoginAsync(user.Email, user.Password);
        using (client)
        {
            var setup = await client.PostAsJsonAsync("/api/auth/mfa/setup", new { password = user.Password });
            setup.EnsureSuccessStatusCode();
            var enrollment = await setup.Content.ReadFromJsonAsync<JsonElement>();
            var secret = enrollment.GetProperty("secret").GetString()!;
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", enrollment.GetProperty("session").GetProperty("accessToken").GetString());
            var code = Totp(secret);
            var enabled = await client.PostAsJsonAsync("/api/auth/mfa/enable", new { password = user.Password, code });
            Assert.Equal(HttpStatusCode.OK, enabled.StatusCode);
            var recovery = (await enabled.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("recoveryCodes")[0].GetString();
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login", new { email = user.Email, password = user.Password })).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login", new { email = user.Email, password = user.Password, code = "invalid" })).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/login", new { email = user.Email, password = user.Password, code = Totp(secret) })).StatusCode);
            var recovered = await client.PostAsJsonAsync("/api/auth/login", new { email = user.Email, password = user.Password, recoveryCode = recovery });
            Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
            var recoveredSession = (await recovered.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString();
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login", new { email = user.Email, password = user.Password, recoveryCode = recovery })).StatusCode);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", recoveredSession);
            var regenerated = await client.PostAsJsonAsync("/api/auth/mfa/recovery-codes", new { password = user.Password, code = Totp(secret) });
            Assert.Equal(HttpStatusCode.OK, regenerated.StatusCode);
            var replacement = (await regenerated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("recoveryCodes")[0].GetString();
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
            var newLogin = await client.PostAsJsonAsync("/api/auth/login", new { email = user.Email, password = user.Password, code = Totp(secret) });
            newLogin.EnsureSuccessStatusCode();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await newLogin.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString());
            Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/auth/mfa/disable", new { password = user.Password, recoveryCode = replacement })).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/login", new { email = user.Email, password = user.Password })).StatusCode);
        }
    }

    [Fact]
    public async Task JWT_alterado_o_sesion_expirada_no_autoriza()
    {
        var (client, session) = await fixture.LoginAsync();
        using (client)
        {
            var parts = session.AccessToken.Split('.');
            parts[2] = (parts[2][0] == 'a' ? "b" : "a") + parts[2][1..];
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", string.Join('.', parts));
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
            using var scope = fixture.Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var sid = Guid.Parse(new JwtSecurityTokenHandler().ReadJwtToken(session.AccessToken).Claims.Single(x => x.Type == "sid").Value);
            await db.Sessions.Where(x => x.Id == sid).ExecuteUpdateAsync(update => update.SetProperty(x => x.ExpiresAt, DateTimeOffset.UtcNow.AddSeconds(-1)));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
        }
    }

    [Fact]
    public async Task Limite_IP_responde_429_sin_ejecutar_login()
    {
        await using var isolated = fixture.Factory.WithWebHostBuilder(_ => { });
        using var client = isolated.CreateClient();
        HttpStatusCode last = default;
        for (var i = 0; i < 61; i++)
            last = (await client.PostAsJsonAsync("/api/auth/login", new { email = "", password = "" })).StatusCode;
        Assert.Equal(HttpStatusCode.TooManyRequests, last);
    }

    private static string Totp(string secret)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bits = 0; var value = 0; var key = new List<byte>();
        foreach (var c in secret)
        {
            value = (value << 5) | alphabet.IndexOf(c, StringComparison.Ordinal); bits += 5;
            if (bits >= 8) { bits -= 8; key.Add((byte)(value >> bits)); }
        }
        Span<byte> counter = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30);
        // SHA-1 is mandated by the standard authenticator TOTP algorithm, not used for passwords.
#pragma warning disable CA5350
        var hash = HMACSHA1.HashData(key.ToArray(), counter);
#pragma warning restore CA5350
        var offset = hash[^1] & 15;
        var number = BinaryPrimitives.ReadInt32BigEndian(hash.AsSpan(offset, 4)) & 0x7fffffff;
        return (number % 1000000).ToString("D6", CultureInfo.InvariantCulture);
    }
}
