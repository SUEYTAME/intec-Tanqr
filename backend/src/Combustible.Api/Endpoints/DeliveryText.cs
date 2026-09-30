using Combustible.Domain;

namespace Combustible.Api.Endpoints;

// El detalle guardado es la respuesta del proveedor (SMTP, Twilio, ruta de bandeja).
// Quien consulta un ticket ve una frase en español, sin códigos, hosts ni identificadores.
public static class DeliveryText
{
    public static string ForPerson(DeliveryChannel channel, DeliveryResult result, string? detail)
    {
        var text = detail ?? string.Empty;
        if (result == DeliveryResult.Outbox)
            return channel == DeliveryChannel.Email
                ? "El correo quedó guardado en la bandeja de pruebas. No llegó a un buzón real."
                : "El SMS quedó guardado en la bandeja de pruebas. No se envió a un teléfono.";

        if (result == DeliveryResult.Sent)
        {
            if (channel == DeliveryChannel.Sms && IsDemo(text))
                return "Se envió un SMS de demostración. No incluye los datos de este ticket.";
            return channel == DeliveryChannel.Email
                ? "El correo fue aceptado y está en camino al destinatario."
                : "El SMS fue aceptado y está en camino al teléfono.";
        }

        return channel == DeliveryChannel.Email ? EmailFailure(text) : SmsFailure(text);
    }

    private static bool IsDemo(string text) =>
        text.Contains("DEMO Trial", StringComparison.OrdinalIgnoreCase)
        || text.Contains("sin datos del ticket", StringComparison.OrdinalIgnoreCase)
        || text.Contains("datos de ejemplo", StringComparison.OrdinalIgnoreCase);

    private static string EmailFailure(string text)
    {
        if (Has(text, "5.1.4", "recipient address rejected", "mailbox unavailable", "user unknown", "invalid address", "domain does not"))
            return "No se pudo enviar el correo: la dirección del destinatario no es válida.";
        if (Has(text, "Authentication", "5.7.8", "535", "credentials", "not authenticated"))
            return "No se pudo enviar el correo: el servidor rechazó el acceso de la aplicación.";
        if (Has(text, "Socket", "connection", "timed out", "timeout", "IOException", "Ssl", "no connection"))
            return "No se pudo enviar el correo: no hubo conexión con el servidor. Puedes reenviar el ticket.";
        return "No se pudo enviar el correo. Puedes reenviar el ticket más tarde.";
    }

    private static string SmsFailure(string text)
    {
        if (Has(text, "E.164"))
            return "No se pudo enviar el SMS: el número debe incluir el código de país, por ejemplo +1 809 555 1234.";
        if (Has(text, "572002", "verified recipient", "trial phone"))
            return "No se pudo enviar el SMS: este número no está autorizado en la cuenta de prueba. Hay que verificarlo con el proveedor o usar una cuenta de producción.";
        if (Has(text, "572006", "predefined", "template"))
            return "No se pudo enviar el SMS: la cuenta de prueba solo admite un mensaje genérico, no el texto del ticket.";
        if (Has(text, "21608", "unverified"))
            return "No se pudo enviar el SMS: el número del destinatario no está verificado en la cuenta de prueba.";
        if (Has(text, "21211", "not a valid phone", "invalid phone"))
            return "No se pudo enviar el SMS: el número de teléfono no es válido.";
        if (Has(text, "expirado", "Trial"))
            return "No se pudo enviar el SMS: el modo de demostración ya venció. Hace falta configurar el envío con los datos del ticket.";
        if (Has(text, "tiempo de espera", "timeout", "TaskCanceled"))
            return "No se pudo enviar el SMS: el proveedor no respondió a tiempo. Puedes reenviar el ticket.";
        if (Has(text, "conexión", "connection", "Socket", "HttpRequest"))
            return "No se pudo enviar el SMS: no hubo conexión con el proveedor. Puedes reenviar el ticket.";
        if (Has(text, "JSON", "campos esperados"))
            return "No se pudo enviar el SMS: el proveedor respondió de una forma inesperada. Puedes reenviar el ticket.";
        return "No se pudo enviar el SMS. Puedes reenviar el ticket más tarde.";
    }

    private static bool Has(string text, params string[] parts) =>
        parts.Any(part => text.Contains(part, StringComparison.OrdinalIgnoreCase));
}
