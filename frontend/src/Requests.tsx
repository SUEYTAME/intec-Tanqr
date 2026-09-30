import { useCallback, useState } from "react";
import type { FormEvent } from "react";
import { api } from "./api";
import { AssignmentFields } from "./Assignment";
import {
  Badge,
  ConfirmDialog,
  Deliveries,
  Empty,
  Loading,
  Messages,
  PageHeading,
  Pager,
  Tabs,
} from "./components";
import type { Delivery } from "./components";
import {
  emptyAssignment,
  explain,
  formatDateTime,
  formatGallons,
  isoToLocalInput,
  localInputToIso,
  numberValue,
  originLabels,
  requestStatusLabels,
  textValue,
  useCatalogs,
  useLoad,
} from "./lib";
import type { Assignment } from "./lib";

type FuelRequest = {
  id: string;
  status: string;
  origin: string;
  authorizedQuantity: number;
  requestedAt: string;
  expiresAt: string | null;
  notes: string;
  decidedAt: string | null;
  decisionReason: string;
  version: string;
  employeeName: string;
  vehiclePlate: string;
  department: string;
  fuelType: string;
  requestedBy: string;
  decidedBy: string | null;
  ticketNumber: string | null;
};
type Approval = { ticketId: string; number: string; deliveries: Delivery[] };
type Decision = { kind: "approve" | "reject" | "cancel"; row: FuelRequest };

const statusTone = {
  Pending: "warn",
  Approved: "",
  Rejected: "danger",
  Cancelled: "inactive",
} as const;

function NewRequest({
  onCreated,
  onCancel,
}: {
  onCreated: () => void;
  onCancel: () => void;
}) {
  const catalogs = useCatalogs();
  const [value, setValue] = useState<Assignment>(emptyAssignment);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const vehicle = catalogs.data?.vehicles.find((v) => v.id === value.vehicleId);
  const [minExpiry] = useState(() =>
    isoToLocalInput(new Date(Date.now() + 61 * 60000)),
  );
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setBusy(true);
    setError("");
    const form = new FormData(event.currentTarget);
    const expires = textValue(form, "expiresAt");
    try {
      await api("/api/solicitudes/", {
        method: "POST",
        body: JSON.stringify({
          ...value,
          authorizedQuantity: numberValue(form, "authorizedQuantity"),
          expiresAt: expires ? localInputToIso(expires) : null,
          notes: textValue(form, "notes") || null,
        }),
      });
      onCreated();
    } catch (e) {
      setError(explain(e));
      setBusy(false);
    }
  }
  return (
    <section className="editor">
      <div className="section-heading">
        <h2>Nueva solicitud</h2>
        <button className="subtle" onClick={onCancel} disabled={busy}>
          Cancelar
        </button>
      </div>
      <form onSubmit={submit}>
        <Messages error={catalogs.error || error} />
        {!catalogs.data ? (
          <Loading text="Cargando catálogos…" />
        ) : (
          <>
            <div className="form-grid">
              <AssignmentFields
                catalogs={catalogs.data}
                value={value}
                onChange={setValue}
              />
              <label>
                Cantidad autorizada (galones)
                <input
                  name="authorizedQuantity"
                  type="number"
                  inputMode="decimal"
                  required
                  min={0.001}
                  step={0.001}
                  max={vehicle?.tankCapacity}
                  aria-describedby="quantity-hint"
                />
                <small id="quantity-hint">
                  {vehicle
                    ? `Máximo ${formatGallons(vehicle.tankCapacity)} (capacidad del tanque del vehículo).`
                    : "Hasta 3 decimales; no puede superar el tanque del vehículo."}
                </small>
              </label>
              <label>
                Vence (opcional)
                <input
                  name="expiresAt"
                  type="datetime-local"
                  min={minExpiry}
                  aria-describedby="expiry-hint"
                />
                <small id="expiry-hint">
                  Hora de Santo Domingo. Entre 1 hora y 90 días; vacío usa la
                  vigencia de Parámetros.
                </small>
              </label>
            </div>
            <label className="wide">
              Notas (opcional)
              <textarea name="notes" maxLength={500} rows={2} />
            </label>
            <div className="form-actions">
              <span />
              <button className="primary" disabled={busy}>
                {busy ? "Guardando…" : "Registrar solicitud"}
              </button>
            </div>
          </>
        )}
      </form>
    </section>
  );
}

