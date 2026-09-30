import { useCallback, useEffect, useState } from "react";
import type { FormEvent } from "react";
import { api, apiBlob } from "./api";
import {
  ConfirmDialog,
  Deliveries,
  Empty,
  Facts,
  Loading,
  Messages,
  PageHeading,
  Pager,
  TicketStatus,
} from "./components";
import type { Delivery } from "./components";
import {
  activeTicketStatuses,
  explain,
  formatDateTime,
  formatGallons,
  saveBlob,
  textValue,
  ticketStatusLabels,
  useLoad,
} from "./lib";

type Ticket = {
  id: string;
  number: string;
  shortCode: string;
  status: string;
  authorizedQuantity: number;
  issuedAt: string;
  expiresAt: string;
  consumedAt: string | null;
  voidedAt: string | null;
  voidReason: string;
  version: string;
  employeeName: string;
  employeeCode: string;
  vehiclePlate: string;
  vehicleCode: string;
  department: string;
  fuelType: string;
};
type Detail = {
  ticket: Ticket;
  deliveries: Delivery[];
  dispatch: {
    id: string;
    quantity: number;
    difference: number;
    differenceReason: string;
    occurredAt: string;
    observations: string;
  } | null;
};

function useRefreshWhenVisible(reload: () => void) {
  useEffect(() => {
    const refresh = () => {
      if (document.visibilityState === "visible") reload();
    };
    window.addEventListener("focus", refresh);
    document.addEventListener("visibilitychange", refresh);
    const timer = window.setInterval(refresh, 15000);
    return () => {
      window.removeEventListener("focus", refresh);
      document.removeEventListener("visibilitychange", refresh);
      window.clearInterval(timer);
    };
  }, [reload]);
}

