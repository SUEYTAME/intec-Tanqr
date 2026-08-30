using System.Globalization;
using System.Net.Sockets;
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

// B-01: no hay pasarela SMS contratada. El mensaje se escribe a disco y se informa como tal.
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
