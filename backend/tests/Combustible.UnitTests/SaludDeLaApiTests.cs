using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Combustible.UnitTests;

// Fase 0: la unica afirmacion que este esqueleto puede sostener es que la
// aplicacion arranca de verdad y responde. Se prueba levantandola en memoria,
// no comprobando que el codigo "parece" correcto.
public sealed class SaludDeLaApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _fabrica;

    public SaludDeLaApiTests(WebApplicationFactory<Program> fabrica) => _fabrica = fabrica;

    [Fact]
    public async Task El_endpoint_de_salud_responde_200()
    {
        var cliente = _fabrica.CreateClient();

        var respuesta = await cliente.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal("Healthy", await respuesta.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task La_raiz_identifica_el_servicio()
    {
        var cliente = _fabrica.CreateClient();

        var respuesta = await cliente.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Contains("INTEC", await respuesta.Content.ReadAsStringAsync());
    }
}
