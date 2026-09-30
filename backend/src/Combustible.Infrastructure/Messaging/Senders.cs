using System.Globalization;
using System.Net.Sockets;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Combustible.Application;
using Combustible.Domain;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace Combustible.Infrastructure.Messaging;

public sealed record SmtpSettings(string Host, int Port, string? User, string? Password, string From, bool RequireTls);

public sealed class SmtpEmailSender(SmtpSettings settings) : IEmailSender
{
    public async Task<DeliveryReport> SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var mime = MailFactory.Build(settings.From, message);
        using var client = new SmtpClient();
        try
        {
            await client.ConnectAsync(settings.Host, settings.Port,
                settings.RequireTls ? SecureSocketOptions.StartTls : SecureSocketOptions.StartTlsWhenAvailable, cancellationToken);
            if (!string.IsNullOrEmpty(settings.User))
                await client.AuthenticateAsync(settings.User, settings.Password ?? string.Empty, cancellationToken);
            var response = await client.SendAsync(mime, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);
            return new DeliveryReport(DeliveryResult.Sent, Truncate($"SMTP {settings.Host}: {response}"));
        }
        // Fallos de la integración: se registran como Failed, se notifican (RF-23) y el ticket
        // queda Pending para reenvío. No se reportan como éxito.
        catch (Exception e) when (e is SmtpCommandException or SmtpProtocolException or ServiceNotConnectedException
            or ServiceNotAuthenticatedException or MailKit.Security.AuthenticationException
            or System.Security.Authentication.AuthenticationException or SocketException or IOException or SslHandshakeException)
        {
            return new DeliveryReport(DeliveryResult.Failed, Truncate($"SMTP {settings.Host}: {e.GetType().Name}: {e.Message}"));
        }
    }

    private static string Truncate(string value) => value.Length <= 500 ? value : value[..500];
}

// ADR-005: bandeja local de desarrollo. Informa Outbox con la ruta del archivo, nunca Sent.
public sealed class OutboxEmailSender(string directory, string from) : IEmailSender
{
    public async Task<DeliveryReport> SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, OutboxName("eml"));
        await MailFactory.Build(from, message).WriteToAsync(path, cancellationToken);
        return new DeliveryReport(DeliveryResult.Outbox, $"Bandeja local: {path}");
    }

    internal static string OutboxName(string extension) =>
        $"{DateTime.UtcNow.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture)}-{Guid.NewGuid():N}.{extension}";
}

public sealed record TwilioSettings(string AccountSid, string AuthToken, string? From, string? MessagingServiceSid,
    string? TrialTemplate = null, DateOnly? TrialUntil = null);

public sealed class TwilioSmsSender(HttpClient client, TwilioSettings settings, TimeProvider? time = null) : ISmsSender, IDisposable
{
    public void Dispose() => client.Dispose();
    public async Task<DeliveryReport> SendAsync(string phoneNumber, string text, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (settings.TrialTemplate is not null && (settings.TrialUntil is null ||
            BusinessClock.LocalDay((time ?? TimeProvider.System).GetUtcNow()) > settings.TrialUntil))
            return new(DeliveryResult.Failed, "Twilio: modo de demostración Trial expirado; configure SMS personalizado para operación real.");
        var phone = Regex.Replace(phoneNumber, @"[\s().-]", "");
        if (Regex.IsMatch(phone, @"^(809|829|849)[0-9]{7}$")) phone = "+1" + phone;
        else if (Regex.IsMatch(phone, @"^1[0-9]{10}$")) phone = "+" + phone;
        if (!Regex.IsMatch(phone, @"^\+[1-9][0-9]{7,14}$"))
            return new(DeliveryResult.Failed, "Móvil no está en formato internacional E.164; use +código de país y número.");

        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"https://api.twilio.com/2010-04-01/Accounts/{settings.AccountSid}/Messages.json");
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.AccountSid}:{settings.AuthToken}")));
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["To"] = phone, ["Body"] = settings.TrialTemplate ?? text,
            [settings.MessagingServiceSid is { Length: > 0 } ? "MessagingServiceSid" : "From"] =
                settings.MessagingServiceSid is { Length: > 0 } ? settings.MessagingServiceSid : settings.From!
        });
        try
        {
            using var response = await client.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            using var json = JsonDocument.Parse(body);
            if (response.IsSuccessStatusCode)
                return new(DeliveryResult.Sent, Detail($"Twilio {json.RootElement.GetProperty("sid").GetString()}: {json.RootElement.GetProperty("status").GetString()}" +
                    (settings.TrialTemplate is null ? "" : "; DEMO Trial: confirmación genérica con datos de ejemplo Twilio, sin datos del ticket.")));
            return new(DeliveryResult.Failed, Detail($"Twilio HTTP {(int)response.StatusCode}: {json.RootElement.GetProperty("code")}: {json.RootElement.GetProperty("message").GetString()}"));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(DeliveryResult.Failed, "Twilio: tiempo de espera agotado.");
        }
        catch (HttpRequestException)
        {
            return new(DeliveryResult.Failed, "Twilio: error de conexión HTTP.");
        }
        catch (JsonException)
        {
            return new(DeliveryResult.Failed, "Twilio: respuesta JSON inválida.");
        }
        catch (KeyNotFoundException)
        {
            return new(DeliveryResult.Failed, "Twilio: respuesta sin los campos esperados.");
        }
    }

    private string Detail(string value)
    {
        value = value.Replace(settings.AuthToken, "[redacted]", StringComparison.Ordinal);
        return value.Length <= 500 ? value : value[..500];
    }
}

// Sin proveedor configurado: bandeja local explícita, nunca Sent.
public sealed class OutboxSmsSender(string directory) : ISmsSender
{
    public async Task<DeliveryReport> SendAsync(string phoneNumber, string text, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, OutboxEmailSender.OutboxName("sms.txt"));
        await File.WriteAllTextAsync(path, $"Para: {phoneNumber}\n\n{text}\n", cancellationToken);
        return new DeliveryReport(DeliveryResult.Outbox, $"Bandeja local SMS (sin pasarela, B-01): {path}");
    }
}

internal static class MailFactory
{
    public static MimeMessage Build(string from, EmailMessage message)
    {
        var mime = new MimeMessage();
        mime.From.Add(MailboxAddress.Parse(from));
        mime.To.Add(MailboxAddress.Parse(message.To));
        mime.Subject = message.Subject;
        var body = new BodyBuilder { HtmlBody = message.HtmlBody, TextBody = message.TextBody };
        foreach (var attachment in message.Attachments)
        {
            if (attachment.ContentId is { } cid)
            {
                var linked = body.LinkedResources.Add(attachment.FileName, attachment.Content, ContentType.Parse(attachment.ContentType));
                linked.ContentId = cid;
            }
            else body.Attachments.Add(attachment.FileName, attachment.Content, ContentType.Parse(attachment.ContentType));
        }
        mime.Body = body.ToMessageBody();
        return mime;
    }
}
