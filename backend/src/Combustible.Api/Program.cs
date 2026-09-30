using System.Globalization;
using System.Security.Cryptography;
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
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.IdentityModel.Tokens;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(TransportSecurity.Configure);
var connection = builder.Configuration.GetConnectionString("Database")
    ?? throw new InvalidOperationException("Falta ConnectionStrings:Database. Usa scripts/iniciar.ps1.");
var signingKey = Convert.FromBase64String(builder.Configuration["JWT_SIGNING_KEY"]
    ?? throw new InvalidOperationException("Falta JWT_SIGNING_KEY en base64."));
if (signingKey.Length < 32) throw new InvalidOperationException("JWT_SIGNING_KEY requiere al menos 32 bytes.");
var jwt = new JwtSettings(builder.Configuration["JWT_ISSUER"] ?? "intec-combustible",
    builder.Configuration["JWT_AUDIENCE"] ?? "intec-combustible", signingKey);
builder.Services.AddSingleton(jwt);
builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseNpgsql(connection).ReplaceService<IModelCacheKeyFactory, ProtectorModelCacheKeyFactory>();
    options.UseOpenIddict();
});
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
var smsProvider = builder.Configuration["SMS_PROVIDER"];
if (string.IsNullOrWhiteSpace(smsProvider))
    builder.Services.AddSingleton<ISmsSender>(new OutboxSmsSender(outbox));
else if (string.Equals(smsProvider, "twilio", StringComparison.OrdinalIgnoreCase))
{
    string RequiredSmsSetting(string key) => !string.IsNullOrWhiteSpace(builder.Configuration[key])
        ? builder.Configuration[key]! : throw new InvalidOperationException($"Falta {key} para SMS_PROVIDER=twilio.");
    var accountSid = RequiredSmsSetting("TWILIO_ACCOUNT_SID");
    var authToken = RequiredSmsSetting("TWILIO_AUTH_TOKEN");
    var twilioFrom = builder.Configuration["TWILIO_FROM"];
    var serviceSid = builder.Configuration["TWILIO_MESSAGING_SERVICE_SID"];
    if (string.IsNullOrWhiteSpace(twilioFrom) == string.IsNullOrWhiteSpace(serviceSid))
        throw new InvalidOperationException("Twilio requiere exactamente uno de TWILIO_FROM o TWILIO_MESSAGING_SERVICE_SID.");
    var trialTemplate = builder.Configuration["TWILIO_TRIAL_TEMPLATE"];
    var trialUntilText = builder.Configuration["TWILIO_TRIAL_UNTIL"];
    DateOnly? trialUntil = null;
    if (!string.IsNullOrWhiteSpace(trialTemplate) || !string.IsNullOrWhiteSpace(trialUntilText))
    {
        if (trialTemplate != "sms_order_confirmation" ||
            !DateOnly.TryParseExact(trialUntilText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var expiry))
            throw new InvalidOperationException("Demo Twilio requiere TWILIO_TRIAL_TEMPLATE=sms_order_confirmation y TWILIO_TRIAL_UNTIL=AAAA-MM-DD.");
        trialUntil = expiry;
    }
    else trialTemplate = null;
    builder.Services.AddSingleton<ISmsSender>(_ => new TwilioSmsSender(new HttpClient(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5)
    }) { Timeout = TimeSpan.FromSeconds(15) }, new TwilioSettings(accountSid, authToken, twilioFrom, serviceSid, trialTemplate, trialUntil)));
}
else throw new InvalidOperationException("SMS_PROVIDER: proveedor no implementado.");
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
    .AddSignInManager().AddDefaultTokenProviders().AddErrorDescriber<SpanishIdentityErrors>().AddPasswordValidator<PassphraseValidator>();
builder.Services.Configure<PasswordHasherOptions>(options => options.IterationCount = 600000);
// RS-05: servidor OAuth 2.0 (client credentials, RFC 6749 §4.4) que emite JWT de acceso (RFC 9068)
// para sistemas que consumen la API (RF-24). Clave propia: una fuga no permite falsificar QR.
using var oauthKey = ECDsa.Create();
oauthKey.ImportFromPem(Encoding.UTF8.GetString(Convert.FromBase64String(builder.Configuration["OAUTH_SIGNING_KEY_B64"]
    ?? throw new InvalidOperationException("Falta OAUTH_SIGNING_KEY_B64 (clave ECDSA P-256 PEM en base64). Usa scripts/iniciar.ps1."))));
var oauthSecurityKey = new ECDsaSecurityKey(ECDsa.Create(oauthKey.ExportParameters(true)));
builder.Services.AddOpenIddict()
    .AddCore(options => options.UseEntityFrameworkCore().UseDbContext<AppDbContext>())
    .AddServer(options =>
    {
        options.SetTokenEndpointUris("/connect/token");
        options.AllowClientCredentialsFlow();
        options.RegisterScopes(OAuthClients.Scope);
        options.AddSigningKey(oauthSecurityKey);
        // Ningún token cifrado se emite (solo acceso JWT firmado); la clave efímera satisface el requisito del servidor.
        options.AddEphemeralEncryptionKey();
        options.DisableAccessTokenEncryption();
        options.SetAccessTokenLifetime(TimeSpan.FromMinutes(15));
        var aspNet = options.UseAspNetCore().EnableTokenEndpointPassthrough();
        if (builder.Environment.IsDevelopment()) aspNet.DisableTransportSecurityRequirement();
    })
    .AddValidation(options =>
    {
        options.UseLocalServer();
        options.UseAspNetCore();
        // Revocar o borrar el cliente invalida sus tokens vigentes.
        options.EnableTokenEntryValidation();
    });
builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = "bearer";
    options.DefaultChallengeScheme = "bearer";
}).AddPolicyScheme("bearer", "JWT de sesión u OAuth 2.0", options => options.ForwardDefaultSelector = OAuthClients.SelectScheme)
.AddJwtBearer(options =>
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
    // ADR-016: escribir catálogos, aprobar/anular e inventario exigen una persona con sesión (claim sid);
    // un cliente OAuth, aunque tenga rol Supervisor, solo consulta y crea solicitudes.
    options.AddPolicy("catalog-write", p => p.RequireRole(Roles.Administrator, Roles.Supervisor).RequireClaim("sid"));
    options.AddPolicy("audit-read", p => p.RequireRole(Roles.Administrator, Roles.Auditor));
    // Actores del SRS §3: Consulta hace de solicitante; Supervisor aprueba, recibe y ajusta;
    // Despachador despacha y cierra; Auditor consulta y exporta.
    options.AddPolicy("request-create", p => p.RequireRole(Roles.Administrator, Roles.Supervisor, Roles.Viewer));
    options.AddPolicy("request-approve", p => p.RequireRole(Roles.Administrator, Roles.Supervisor).RequireClaim("sid"));
    options.AddPolicy("inventory-write", p => p.RequireRole(Roles.Administrator, Roles.Supervisor).RequireClaim("sid"));
    // Datos personales del empleado (cédula, correo, móvil): solo Administrador y Supervisor con sesión.
    options.AddPolicy("employee-pii", p => p.RequireRole(Roles.Administrator, Roles.Supervisor).RequireClaim("sid"));
    // Despacho y cierre: solo Despachador con sesión (SRS §3.3). H-05 pide confirmar la cédula en persona,
    // y quien aprueba tickets no los despacha (separación de funciones, ADR-016).
    options.AddPolicy("dispatch", p => p.RequireRole(Roles.Dispatcher).RequireClaim("sid"));
    options.AddPolicy("close", p => p.RequireRole(Roles.Dispatcher).RequireClaim("sid"));
    options.AddPolicy("user", p => p.RequireAuthenticatedUser().RequireClaim("sid"));
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
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers.XFrameOptions = "DENY";
    // Interfaz servida desde este mismo origen (WEB_ROOT): todo 'self'. 'wasm-unsafe-eval' es para el
    // lector de QR (ZXing en WebAssembly); blob: para las descargas y vistas de PDF/QR.
    if (!context.Request.Path.StartsWithSegments("/api"))
        context.Response.Headers.ContentSecurityPolicy = "default-src 'self'; script-src 'self' 'wasm-unsafe-eval'; " +
            "img-src 'self' blob: data:; style-src 'self'; connect-src 'self'; worker-src 'self'; manifest-src 'self'; " +
            "object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";
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
var webRoot = builder.Configuration["WEB_ROOT"];
// Con WEB_ROOT, "/" es la interfaz; sin él, la raíz describe el servicio.
if (string.IsNullOrEmpty(webRoot))
    app.MapGet("/", () => Results.Ok(new { servicio = "INTEC Combustible", version = "0.4.0", estado = "tickets, inventario y despacho" }));
app.MapOpenApi();
app.MapAuth(); app.MapUsers(); app.MapCatalogs(); app.MapTickets(); app.MapInventory(); app.MapReports(); app.MapOAuth();
// Despliegue de un solo origen: Kestrel (TLS 1.3) sirve también la interfaz compilada. Sin proxy
// inverso, la IP del cliente que ven el límite de tasa y la auditoría es la real.
if (!string.IsNullOrEmpty(webRoot))
{
    var root = Path.GetFullPath(webRoot);
    if (!File.Exists(Path.Combine(root, "index.html"))) throw new InvalidOperationException($"WEB_ROOT no contiene index.html: {root}");
    var files = new StaticFileOptions { FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(root) };
    app.UseStaticFiles(files);
    // Rutas del SPA (p. ej. /ticket/<clave>) → index.html; /api, /connect y /health nunca caen aquí.
    // No se usa :nonfile porque la clave del ticket público lleva punto (<id>.<token>); se excluyen
    // las extensiones reales para que un archivo inexistente dé 404 y no index.html.
    app.MapFallbackToFile("/", "index.html", files);
    app.MapFallbackToFile(@"{*path:regex(^(?!api/|api$|connect/|health)(?!.*\.(js|css|png|svg|ico|wasm|webmanifest|json|map|txt)$).+$)}",
        "index.html", files);
}
if (args.Contains("--initialize", StringComparer.Ordinal))
{
    await DatabaseBootstrap.InitializeAsync(app.Services, builder.Configuration);
    return;
}
await app.RunAsync();
public partial class Program;