// RF-06/RF-07: solicitudes y su aprobación, rechazo o cancelación.
export function Requests({
  canCreate,
  canApprove,
}: {
  canCreate: boolean;
  canApprove: boolean;
}) {
  const [status, setStatus] = useState("Pending");
  const [page, setPage] = useState(1);
  const [creating, setCreating] = useState(false);
  const [decision, setDecision] = useState<Decision | null>(null);
  const [approval, setApproval] = useState<Approval | null>(null);
  const [notice, setNotice] = useState("");
  const load = useCallback(
    () =>
      api<{ items: FuelRequest[]; total: number }>(
        `/api/solicitudes/?page=${page}${status ? `&status=${status}` : ""}`,
      ),
    [page, status],
  );
  const { data, error, loading, reload } = useLoad(load);
  async function decide(form: FormData) {
    if (!decision) return;
    const { kind, row } = decision;
    const reason = textValue(form, "reason") || null;
    if (kind === "approve") {
      const result = await api<Approval>(`/api/solicitudes/${row.id}/aprobar`, {
        method: "POST",
        body: JSON.stringify({ version: row.version, reason }),
      });
      setApproval(result);
      setNotice("");
    } else {
      await api(
        `/api/solicitudes/${row.id}/${kind === "reject" ? "rechazar" : "cancelar"}`,
        {
          method: "POST",
          body: JSON.stringify({ version: row.version, reason }),
        },
      );
      setApproval(null);
      setNotice(
        kind === "reject" ? "Solicitud rechazada." : "Solicitud cancelada.",
      );
    }
    reload();
  }
  const dialog = decision && {
    approve: {
      title: "Aprobar solicitud",
      confirm: "Aprobar y emitir ticket",
      text: "Se emitirá un ticket con QR firmado y se enviará al empleado.",
      required: false,
    },
    reject: {
      title: "Rechazar solicitud",
      confirm: "Rechazar solicitud",
      text: "La solicitud quedará rechazada y no se podrá aprobar después.",
      required: true,
    },
    cancel: {
      title: "Cancelar solicitud",
      confirm: "Cancelar solicitud",
      text: "La solicitud quedará cancelada y no se podrá aprobar después.",
      required: false,
    },
  }[decision.kind];
  return (
    <>
      <PageHeading
        eyebrow="OPERACIÓN / SOLICITUDES"
        title="Solicitudes"
        description="Pedidos de combustible pendientes de aprobación y su historial."
      >
        {canCreate && !creating && (
          <button className="primary" onClick={() => setCreating(true)}>
            + Nueva solicitud
          </button>
        )}
      </PageHeading>
      <Messages error={error} notice={notice} />
      {approval && (
        <section className="result-card" aria-live="polite">
          <p className="eyebrow">TICKET EMITIDO</p>
          <h2>Ticket {approval.number}</h2>
          <p>
            Resultado de la entrega al empleado. «En bandeja local» significa
            que el mensaje quedó guardado en el servidor y <strong>no</strong>{" "}
            salió a un teléfono ni a un buzón.
          </p>
          <Deliveries deliveries={approval.deliveries} />
          <button className="subtle" onClick={() => setApproval(null)}>
            Cerrar aviso
          </button>
        </section>
      )}
      {creating && (
        <NewRequest
          onCancel={() => setCreating(false)}
          onCreated={() => {
            setCreating(false);
            setNotice("Solicitud registrada. Queda pendiente de aprobación.");
            setApproval(null);
            setStatus("Pending");
            setPage(1);
            reload();
          }}
        />
      )}
      <Tabs
        label="Estado de las solicitudes"
        value={status}
        onChange={(value) => {
          setStatus(value);
          setPage(1);
        }}
        tabs={[
          ["Pending", "Pendientes"],
          ["Approved", "Aprobadas"],
          ["Rejected", "Rechazadas"],
          ["Cancelled", "Canceladas"],
          ["", "Todas"],
        ]}
      />
      <section className="data-panel">
        {!data ? (
          loading && <Loading />
        ) : data.items.length === 0 ? (
          <Empty
            title="No hay solicitudes en este estado"
            text={
              canCreate
                ? "Registra una solicitud con «Nueva solicitud»."
                : "Aparecerán cuando un usuario autorizado las registre."
            }
          />
        ) : (
          <div className="table-scroll">
            <table>
              <thead>
                <tr>
                  <th>Solicitada</th>
                  <th>Empleado</th>
                  <th>Vehículo</th>
                  <th>Departamento</th>
                  <th>Combustible</th>
                  <th>Cantidad</th>
                  <th>Origen</th>
                  <th>Estado</th>
                  <th>Ticket</th>
                  {(canApprove || canCreate) && <th>Acciones</th>}
                </tr>
              </thead>
              <tbody>
                {data.items.map((row) => (
                  <tr key={row.id}>
                    <td>{formatDateTime(row.requestedAt)}</td>
                    <td>{row.employeeName}</td>
                    <td>{row.vehiclePlate}</td>
                    <td>{row.department}</td>
                    <td>{row.fuelType}</td>
                    <td>{formatGallons(row.authorizedQuantity)}</td>
                    <td>{originLabels[row.origin] ?? row.origin}</td>
                    <td>
                      <Badge
                        tone={
                          statusTone[row.status as keyof typeof statusTone] ??
                          ""
                        }
                      >
                        {requestStatusLabels[row.status] ?? row.status}
                      </Badge>
                      {row.decisionReason && (
                        <small className="cell-note">
                          {row.decisionReason}
                        </small>
                      )}
                    </td>
                    <td className="mono">{row.ticketNumber ?? "—"}</td>
                    {(canApprove || canCreate) && (
                      <td className="actions">
                        {row.status === "Pending" && (
                          <>
                            {canApprove && (
                              <button
                                className="text-button"
                                onClick={() =>
                                  setDecision({ kind: "approve", row })
                                }
                              >
                                Aprobar
                              </button>
                            )}
                            {canApprove && (
                              <button
                                className="text-button"
                                onClick={() =>
                                  setDecision({ kind: "reject", row })
                                }
                              >
                                Rechazar
                              </button>
                            )}
                            {canCreate && (
                              <button
                                className="text-button"
                                onClick={() =>
                                  setDecision({ kind: "cancel", row })
                                }
                              >
                                Cancelar
                              </button>
                            )}
                          </>
                        )}
                      </td>
                    )}
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
            unit="solicitudes"
          />
        )}
      </section>
      {decision && dialog && (
        <ConfirmDialog
          title={dialog.title}
          confirmLabel={dialog.confirm}
          danger={decision.kind !== "approve"}
          description={
            <>
              <p>{dialog.text}</p>
              <p>
                <strong>{decision.row.employeeName}</strong> ·{" "}
                {decision.row.vehiclePlate} · {decision.row.fuelType} ·{" "}
                {formatGallons(decision.row.authorizedQuantity)}
              </p>
            </>
          }
          onConfirm={decide}
          onClose={() => setDecision(null)}
        >
          <label>
            {dialog.required ? "Motivo (mínimo 5 caracteres)" : "Motivo (opcional)"}
            <textarea
              name="reason"
              rows={2}
              maxLength={500}
              minLength={dialog.required ? 5 : undefined}
              required={dialog.required}
            />
          </label>
        </ConfirmDialog>
      )}
    </>
  );
}
