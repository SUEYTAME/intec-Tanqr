using Combustible.Api.Endpoints;
using Combustible.Domain;
using Combustible.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Combustible.Api;

public static class DatabaseBootstrap
{
    public static async Task InitializeAsync(IServiceProvider services, IConfiguration configuration)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
        var password = configuration["APP_DB_PASSWORD"] ?? throw new InvalidOperationException("Falta APP_DB_PASSWORD.");
        await ConfigureApplicationRoleAsync(db.Database.GetConnectionString()!, password);
        await using var tx = await db.Database.BeginTransactionAsync();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        foreach (var role in Roles.All)
            if (!await roles.RoleExistsAsync(role)) UserEndpoints.Ensure(await roles.CreateAsync(new IdentityRole<Guid>(role)));
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var email = configuration["BOOTSTRAP_EMAIL"] ?? throw new InvalidOperationException("Falta BOOTSTRAP_EMAIL.");
        if (!await users.Users.AnyAsync())
        {
            var user = new AppUser { Email = email, UserName = email, DisplayName = "Administrador local" };
            UserEndpoints.Ensure(await users.CreateAsync(user, configuration["BOOTSTRAP_PASSWORD"] ?? throw new InvalidOperationException("Falta BOOTSTRAP_PASSWORD.")));
            UserEndpoints.Ensure(await users.AddToRoleAsync(user, Roles.Administrator));
            await scope.ServiceProvider.GetRequiredService<AuditWriter>().WriteAsync("bootstrap", "local", "initialize", "User", user.Id.ToString());
        }
        await tx.CommitAsync();
    }

    public static async Task ConfigureApplicationRoleAsync(string connectionString, string password)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        // format(%L) is evaluated by PostgreSQL and quotes the password as a SQL literal.
        await using var formatter = new NpgsqlCommand("SELECT format(CASE WHEN EXISTS (SELECT FROM pg_roles WHERE rolname = 'combustible_app') THEN 'ALTER ROLE combustible_app LOGIN PASSWORD %L' ELSE 'CREATE ROLE combustible_app LOGIN PASSWORD %L' END, @password)", connection);
        formatter.Parameters.AddWithValue("password", password);
        var sql = (string)(await formatter.ExecuteScalarAsync())!;
        await using var create = new NpgsqlCommand(sql, connection);
        await create.ExecuteNonQueryAsync();
        await using var grantDatabaseFormatter = new NpgsqlCommand("SELECT format('GRANT CONNECT ON DATABASE %I TO combustible_app', current_database())", connection);
        var grantDatabaseSql = (string)(await grantDatabaseFormatter.ExecuteScalarAsync())!;
        await using var grantDatabase = new NpgsqlCommand(grantDatabaseSql, connection);
        await grantDatabase.ExecuteNonQueryAsync();
        const string grants = """
            GRANT USAGE ON SCHEMA public TO combustible_app;
            GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO combustible_app;
            GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO combustible_app;
            REVOKE ALL ON TABLE "AuditEvents" FROM combustible_app;
            GRANT SELECT, INSERT ON TABLE "AuditEvents" TO combustible_app;
            REVOKE ALL ON TABLE "__EFMigrationsHistory" FROM combustible_app;
            """;
        await using var grant = new NpgsqlCommand(grants, connection);
        await grant.ExecuteNonQueryAsync();
    }
}
