using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Combustible.Api;
using Combustible.Api.Endpoints;
using Combustible.Api.Security;
using Combustible.Api.Tickets;
using Combustible.Application;
using Combustible.Infrastructure.Data;
using Combustible.Infrastructure.Messaging;
using Combustible.Infrastructure.Security;
using Combustible.Domain;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
var connection = builder.Configuration.GetConnectionString("Database")
    ?? throw new InvalidOperationException("Falta ConnectionStrings:Database. Usa scripts/iniciar.ps1.");
var signingKey = Convert.FromBase64String(builder.Configuration["JWT_SIGNING_KEY"]
    ?? throw new InvalidOperationException("Falta JWT_SIGNING_KEY en base64."));
if (signingKey.Length < 32) throw new InvalidOperationException("JWT_SIGNING_KEY requiere al menos 32 bytes.");
var jwt = new JwtSettings(builder.Configuration["JWT_ISSUER"] ?? "intec-combustible",
    builder.Configuration["JWT_AUDIENCE"] ?? "intec-combustible", signingKey);
builder.Services.AddSingleton(jwt);
builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connection));
builder.Services.AddScoped<AuditWriter>();
builder.Services.AddScoped<SessionService>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
// RS-03 (reposo) y RS-04: claves obligatorias; sin valores por defecto.
builder.Services.AddSingleton(new FieldProtector(Convert.FromBase64String(builder.Configuration["DATA_ENCRYPTION_KEY"]
    ?? throw new InvalidOperationException("Falta DATA_ENCRYPTION_KEY (32 bytes en base64). Usa scripts/iniciar.ps1."))));
builder.Services.AddSingleton(new TicketSigner(Encoding.UTF8.GetString(Convert.FromBase64String(builder.Configuration["QR_SIGNING_KEY_B64"]
    ?? throw new InvalidOperationException("Falta QR_SIGNING_KEY_B64 (clave ECDSA P-256 PEM en base64). Usa scripts/iniciar.ps1.")))));
builder.Services.AddSingleton(new PublicLinks(builder.Configuration["PUBLIC_BASE_URL"] ?? "http://localhost:5173"));
var outbox = Path.GetFullPath(builder.Configuration["OUTBOX_DIR"] ?? Path.Combine("artifacts", "outbox"));
var mailFrom = builder.Configuration["SMTP_FROM"] is { Length: > 0 } from ? from : "combustible@localhost.test";
if (builder.Configuration["SMTP_HOST"] is { Length: > 0 } smtpHost)
{
    var smtpPort = int.Parse(builder.Configuration["SMTP_PORT"] ?? throw new InvalidOperationException("Falta SMTP_PORT."), CultureInfo.InvariantCulture);
    builder.Services.AddSingleton<IEmailSender>(new SmtpEmailSender(new SmtpSettings(smtpHost, smtpPort, builder.Configuration["SMTP_USER"],
        builder.Configuration["SMTP_PASSWORD"], mailFrom, !string.Equals(builder.Configuration["SMTP_REQUIRE_TLS"], "false", StringComparison.OrdinalIgnoreCase))));
}
else builder.Services.AddSingleton<IEmailSender>(new OutboxEmailSender(outbox, mailFrom));
// B-01: no hay pasarela SMS. Configurar un proveedor sin implementación es un error, no un envío simulado.
if (builder.Configuration["SMS_PROVIDER"] is { Length: > 0 } smsProvider)
    throw new InvalidOperationException($"SMS_PROVIDER={smsProvider} no tiene implementación (bloqueo B-01). Déjalo vacío para usar la bandeja local.");
builder.Services.AddSingleton<ISmsSender>(new OutboxSmsSender(outbox));
builder.Services.AddScoped<TicketService>();
builder.Services.AddSingleton<LifecycleService>();
if (builder.Configuration.GetValue("Jobs:Enabled", true)) builder.Services.AddHostedService<LifecycleWorker>();
builder.Services.AddOpenApi();
builder.Services.AddDataProtection();
builder.Services.AddIdentityCore<AppUser>(options =>
{
    options.User.RequireUniqueEmail = true;
    options.Password.RequiredLength = 1;
    options.Password.RequireDigit = false; options.Password.RequireLowercase = false;
    options.Password.RequireUppercase = false; options.Password.RequireNonAlphanumeric = false;
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
}).AddRoles<IdentityRole<Guid>>().AddEntityFrameworkStores<AppDbContext>()
    .AddSignInManager().AddDefaultTokenProviders().AddPasswordValidator<PassphraseValidator>();
