using System.Net;

namespace Combustible.UnitTests;

// Fase 0: la unica afirmacion que este esqueleto puede sostener es que la
// aplicacion arranca de verdad y responde. Se prueba levantandola en memoria,
// no comprobando que el codigo "parece" correcto.
[Collection("api")]
public sealed class SaludDeLaApiTests
{
    private readonly ApiFixture _fixture;

    public SaludDeLaApiTests(ApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task El_endpoint_de_salud_responde_200()
    {
        using var cliente = _fixture.Factory.CreateClient();

        var respuesta = await cliente.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal("Healthy", await respuesta.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task La_raiz_identifica_el_servicio()
    {
        using var cliente = _fixture.Factory.CreateClient();

        var respuesta = await cliente.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Contains("INTEC", await respuesta.Content.ReadAsStringAsync());
    }
}