function TicketDetail({
  id,
  canManage,
  onClose,
  onChanged,
}: {
  id: string;
  canManage: boolean;
  onClose: () => void;
  onChanged: () => void;
}) {
  const load = useCallback(() => api<Detail>(`/api/tickets/${id}`), [id]);
  const { data, error, loading, reload } = useLoad(load);
  useRefreshWhenVisible(reload);
  const [qr, setQr] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [actionError, setActionError] = useState("");
  const [notice, setNotice] = useState("");
  const [resent, setResent] = useState<Delivery[] | null>(null);
  const [voiding, setVoiding] = useState(false);
  useEffect(
    () => () => {
      if (qr) URL.revokeObjectURL(qr);
    },
    [qr],
  );
  async function run(action: () => Promise<void>) {
    setBusy(true);
    setActionError("");
    setNotice("");
    try {
      await action();
    } catch (e) {
      setActionError(explain(e));
    } finally {
      setBusy(false);
    }
  }
  const ticket = data?.ticket;
  const active = ticket ? activeTicketStatuses.includes(ticket.status) : false;
  return (
    <section className="editor" aria-labelledby="ticket-detail-title">
      <div className="section-heading">
        <h2 id="ticket-detail-title">
          {ticket ? `Ticket ${ticket.number}` : "Ticket"}
        </h2>
        <button className="subtle" onClick={onClose}>
          Cerrar detalle
        </button>
      </div>
      <div className="detail-body">
        <Messages error={error || actionError} notice={notice} />
        {!data || !ticket ? (
          loading && <Loading />
        ) : (
          <>
            <Facts
              items={[
                ["Estado", <TicketStatus key="estado" status={ticket.status} />],
                ["Código corto", <span key="codigo" className="mono">{ticket.shortCode}</span>],
                ["Empleado", `${ticket.employeeName} (${ticket.employeeCode})`],
                ["Vehículo", `${ticket.vehiclePlate} · ficha ${ticket.vehicleCode}`],
                ["Departamento", ticket.department],
                ["Combustible", ticket.fuelType],
                ["Autorizado", formatGallons(ticket.authorizedQuantity)],
                ["Emitido", formatDateTime(ticket.issuedAt)],
                ["Vence", formatDateTime(ticket.expiresAt)],
                ...(ticket.consumedAt
                  ? ([["Consumido", formatDateTime(ticket.consumedAt)]] as [string, string][])
                  : []),
                ...(ticket.voidedAt
                  ? ([
                      ["Anulado", formatDateTime(ticket.voidedAt)],
                      ["Motivo de anulación", ticket.voidReason],
                    ] as [string, string][])
                  : []),
              ]}
            />
            {canManage && active && (
              <div className="button-row">
                <button
                  disabled={busy}
                  onClick={() =>
                    run(async () => {
                      const { blob } = await apiBlob(`/api/tickets/${id}/qr.png`);
                      setQr(URL.createObjectURL(blob));
                    })
                  }
                >
                  Ver QR
                </button>
                <button
                  disabled={busy}
                  onClick={() =>
                    run(async () => {
                      const { blob, filename } = await apiBlob(
                        `/api/tickets/${id}/qr.png`,
                      );
                      saveBlob(blob, filename ?? `${ticket.number}.png`);
                    })
                  }
                >
                  Descargar QR
                </button>
                <button
                  disabled={busy}
                  onClick={() =>
                    run(async () => {
                      const { blob, filename } = await apiBlob(
                        `/api/tickets/${id}/pdf`,
                      );
                      saveBlob(blob, filename ?? `${ticket.number}.pdf`);
                    })
                  }
                >
                  Descargar PDF
                </button>
                <button
                  disabled={busy}
                  onClick={() =>
                    run(async () => {
                      const result = await api<{ deliveries: Delivery[] }>(
                        `/api/tickets/${id}/reenviar`,
                        { method: "POST" },
                      );
                      setResent(result.deliveries);
                      setNotice("Reenvío procesado. Revisa el resultado por canal.");
                      reload();
                    })
                  }
                >
                  Reenviar
                </button>
                <button
                  className="danger-outline"
                  disabled={busy}
                  onClick={() => setVoiding(true)}
                >
                  Anular
                </button>
              </div>
            )}
            {qr && (
              <figure className="qr-view">
                <img src={qr} alt={`Código QR del ticket ${ticket.number}`} />
                <figcaption>
                  El QR contiene la firma del ticket; la cantidad siempre la
                  confirma el servidor.
                </figcaption>
              </figure>
            )}
            {resent && (
              <>
                <h3>Resultado del reenvío</h3>
                <Deliveries deliveries={resent} />
              </>
            )}
            <h3>Entregas registradas</h3>
            <Deliveries deliveries={data.deliveries} />
            <h3>Despacho</h3>
            {data.dispatch ? (
              <Facts
                items={[
                  ["Fecha", formatDateTime(data.dispatch.occurredAt)],
                  ["Despachado", formatGallons(data.dispatch.quantity)],
                  ["Diferencia", formatGallons(data.dispatch.difference)],
                  ...(data.dispatch.differenceReason
                    ? ([["Motivo de diferencia", data.dispatch.differenceReason]] as [string, string][])
                    : []),
                  ...(data.dispatch.observations
                    ? ([["Observaciones", data.dispatch.observations]] as [string, string][])
                    : []),
                ]}
              />
            ) : (
              <p className="hint">Sin despacho registrado.</p>
            )}
          </>
        )}
      </div>
      {voiding && ticket && (
        <ConfirmDialog
          title={`Anular ticket ${ticket.number}`}
          confirmLabel="Anular ticket"
          danger
          description={
            <p>
              El QR dejará de ser válido de inmediato. Esta acción no se puede
              deshacer y queda en la auditoría.
            </p>
          }
          onConfirm={async (form) => {
            await api(`/api/tickets/${id}/anular`, {
              method: "POST",
              body: JSON.stringify({
                version: ticket.version,
                reason: textValue(form, "reason"),
              }),
            });
            setQr(null);
            setNotice("Ticket anulado.");
            reload();
            onChanged();
          }}
          onClose={() => setVoiding(false)}
        >
          <label>
            Motivo (mínimo 5 caracteres)
            <textarea name="reason" rows={2} required minLength={5} maxLength={500} />
          </label>
        </ConfirmDialog>
      )}
    </section>
  );
}

