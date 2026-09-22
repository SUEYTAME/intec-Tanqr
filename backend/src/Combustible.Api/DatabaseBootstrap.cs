using Combustible.Api.Endpoints;
using Combustible.Domain;
using System.Globalization;
using Combustible.Infrastructure.Data;
using Combustible.Infrastructure.Security;
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
        var encrypted = await EncryptEmployeesAsync(db.Database.GetConnectionString()!, scope.ServiceProvider.GetRequiredService<FieldProtector>());
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
        if (encrypted > 0)
            await scope.ServiceProvider.GetRequiredService<AuditWriter>().WriteAsync("bootstrap", "local", "encrypt_backfill", "Employee", encrypted.ToString(CultureInfo.InvariantCulture));
        await tx.CommitAsync();
    }

    // RS-03: filas anteriores al cifrado. Se leen con ADO (EF ya espera texto cifrado) y se cifran en una transacción.
    public static async Task<int> EncryptEmployeesAsync(string connectionString, FieldProtector protector)
    {
        ArgumentNullException.ThrowIfNull(protector);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var tx = await connection.BeginTransactionAsync();
        var pending = new List<(Guid Id, string NationalId, string Email, string Mobile)>();
        await using (var select = new NpgsqlCommand(
            "SELECT \"Id\", \"NationalId\", \"Email\", \"Mobile\" FROM \"Employees\" WHERE \"NationalIdHash\" IS NULL FOR UPDATE", connection, tx))
        await using (var reader = await select.ExecuteReaderAsync())
            while (await reader.ReadAsync()) pending.Add((reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
        foreach (var (id, nationalId, email, mobile) in pending)
        {
            if (FieldProtector.IsProtected(nationalId)) throw new InvalidOperationException($"Empleado {id}: cédula cifrada sin índice ciego; revisar manualmente.");
            string Protect(string value, string purpose) => FieldProtector.IsProtected(value) ? value : protector.Protect(value, purpose);
            await using var update = new NpgsqlCommand(
                "UPDATE \"Employees\" SET \"NationalId\" = @n, \"Email\" = @e, \"Mobile\" = @m, \"NationalIdHash\" = @h WHERE \"Id\" = @id", connection, tx);
            update.Parameters.AddWithValue("n", Protect(nationalId, AppDbContext.NationalIdPurpose));
            update.Parameters.AddWithValue("e", Protect(email, AppDbContext.EmailPurpose));
            update.Parameters.AddWithValue("m", Protect(mobile, AppDbContext.MobilePurpose));
            update.Parameters.AddWithValue("h", protector.BlindIndex(nationalId, AppDbContext.NationalIdPurpose));
            update.Parameters.AddWithValue("id", id);
            await update.ExecuteNonQueryAsync();
        }
        await tx.CommitAsync();
        return pending.Count;
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
            REVOKE ALL ON TABLE "InventoryMovements", "Dispatches", "DailyCloses", "DailyCloseLines", "TicketDeliveries",
                "FuelReceipts", "Notifications", "NotificationReads" FROM combustible_app;
            GRANT SELECT, INSERT ON TABLE "InventoryMovements", "Dispatches", "DailyCloses", "DailyCloseLines", "TicketDeliveries",
                "FuelReceipts", "Notifications", "NotificationReads" TO combustible_app;
            REVOKE ALL ON TABLE "Tickets" FROM combustible_app;
            GRANT SELECT, INSERT ON TABLE "Tickets" TO combustible_app;
            GRANT UPDATE ("Status", "ConsumedAt", "VoidedAt", "VoidReason", "Version") ON TABLE "Tickets" TO combustible_app;
            """;
        await using var grant = new NpgsqlCommand(grants, connection);
        await grant.ExecuteNonQueryAsync();
    }
}
