using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
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
    private readonly string _appPassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private readonly string _key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    public string Password { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
    public WebApplicationFactory<Program> Factory { get; private set; } = null!;
    public string ApplicationConnection { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        var values = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Database"] = _postgres.GetConnectionString(), ["JWT_SIGNING_KEY"] = _key,
            ["APP_DB_PASSWORD"] = _appPassword, ["BOOTSTRAP_EMAIL"] = "admin@localhost.test",
            ["BOOTSTRAP_PASSWORD"] = Password, ["Logging:LogLevel:Default"] = "Warning"
        };
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

    private static WebApplicationFactory<Program> CreateFactory(Dictionary<string, string?> values) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            foreach (var pair in values) builder.UseSetting(pair.Key, pair.Value);
        });

    public async Task<(HttpClient Client, SessionResponse Session)> LoginAsync(string? email = null, string? password = null)
    {
        var client = Factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email = email ?? "admin@localhost.test", password = password ?? Password });
        response.EnsureSuccessStatusCode();
        var session = (await response.Content.ReadFromJsonAsync<SessionResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        return (client, session);
    }

    public async Task DisposeAsync()
    {
        if (Factory is not null) await Factory.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}

[CollectionDefinition("api")]
public sealed class ApiTestGroup : ICollectionFixture<ApiFixture>;
