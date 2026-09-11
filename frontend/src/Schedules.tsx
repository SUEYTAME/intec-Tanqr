import { useCallback, useState } from "react";
import type { FormEvent } from "react";
import { api } from "./api";
import { AssignmentFields } from "./Assignment";
import {
  Badge,
  Empty,
  Loading,
  Messages,
  PageHeading,
} from "./components";
import {
  emptyAssignment,
  explain,
  formatDateTime,
  formatGallons,
  frequencyLabels,
  isoToLocalInput,
  localInputToIso,
  numberValue,
  ruleLabels,
  textValue,
  useCatalogs,
  useLoad,
} from "./lib";
import type { Assignment, Catalogs } from "./lib";

type Schedule = Assignment & {
  id: string;
  name: string;
  rule: string;
  quantity: number;
  historySize: number;
  frequency: string;
  nextRunAt: string;
  endsAt: string | null;
  autoApprove: boolean;
  active: boolean;
  lastError: string;
  lastRunAt: string | null;
  version: string;
};

function ScheduleForm({
  catalogs,
  editing,
  onSaved,
  onCancel,
}: {
  catalogs: Catalogs;
  editing: Schedule | null;
  onSaved: () => void;
  onCancel: () => void;
}) {
  const [assignment, setAssignment] = useState<Assignment>(
    editing
      ? {
          employeeId: editing.employeeId,
          vehicleId: editing.vehicleId,
          departmentId: editing.departmentId,
          fuelTypeId: editing.fuelTypeId,
        }
      : emptyAssignment,
  );
  const [rule, setRule] = useState(editing?.rule ?? "Fixed");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const vehicle = catalogs.vehicles.find((v) => v.id === assignment.vehicleId);
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const form = new FormData(event.currentTarget);
    const ends = textValue(form, "endsAt");
    setBusy(true);
    setError("");
    try {
      await api(`/api/programaciones/${editing?.id ?? ""}`, {
        method: editing ? "PUT" : "POST",
        headers: editing ? { "If-Match": `"${editing.version}"` } : {},
        body: JSON.stringify({
          name: textValue(form, "name"),
          ...assignment,
          rule,
          quantity: rule === "Fixed" ? numberValue(form, "quantity") : 0,
          historySize: numberValue(form, "historySize") ?? editing?.historySize ?? 1,
          frequency: textValue(form, "frequency"),
          nextRunAt: localInputToIso(textValue(form, "nextRunAt")),
          endsAt: ends ? localInputToIso(ends) : null,
          autoApprove: form.get("autoApprove") === "on",
          active: form.get("active") === "on",
        }),
      });
      onSaved();
    } catch (e) {
      setError(explain(e));
      setBusy(false);
    }
  }
  return (
    <section className="editor">
      <div className="section-heading">
        <h2>{editing ? "Editar programación" : "Nueva programación"}</h2>
        <button className="subtle" onClick={onCancel} disabled={busy}>
          Cancelar
        </button>
      </div>
      <form onSubmit={submit}>
        <Messages error={error} />
        <div className="form-grid">
          <label>
            Nombre
            <input name="name" required maxLength={150} defaultValue={editing?.name} />
          </label>
          <AssignmentFields catalogs={catalogs} value={assignment} onChange={setAssignment} />
          <label>
            Regla de cantidad
            <select value={rule} onChange={(e) => setRule(e.target.value)}>
              {Object.entries(ruleLabels).map(([key, label]) => (
                <option key={key} value={key}>
                  {label}
                </option>
              ))}
            </select>
          </label>
          {rule === "Fixed" ? (
            <label>
              Cantidad (galones)
              <input
                name="quantity"
                type="number"
                inputMode="decimal"
                required
                min={0.001}
                step={0.001}
                max={vehicle?.tankCapacity}
                defaultValue={editing?.rule === "Fixed" ? editing.quantity : undefined}
              />
            </label>
          ) : (
            <label>
              Despachos a promediar (1–50)
              <input
                name="historySize"
                type="number"
                inputMode="numeric"
                required
                min={1}
                max={50}
                step={1}
                defaultValue={editing?.historySize ?? 5}
              />
            </label>
          )}
          <label>
            Frecuencia
            <select name="frequency" required defaultValue={editing?.frequency ?? "Weekly"}>
              {Object.entries(frequencyLabels).map(([key, label]) => (
                <option key={key} value={key}>
                  {label}
                </option>
              ))}
            </select>
          </label>
          <label>
            Próxima ejecución
            <input
              name="nextRunAt"
              type="datetime-local"
              required
              defaultValue={editing ? isoToLocalInput(editing.nextRunAt) : undefined}
            />
          </label>
          <label>
            Termina (opcional)
            <input
              name="endsAt"
              type="datetime-local"
              defaultValue={editing?.endsAt ? isoToLocalInput(editing.endsAt) : undefined}
            />
          </label>
        </div>
        <p className="hint">Fechas en hora de Santo Domingo.</p>
        <div className="form-actions">
          <div className="checks">
            <label className="check">
              <input name="autoApprove" type="checkbox" defaultChecked={editing?.autoApprove ?? false} />
              Aprobar automáticamente (emite el ticket sin revisión)
            </label>
            <label className="check">
              <input name="active" type="checkbox" defaultChecked={editing?.active ?? true} />
              Programación activa
            </label>
          </div>
          <button className="primary" disabled={busy}>
            {busy ? "Guardando…" : "Guardar programación"}
          </button>
        </div>
      </form>
    </section>
  );
}

