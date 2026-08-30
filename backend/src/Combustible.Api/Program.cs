using System.Threading.RateLimiting;
using Combustible.Api;
using Combustible.Api.Endpoints;
using Combustible.Api.Security;
using Combustible.Infrastructure.Data;
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
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
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
app.MapGet("/", () => Results.Ok(new { servicio = "INTEC Combustible", version = "0.2.0", estado = "dominio y autenticación" }));
app.MapAuth(); app.MapUsers(); app.MapCatalogs();
if (args.Contains("--initialize", StringComparer.Ordinal))
{
    await DatabaseBootstrap.InitializeAsync(app.Services, builder.Configuration);
    return;
}
await app.RunAsync();
public partial class Program;