// RF-08/RF-09/RF-10: consulta de tickets, QR, PDF, reenvío y anulación.
export function Tickets({ canManage }: { canManage: boolean }) {
  const [status, setStatus] = useState("");
  const [query, setQuery] = useState("");
  const [page, setPage] = useState(1);
  const [selected, setSelected] = useState<string | null>(null);
  const load = useCallback(() => {
    const params = new URLSearchParams({ page: String(page) });
    if (status) params.set("status", status);
    if (query) params.set("q", query);
    return api<{ items: Ticket[]; total: number }>(`/api/tickets/?${params}`);
  }, [page, status, query]);
  const { data, error, loading, reload } = useLoad(load);
  useRefreshWhenVisible(reload);
  function search(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setQuery(textValue(new FormData(event.currentTarget), "q"));
    setPage(1);
  }
  return (
    <>
      <PageHeading
        eyebrow="OPERACIÓN / TICKETS"
        title="Tickets"
        description="Tickets emitidos, su entrega y su estado de consumo."
      >
        <button className="subtle" onClick={reload} disabled={loading}>
          Actualizar
        </button>
      </PageHeading>
      <Messages error={error} />
      {selected && (
        <TicketDetail
          key={selected}
          id={selected}
          canManage={canManage}
          onClose={() => setSelected(null)}
          onChanged={reload}
        />
      )}
      <form className="filters" onSubmit={search}>
        <label>
          Estado
          <select
            value={status}
            onChange={(e) => {
              setStatus(e.target.value);
              setPage(1);
            }}
          >
            <option value="">Todos</option>
            {Object.entries(ticketStatusLabels).map(([key, label]) => (
              <option key={key} value={key}>
                {label}
              </option>
            ))}
          </select>
        </label>
        <label>
          Buscar por número, código corto o placa
          <input name="q" type="search" maxLength={40} defaultValue={query} />
        </label>
        <button type="submit">Buscar</button>
      </form>
      <section className="data-panel">
        {!data ? (
          loading && <Loading />
        ) : data.items.length === 0 ? (
          <Empty
            title="No hay tickets con estos filtros"
            text="Los tickets se emiten al aprobar una solicitud."
          />
        ) : (
          <div className="table-scroll">
            <table>
              <thead>
                <tr>
                  <th>Número</th>
                  <th>Estado</th>
                  <th>Empleado</th>
                  <th>Vehículo</th>
                  <th>Combustible</th>
                  <th>Autorizado</th>
                  <th>Vence</th>
                  <th>Acciones</th>
                </tr>
              </thead>
              <tbody>
                {data.items.map((t) => (
                  <tr key={t.id}>
                    <td className="mono">
                      {t.number}
                      <small className="cell-note">{t.shortCode}</small>
                    </td>
                    <td>
                      <TicketStatus status={t.status} />
                    </td>
                    <td>{t.employeeName}</td>
                    <td>{t.vehiclePlate}</td>
                    <td>{t.fuelType}</td>
                    <td>{formatGallons(t.authorizedQuantity)}</td>
                    <td>{formatDateTime(t.expiresAt)}</td>
                    <td>
                      <button
                        className="text-button"
                        aria-label={`Ver ticket ${t.number}`}
                        onClick={() => {
                          setSelected(t.id);
                          window.scrollTo({ top: 0, behavior: "smooth" });
                        }}
                      >
                        Ver
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
        {data && (
          <Pager
            page={page}
            total={data.total}
            loading={loading}
            onPage={setPage}
            unit="tickets"
          />
        )}
      </section>
    </>
  );
}
