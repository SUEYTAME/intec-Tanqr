// Punto de entrada de la API. Fase 0: solo se comprueba que arranca y responde.
// Los endpoints de negocio llegan en la Fase 1 (ver vault/Tareas pendientes.md).

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();

var app = builder.Build();

// RS-03: fuera de desarrollo se exige HTTPS. En desarrollo se permite HTTP para
// no obligar a confiar el certificado antes de poder correr una prueba.
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
    app.UseHsts();
}

app.MapHealthChecks("/health");

app.MapGet("/", () => Results.Ok(new
{
    servicio = "Plataforma de Tickets Digitales de Combustible - INTEC",
    version = "0.1.0-fase0",
    estado = "esqueleto: sin endpoints de negocio todavia"
}));

app.Run();

// Expuesto para que las pruebas de integracion puedan arrancar la aplicacion
// con WebApplicationFactory. Sin esto, la clase generada es internal.
public partial class Program;
