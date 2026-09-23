import { useCallback } from "react";
import { Facts, Loading, TicketStatus } from "./components";
import { formatDateTime, formatGallons, useLoad } from "./lib";

type PublicView = {
  number: string;
  shortCode: string;
  status: string;
  authorizedQuantity: number;
  issuedAt: string;
  expiresAt: string;
  employeeName: string;
  vehiclePlate: string;
  fuelType: string;
  department: string;
  active: boolean;
};

// RF-09: enlace seguro del ticket (sin sesión). El token del enlace es el secreto;
// el servidor lo compara por hash y limita la tasa de consultas.
export function PublicTicket({ ticketKey }: { ticketKey: string }) {
  const base = `/api/publico/tickets/${encodeURIComponent(ticketKey)}`;
  const load = useCallback(async () => {
    const response = await fetch(base, {
      headers: { Accept: "application/json" },
    });
    if (response.status === 404) return null;
    if (response.status === 429)
      throw new Error(
        "Demasiadas consultas seguidas. Espera un minuto y vuelve a abrir el enlace.",
      );
    if (!response.ok)
      throw new Error(`No se pudo consultar el ticket (${response.status}).`);
    return (await response.json()) as PublicView;
  }, [base]);
  const ticket = useLoad(load);
  const t = ticket.data;

  return (
    <main className="public-ticket">
      <section>
        <div className="brand">
          INTEC<span>TanQR</span>
        </div>
        {ticket.error ? (
          <p className="error" role="alert">
            {ticket.error}
          </p>
        ) : ticket.loading ? (
          <Loading text="Buscando el ticket…" />
        ) : !t ? (
          <p className="error" role="alert">
            Este enlace no corresponde a ningún ticket. Revisa que lo copiaste
            completo.
          </p>
        ) : (
          <>
            <p className="eyebrow">TICKET DE COMBUSTIBLE</p>
            <h1 className="mono">{t.number}</h1>
            <p>
              <TicketStatus status={t.status} />
            </p>
            {t.active ? (
              <figure className="qr-view">
                <img src={`${base}/qr.png`} alt={`Código QR del ticket ${t.number}`} />
                <figcaption>
                  Muestra este código en la estación. Código corto:{" "}
                  <strong className="mono">{t.shortCode}</strong>
                </figcaption>
              </figure>
            ) : (
              <p className="notice">
                Este ticket ya no se puede despachar, por eso no se muestra el
                QR.
              </p>
            )}
            <Facts
              items={[
                ["Cantidad autorizada", formatGallons(t.authorizedQuantity)],
                ["Combustible", t.fuelType],
                ["Empleado", t.employeeName],
                ["Vehículo", t.vehiclePlate],
                ["Departamento", t.department],
                ["Emitido", formatDateTime(t.issuedAt)],
                ["Vence", formatDateTime(t.expiresAt)],
              ]}
            />
            {t.active && (
              <a className="primary" href={`${base}/pdf`} download>
                Descargar PDF
              </a>
            )}
          </>
        )}
      </section>
    </main>
  );
}