// RF-05/RF-11: solicitudes programadas y recurrentes.
export function Schedules({ canWrite }: { canWrite: boolean }) {
  const load = useCallback(
    () => api<{ items: Schedule[] }>("/api/programaciones/"),
    [],
  );
  const { data, error, loading, reload } = useLoad(load);
  const catalogs = useCatalogs();
  const [editing, setEditing] = useState<Schedule | null | undefined>(undefined);
  const [notice, setNotice] = useState("");
  return (
    <>
      <PageHeading
        eyebrow="OPERACIÓN / PROGRAMACIONES"
        title="Programaciones"
        description="Solicitudes que el sistema genera solo, una vez o de forma recurrente."
      >
        {canWrite && editing === undefined && (
          <button
            className="primary"
            onClick={() => {
              setEditing(null);
              setNotice("");
            }}
          >
            + Nueva programación
          </button>
        )}
      </PageHeading>
      <Messages error={error || catalogs.error} notice={notice} />
      {editing !== undefined && catalogs.data && (
        <ScheduleForm
          key={editing?.id ?? "new"}
          catalogs={catalogs.data}
          editing={editing}
          onCancel={() => setEditing(undefined)}
          onSaved={() => {
            setEditing(undefined);
            setNotice("Programación guardada. El cambio quedó en la auditoría.");
            reload();
          }}
        />
      )}
      <section className="data-panel">
        {!data ? (
          loading && <Loading />
        ) : data.items.length === 0 ? (
          <Empty
            title="No hay programaciones"
            text="Crea una para generar solicitudes automáticamente."
          />
        ) : (
          <div className="table-scroll">
            <table>
              <thead>
                <tr>
                  <th>Nombre</th>
                  <th>Empleado</th>
                  <th>Vehículo</th>
                  <th>Cantidad</th>
                  <th>Frecuencia</th>
                  <th>Próxima ejecución</th>
                  <th>Última ejecución</th>
                  <th>Estado</th>
                  {canWrite && <th>Acciones</th>}
                </tr>
              </thead>
              <tbody>
                {data.items.map((s) => (
                  <tr key={s.id}>
                    <td>
                      {s.name}
                      {s.autoApprove && <small className="cell-note">Aprobación automática</small>}
                    </td>
                    <td>
                      {catalogs.data?.employees.find((e) => e.id === s.employeeId)?.fullName ?? "…"}
                    </td>
                    <td>
                      {catalogs.data?.vehicles.find((v) => v.id === s.vehicleId)?.plate ?? "…"}
                    </td>
                    <td>
                      {s.rule === "Fixed"
                        ? formatGallons(s.quantity)
                        : `Promedio de ${s.historySize} despachos`}
                    </td>
                    <td>
                      {frequencyLabels[s.frequency] ?? s.frequency}
                      {s.endsAt && <small className="cell-note">hasta {formatDateTime(s.endsAt)}</small>}
                    </td>
                    <td>{formatDateTime(s.nextRunAt)}</td>
                    <td>
                      {formatDateTime(s.lastRunAt)}
                      {s.lastError && <small className="cell-note negative">{s.lastError}</small>}
                    </td>
                    <td>
                      {!s.active ? (
                        <Badge tone="inactive">Inactiva</Badge>
                      ) : s.lastError ? (
                        <Badge tone="danger">Con error</Badge>
                      ) : (
                        <Badge>Activa</Badge>
                      )}
                    </td>
                    {canWrite && (
                      <td>
                        <button
                          className="text-button"
                          onClick={() => {
                            setEditing(s);
                            setNotice("");
                            window.scrollTo({ top: 0, behavior: "smooth" });
                          }}
                        >
                          Editar
                        </button>
                      </td>
                    )}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>
    </>
  );
}
