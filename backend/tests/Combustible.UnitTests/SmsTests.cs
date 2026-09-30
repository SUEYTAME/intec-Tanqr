using System.Net;
using System.Text;
using Combustible.Domain;
using Combustible.Infrastructure.Messaging;

namespace Combustible.UnitTests;

public sealed class SmsTests
{
    private const string Token = "test-token-private";
    private static readonly TwilioSettings Settings = new("AC00000000000000000000000000000000", Token, "+17375550100", null);

    [Theory]
    [InlineData("8095551234", "+18095551234")]
    [InlineData("829-555-1234", "+18295551234")]
    [InlineData("18495551234", "+18495551234")]
    [InlineData("+34 (612) 345-678", "+34612345678")]
    public async Task Aceptacion_envia_formulario_autenticado(string phone, string expected)
    {
        var handler = new Handler(async request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal($"https://api.twilio.com/2010-04-01/Accounts/{Settings.AccountSid}/Messages.json", request.RequestUri!.ToString());
            Assert.Equal("Basic", request.Headers.Authorization!.Scheme);
            Assert.Equal($"{Settings.AccountSid}:{Token}", Encoding.ASCII.GetString(Convert.FromBase64String(request.Headers.Authorization.Parameter!)));
            var form = await request.Content!.ReadAsStringAsync();
            Assert.Contains("To=" + Uri.EscapeDataString(expected), form);
            Assert.Contains("From=%2B17375550100", form);
            Assert.Contains("Body=Ticket", form);
            return Json(HttpStatusCode.Created, "{\"sid\":\"SMtest\",\"status\":\"queued\"}");
        });
        using var client = new HttpClient(handler);
        var result = await new TwilioSmsSender(client, Settings).SendAsync(phone, "Ticket", default);
        Assert.Equal(DeliveryResult.Sent, result.Result);
        Assert.Contains("queued", result.Detail);
    }

    [Fact]
    public async Task Error_registra_codigo_y_oculta_token()
    {
        using var client = new HttpClient(new Handler(_ => Task.FromResult(Json(HttpStatusCode.BadRequest,
            "{\"code\":21608,\"message\":\"unverified " + Token + "\"}"))));
        var report = await new TwilioSmsSender(client, Settings).SendAsync("8095551234", "Ticket", default);
        Assert.Equal(DeliveryResult.Failed, report.Result);
        Assert.Contains("21608", report.Detail);
        Assert.DoesNotContain(Token, report.Detail);
    }

    [Theory]
    [InlineData("5551234")]
    [InlineData("abc8295551234")]
    [InlineData("2345551234")]
    [InlineData("++18095551234")]
    public async Task Movil_invalido_no_hace_peticion(string phone)
    {
        using var client = new HttpClient(new Handler(_ => throw new InvalidOperationException("No debe llamar HTTP")));
        Assert.Equal(DeliveryResult.Failed, (await new TwilioSmsSender(client, Settings).SendAsync(phone, "Ticket", default)).Result);
    }

    [Fact]
    public async Task Messaging_service_sustituye_From()
    {
        using var client = new HttpClient(new Handler(async request =>
        {
            var form = await request.Content!.ReadAsStringAsync();
            Assert.Contains("MessagingServiceSid=MGtest", form);
            Assert.DoesNotContain("From=", form);
            return Json(HttpStatusCode.Created, "{\"sid\":\"SMtest\",\"status\":\"queued\"}");
        }));
        Assert.Equal(DeliveryResult.Sent, (await new TwilioSmsSender(client, Settings with { From = null, MessagingServiceSid = "MGtest" })
            .SendAsync("8095551234", "Ticket", default)).Result);
    }

    [Fact]
    public async Task Timeout_y_red_fallan_cancelacion_del_llamador_se_propaga()
    {
        foreach (var error in new Exception[] { new HttpRequestException("red"), new TaskCanceledException("timeout") })
        {
            using var client = new HttpClient(new Handler(_ => throw error));
            Assert.Equal(DeliveryResult.Failed, (await new TwilioSmsSender(client, Settings).SendAsync("8095551234", "Ticket", default)).Result);
        }
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        using var canceledClient = new HttpClient(new Handler(_ => throw new TaskCanceledException()));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new TwilioSmsSender(canceledClient, Settings)
            .SendAsync("8095551234", "Ticket", canceled.Token));
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status) { Content = new StringContent(body) };
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }
}

[Collection("api")]
public sealed class SmsConfigurationTests(ApiFixture fixture)
{
    [Theory]
    [InlineData("otro", null, null, null, null, "proveedor no implementado")]
    [InlineData("twilio", null, null, null, null, "TWILIO_ACCOUNT_SID")]
    [InlineData("twilio", "ACtest", null, null, null, "TWILIO_AUTH_TOKEN")]
    [InlineData("twilio", "ACtest", "test", null, null, "exactamente uno")]
    [InlineData("twilio", "ACtest", "test", "+17375550100", "MGtest", "exactamente uno")]
    public void Configuracion_incompleta_rechaza_arranque(string provider, string? sid, string? token, string? from, string? service, string expected)
    {
        var values = new Dictionary<string, string?>(fixture.Settings)
        {
            ["SMS_PROVIDER"] = provider, ["TWILIO_ACCOUNT_SID"] = sid, ["TWILIO_AUTH_TOKEN"] = token,
            ["TWILIO_FROM"] = from, ["TWILIO_MESSAGING_SERVICE_SID"] = service
        };
        using var factory = ApiFixture.CreateFactory(values);
        var error = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
        Assert.Contains(expected, error.Message);
    }
}
