using Combustible.Domain;

namespace Combustible.Application;

// ADR-005: todo servicio externo detrás de una interfaz. Un resultado Outbox significa
// "escrito en la bandeja local", nunca "enviado".
public sealed record DeliveryReport(DeliveryResult Result, string Detail);

public sealed record EmailAttachment(string FileName, string ContentType, byte[] Content, string? ContentId = null);

public sealed record EmailMessage(string To, string Subject, string HtmlBody, string TextBody,
    IReadOnlyList<EmailAttachment> Attachments);

public interface IEmailSender
{
    Task<DeliveryReport> SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

public interface ISmsSender
{
    Task<DeliveryReport> SendAsync(string phoneNumber, string text, CancellationToken cancellationToken);
}
