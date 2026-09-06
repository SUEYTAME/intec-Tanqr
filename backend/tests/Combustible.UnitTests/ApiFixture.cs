using System.Globalization;
using System.Net.Http.Headers;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Combustible.Api;
using Combustible.Api.Security;
using Combustible.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Combustible.UnitTests;

public sealed class ApiFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("combustible").Build();
    // Servidor SMTP real de desarrollo: el correo del ticket viaja por SMTP y se consulta por su API.
    private readonly IContainer _mailpit = new ContainerBuilder("axllent/mailpit:v1.31.2")
        .WithPortBinding(1025, true).WithPortBinding(8025, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(8025).ForPath("/api/v1/info"))).Build();
    private readonly string _appPassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private readonly string _key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    public string Password { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
    public WebApplicationFactory<Program> Factory { get; private set; } = null!;
    public string ApplicationConnection { get; private set; } = string.Empty;
    public string OwnerConnection => _postgres.GetConnectionString();
    public Uri MailpitApi { get; private set; } = null!;
    public string OutboxDirectory { get; } = Path.Combine(Path.GetTempPath(), "combustible-outbox-" + Guid.NewGuid().ToString("N"));
    public IReadOnlyDictionary<string, string?> Settings => _values;
    private Dictionary<string, string?> _values = [];
    private int _clientNumber;

    public HttpClient CreateClient()
    {
        var number = Interlocked.Increment(ref _clientNumber);
        var address = IPAddress.Parse($"10.0.{number / 256}.{number % 256}");
        return new HttpClient(Factory.Server.CreateHandler(context => context.Connection.RemoteIpAddress = address))
        { BaseAddress = new Uri("http://localhost") };
    }

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _mailpit.StartAsync());
        MailpitApi = new Uri($"http://{_mailpit.Hostname}:{_mailpit.GetMappedPublicPort(8025)}/api/v1/");
        using var qrKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var oauthKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var values = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Database"] = _postgres.GetConnectionString(), ["JWT_SIGNING_KEY"] = _key,
            ["APP_DB_PASSWORD"] = _appPassword, ["BOOTSTRAP_EMAIL"] = "admin@localhost.test",
            ["BOOTSTRAP_PASSWORD"] = Password, ["Logging:LogLevel:Default"] = "Warning",
            ["DATA_ENCRYPTION_KEY"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            ["QR_SIGNING_KEY_B64"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(qrKey.ExportPkcs8PrivateKeyPem())),
            ["OAUTH_SIGNING_KEY_B64"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(oauthKey.ExportPkcs8PrivateKeyPem())),
            ["OUTBOX_DIR"] = OutboxDirectory, ["PUBLIC_BASE_URL"] = "http://localhost:5173", ["Jobs:Enabled"] = "false",
            ["SMTP_HOST"] = _mailpit.Hostname, ["SMTP_PORT"] = _mailpit.GetMappedPublicPort(1025).ToString(CultureInfo.InvariantCulture),
            ["SMTP_FROM"] = "combustible@localhost.test", ["SMTP_REQUIRE_TLS"] = "false",
        };
        _values = values;
        await using (var admin = CreateFactory(values))
        {
            await DatabaseBootstrap.InitializeAsync(admin.Services, new ConfigurationBuilder().AddInMemoryCollection(values).Build());
        }
        var connection = new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString())
        { Username = "combustible_app", Password = _appPassword };
        ApplicationConnection = connection.ConnectionString;
        values["ConnectionStrings:Database"] = ApplicationConnection;
        Factory = CreateFactory(values);
    }

    public static WebApplicationFactory<Program> CreateFactory(IReadOnlyDictionary<string, string?> values) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            foreach (var pair in values) builder.UseSetting(pair.Key, pair.Value);
        });

    public async Task<(HttpClient Client, SessionResponse Session)> LoginAsync(string? email = null, string? password = null)
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email = email ?? "admin@localhost.test", password = password ?? Password });
        response.EnsureSuccessStatusCode();
        var session = (await response.Content.ReadFromJsonAsync<SessionResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        return (client, session);
    }

    public async Task<(HttpClient Client, string Email)> LoginAsRoleAsync(string role)
    {
        var (admin, _) = await LoginAsync();
        using (admin)
        {
            var email = $"{role.ToLowerInvariant()}-{Guid.NewGuid():N}@example.test";
            var password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
            var create = await admin.PostAsJsonAsync("/api/usuarios/", new { email, password, role, displayName = $"Prueba {role}" });
            create.EnsureSuccessStatusCode();
            var (client, _) = await LoginAsync(email, password);
            return (client, email);
        }
    }

    public async Task DisposeAsync()
    {
        if (Factory is not null) await Factory.DisposeAsync();
        await _postgres.DisposeAsync();
        await _mailpit.DisposeAsync();
        if (Directory.Exists(OutboxDirectory)) Directory.Delete(OutboxDirectory, true);
    }
}

[CollectionDefinition("api")]
public sealed class ApiTestGroup : ICollectionFixture<ApiFixture>;
