using System.Security.Authentication;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace Combustible.Api.Security;

// RS-03 (tránsito): todo endpoint HTTPS de Kestrel negocia solo TLS 1.3. El certificado de
// producción y el dominio siguen bloqueados (B-03); la política ya está aplicada y probada.
public static class TransportSecurity
{
    public static void Configure(KestrelServerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.AddServerHeader = false;
        options.ConfigureHttpsDefaults(https => https.SslProtocols = SslProtocols.Tls13);
    }
}