builder.Services.Configure<PasswordHasherOptions>(options => options.IterationCount = 600000);
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.MapInboundClaims = false;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true, ValidateAudience = true, ValidateLifetime = true, ValidateIssuerSigningKey = true,
        ValidIssuer = jwt.Issuer, ValidAudience = jwt.Audience, IssuerSigningKey = new SymmetricSecurityKey(jwt.Key),
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256], ClockSkew = TimeSpan.FromSeconds(15),
        NameClaimType = "sub", RoleClaimType = "role"
    };
    options.Events = new JwtBearerEvents
    {
        OnTokenValidated = async context =>
        {
            var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
            if (!Guid.TryParse(context.Principal?.FindFirst("sid")?.Value, out var sid)
                || !Guid.TryParse(context.Principal?.FindFirst("sub")?.Value, out var uid)) { context.Fail("Sesión inválida."); return; }
            var stamp = context.Principal!.FindFirst("stamp")?.Value;
            if (!await db.Sessions.AnyAsync(s => s.Id == sid && s.UserId == uid && !s.Revoked && s.ExpiresAt > DateTimeOffset.UtcNow && s.SecurityStamp == stamp)
                || !await db.Users.AnyAsync(u => u.Id == uid && u.Active && u.SecurityStamp == stamp)) context.Fail("Sesión revocada.");
        }
    };
});
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("admin", p => p.RequireRole(Roles.Administrator));
    options.AddPolicy("catalog-write", p => p.RequireRole(Roles.Administrator, Roles.Supervisor));
    options.AddPolicy("audit-read", p => p.RequireRole(Roles.Administrator, Roles.Auditor));
    // Actores del SRS §3: Consulta hace de solicitante; Supervisor aprueba, recibe y ajusta;
    // Despachador despacha y cierra; Auditor consulta y exporta.
    options.AddPolicy("request-create", p => p.RequireRole(Roles.Administrator, Roles.Supervisor, Roles.Viewer));
    options.AddPolicy("request-approve", p => p.RequireRole(Roles.Administrator, Roles.Supervisor));
    options.AddPolicy("inventory-write", p => p.RequireRole(Roles.Administrator, Roles.Supervisor));
    options.AddPolicy("dispatch", p => p.RequireRole(Roles.Dispatcher, Roles.Supervisor));
    options.AddPolicy("close", p => p.RequireRole(Roles.Dispatcher, Roles.Supervisor));
    options.AddPolicy("reports", p => p.RequireRole(Roles.Administrator, Roles.Supervisor, Roles.Auditor, Roles.Viewer));
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("public", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();
var app = builder.Build();
app.UseExceptionHandler();
app.Use(async (context, next) =>
{
    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers.XContentTypeOptions = "nosniff";
    try { await next(context); }
    catch (DbUpdateConcurrencyException) { await Results.Problem(statusCode: 412, title: "El registro cambió; vuelve a cargarlo.").ExecuteAsync(context); }
    catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: "23505" })
    { await Results.Problem(statusCode: 409, title: "Ya existe un registro con esos identificadores.").ExecuteAsync(context); }
    catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: "23503" or "23514" })
    { await Results.Problem(statusCode: 422, title: "La operación incumple una relación o regla de datos.").ExecuteAsync(context); }
});
if (!app.Environment.IsDevelopment()) { app.UseHttpsRedirection(); app.UseHsts(); }
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapHealthChecks("/health");
app.MapGet("/health/ready", async (AppDbContext db) =>
    await db.Database.CanConnectAsync() ? Results.Ok(new { status = "ready" }) : Results.StatusCode(503));
app.MapGet("/", () => Results.Ok(new { servicio = "INTEC Combustible", version = "0.3.0", estado = "tickets, inventario y despacho" }));
app.MapOpenApi();
app.MapAuth(); app.MapUsers(); app.MapCatalogs(); app.MapTickets(); app.MapInventory(); app.MapReports();
if (args.Contains("--initialize", StringComparer.Ordinal))
{
    await DatabaseBootstrap.InitializeAsync(app.Services, builder.Configuration);
    return;
}
await app.RunAsync();
public partial class Program;
