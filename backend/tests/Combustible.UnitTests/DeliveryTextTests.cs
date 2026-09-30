using Combustible.Api.Endpoints;
using Combustible.Api.Security;
using Combustible.Domain;

namespace Combustible.UnitTests;

public sealed class DeliveryTextTests
{
    [Theory]
    [InlineData(DeliveryChannel.Email, DeliveryResult.Failed, "SMTP smtp.azurecomm.net: SmtpCommandException: 5.1.4 Recipient address reserved by RFC 2606. For more information see https://aka.ms/EXOSmtpErrors.", "dirección del destinatario")]
    [InlineData(DeliveryChannel.Sms, DeliveryResult.Failed, "Twilio HTTP 422: 572002: No Twilio trial phone number is assigned for messaging to this destination number. Please add the 'to' number as a verified recipient.", "no está autorizado")]
    [InlineData(DeliveryChannel.Email, DeliveryResult.Sent, "SMTP smtp.azurecomm.net: 2.6.0 7b88a35c-24c6-49cd-995e-c0fa7a0f44f9 Queued mail for delivery", "en camino al destinatario")]
    public void Los_textos_del_proveedor_que_ve_la_persona_no_se_muestran(DeliveryChannel channel, DeliveryResult result, string raw, string fragment)
    {
        var text = DeliveryText.ForPerson(channel, result, raw);
        Assert.Contains(fragment, text, StringComparison.Ordinal);
        Assert.DoesNotContain("SMTP", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Twilio", text, StringComparison.Ordinal);
        Assert.DoesNotContain("azurecomm", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("aka.ms", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("572002", text, StringComparison.Ordinal);
        Assert.DoesNotContain("7b88a35c", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Una_frase_ya_clara_no_se_reescribe()
    {
        const string clear = "No se pudo enviar el SMS: este número no está autorizado en la cuenta de prueba. Hay que verificarlo con el proveedor o usar una cuenta de producción.";
        Assert.Equal(clear, DeliveryText.ForPerson(DeliveryChannel.Sms, DeliveryResult.Failed, clear));
    }

    [Fact]
    public void Correo_aceptado_no_muestra_la_respuesta_SMTP()
    {
        var raw = "SMTP smtp.azurecomm.net: 2.6.0 7b88a35c-24c6-49cd-995e-c0fa7a0f44f9 Queued mail for delivery";
        var text = DeliveryText.ForPerson(DeliveryChannel.Email, DeliveryResult.Sent, raw);
        Assert.Equal("El correo fue aceptado y está en camino al destinatario.", text);
        Assert.DoesNotContain("smtp.azurecomm.net", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("7b88a35c", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Queued", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Sms_de_prueba_explica_el_numero_no_autorizado()
    {
        var raw = "Twilio HTTP 422: 572002: No Twilio trial phone number is assigned for messaging to this destination number. Please add the 'to' number as a verified recipient.";
        var text = DeliveryText.ForPerson(DeliveryChannel.Sms, DeliveryResult.Failed, raw);
        Assert.Equal("No se pudo enviar el SMS: este número no está autorizado en la cuenta de prueba. Hay que verificarlo con el proveedor o usar una cuenta de producción.", text);
        Assert.DoesNotContain("572002", text, StringComparison.Ordinal);
        Assert.DoesNotContain("HTTP", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Twilio", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(DeliveryChannel.Email, "Bandeja local: /tmp/combustible/a.eml", "El correo quedó guardado en la bandeja de pruebas. No llegó a un buzón real.")]
    [InlineData(DeliveryChannel.Sms, "Bandeja local SMS (sin pasarela, B-01): /tmp/a.sms.txt", "El SMS quedó guardado en la bandeja de pruebas. No se envió a un teléfono.")]
    public void Bandeja_local_no_muestra_la_ruta(DeliveryChannel channel, string raw, string expected)
    {
        var text = DeliveryText.ForPerson(channel, DeliveryResult.Outbox, raw);
        Assert.Equal(expected, text);
        Assert.DoesNotContain("/tmp", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Sms_de_demostracion_avisa_que_no_lleva_datos_del_ticket()
    {
        var raw = "Twilio SMceb: queued; DEMO Trial: confirmación genérica con datos de ejemplo Twilio, sin datos del ticket.";
        var text = DeliveryText.ForPerson(DeliveryChannel.Sms, DeliveryResult.Sent, raw);
        Assert.Equal("Se envió un SMS de demostración. No incluye los datos de este ticket.", text);
        Assert.DoesNotContain("SMceb", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Móvil no está en formato internacional E.164; use +código de país y número.", "código de país")]
    [InlineData("Twilio HTTP 400: 21608: unverified secret-token", "no está verificado")]
    [InlineData("Twilio HTTP 400: 572006: Trial accounts can only send pre-approved templates", "mensaje genérico")]
    [InlineData("Twilio: modo de demostración Trial expirado; configure SMS personalizado para operación real.", "ya venció")]
    [InlineData("Twilio: tiempo de espera agotado.", "no respondió a tiempo")]
    [InlineData("Twilio: error de conexión HTTP.", "no hubo conexión")]
    [InlineData("Twilio: respuesta JSON inválida.", "forma inesperada")]
    [InlineData("Twilio HTTP 500: 30001: carrier rejected the message", "reenviar el ticket más tarde")]
    public void Fallos_de_sms_se_leen_sin_codigos(string raw, string fragment)
    {
        var text = DeliveryText.ForPerson(DeliveryChannel.Sms, DeliveryResult.Failed, raw);
        Assert.Contains(fragment, text, StringComparison.Ordinal);
        Assert.DoesNotContain("Twilio", text, StringComparison.Ordinal);
        Assert.DoesNotContain("HTTP", text, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-token", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("SMTP smtp.example: SmtpCommandException: 5.1.4 Recipient address rejected", "dirección del destinatario")]
    [InlineData("SMTP smtp.example: AuthenticationException: 535 Authentication failed", "rechazó el acceso")]
    [InlineData("SMTP 127.0.0.1: SocketException: Connection refused", "no hubo conexión")]
    [InlineData("SMTP smtp.example: SmtpCommandException: 550 relay denied", "reenviar el ticket más tarde")]
    public void Fallos_de_correo_se_leen_sin_el_servidor(string raw, string fragment)
    {
        var text = DeliveryText.ForPerson(DeliveryChannel.Email, DeliveryResult.Failed, raw);
        Assert.Contains(fragment, text, StringComparison.Ordinal);
        Assert.DoesNotContain("smtp.example", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("127.0.0.1", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", text, StringComparison.Ordinal);
    }
}

public sealed class ValidationTextTests
{
    [Theory]
    [InlineData("Email", "The Email field is not a valid e-mail address.", "El correo no tiene un formato válido.")]
    [InlineData("Mobile", "The Mobile field is not a valid phone number.", "El teléfono no tiene un formato válido.")]
    [InlineData("Code", "The Code field is required.", "Completa el código.")]
    [InlineData("NationalId", "The field NationalId must match the regular expression '^[0-9]{11}$'.", "La cédula no tiene el formato esperado.")]
    [InlineData("Name", "El campo Name debe ser una cadena con una longitud máxima de '150'.", "El nombre no tiene una longitud válida.")]
    [InlineData("Year", "The field Year must be between 1900 and 2200.", "El año está fuera del rango permitido.")]
    [InlineData("Reason", "Indica el motivo del rechazo (mínimo 5 caracteres).", "Indica el motivo del rechazo (mínimo 5 caracteres).")]
    public void Validacion_se_lee_en_espanol(string member, string raw, string expected) =>
        Assert.Equal(expected, ValidationText.ForPerson(member, raw));

    [Fact]
    public void Cuenta_duplicada_no_usa_el_texto_de_Identity()
    {
        var error = new SpanishIdentityErrors().DuplicateEmail("admin@example.test");
        Assert.Equal("Ya existe una cuenta con ese correo.", error.Description);
        Assert.DoesNotContain("admin@example.test", error.Description, StringComparison.Ordinal);
        Assert.DoesNotContain("already taken", error.Description, StringComparison.OrdinalIgnoreCase);
    }
}
