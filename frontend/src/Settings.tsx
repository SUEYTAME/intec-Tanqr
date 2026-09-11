import { useCallback, useState } from "react";
import type { FormEvent } from "react";
import { api } from "./api";
import {
  ConfirmDialog,
  Empty,
  Facts,
  Loading,
  Messages,
  PageHeading,
} from "./components";
import { explain, numberValue, textValue, useLoad } from "./lib";

type TicketSettings = {
  prefix: string;
  resetAnnually: boolean;
  validityDays: number;
  warningHours: number;
  maxActiveTicketsPerVehicle: number;
  version: string;
};
type Client = { clientId: string; displayName: string; role: string | null };
type ClientList = { items: Client[]; tokenEndpoint: string; scope: string };
type NewClient = Client & {
  clientSecret: string;
  tokenEndpoint: string;
  scope: string;
};

// RF-08/RF-10: numeración, vigencia y límites de los tickets. Solo el administrador cambia.
export function Settings({ canWrite }: { canWrite: boolean }) {
  const load = useCallback(() => api<TicketSettings>("/api/parametros/"), []);
  const settings = useLoad(load);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");

  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!settings.data) return;
    const form = new FormData(event.currentTarget);
    setSaving(true);
    setError("");
    setNotice("");
    try {
      await api("/api/parametros/", {
        method: "PUT",
        body: JSON.stringify({
          prefix: textValue(form, "prefix").toUpperCase(),
          resetAnnually: form.get("resetAnnually") === "on",
          validityDays: numberValue(form, "validityDays"),
          warningHours: numberValue(form, "warningHours"),
          maxActiveTicketsPerVehicle: numberValue(form, "maxActive"),
          version: settings.data.version,
        }),
      });
      settings.reload();
      setNotice(
        "Parámetros guardados. Aplican a los tickets que se emitan desde ahora.",
      );
    } catch (e) {
      setError(
        `${explain(e)} Si otra persona los cambió mientras editabas, recarga y vuelve a intentarlo.`,
      );
    } finally {
      setSaving(false);
    }
  }

  const s = settings.data;
  return (
    <>
      <PageHeading
        eyebrow="CONFIGURACIÓN"
        title="Parámetros de tickets"
        description="Prefijo y reinicio de la numeración, días de vigencia, aviso previo al vencimiento y tickets activos por vehículo."
      />
      <Messages error={error || settings.error} notice={notice} />
      <section className="editor">
        <div className="section-heading">
          <h2>{canWrite ? "Editar parámetros" : "Parámetros vigentes"}</h2>
          <button
            className="subtle"
            onClick={settings.reload}
            disabled={settings.loading}
          >
            Recargar
          </button>
        </div>
        {!s ? (
          <Loading />
        ) : canWrite ? (
          <form key={s.version} onSubmit={save}>
            <div className="form-grid">
              <label>
                Prefijo (2 a 10 letras)
                <input
                  name="prefix"
                  required
                  pattern="[A-Za-z]{2,10}"
                  maxLength={10}
                  defaultValue={s.prefix}
                />
              </label>
              <label>
                Vigencia (días, 1 a 90)
                <input
                  name="validityDays"
                  type="number"
                  required
                  min={1}
                  max={90}
                  defaultValue={s.validityDays}
                />
              </label>
              <label>
                Aviso antes de vencer (horas, 1 a 720)
                <input
                  name="warningHours"
                  type="number"
                  required
                  min={1}
                  max={720}
                  defaultValue={s.warningHours}
                />
              </label>
              <label>
                Tickets activos por vehículo (1 a 20)
                <input
                  name="maxActive"
                  type="number"
                  required
                  min={1}
                  max={20}
                  defaultValue={s.maxActiveTicketsPerVehicle}
                />
              </label>
            </div>
            <div className="form-actions">
              <label className="check">
                <input
                  name="resetAnnually"
                  type="checkbox"
                  defaultChecked={s.resetAnnually}
                />
                Reiniciar la numeración cada año
              </label>
              <button className="primary" disabled={saving}>
                {saving ? "Guardando…" : "Guardar parámetros"}
              </button>
            </div>
          </form>
        ) : (
          <div className="detail-body">
            <Facts
              items={[
                ["Prefijo", s.prefix],
                ["Reinicio anual", s.resetAnnually ? "Sí" : "No"],
                ["Vigencia", `${s.validityDays} días`],
                ["Aviso previo", `${s.warningHours} horas`],
                ["Activos por vehículo", String(s.maxActiveTicketsPerVehicle)],
              ]}
            />
          </div>
        )}
      </section>
    </>
  );
}

// RS-05: clientes OAuth 2.0 (client credentials) para sistemas externos.
export function Integrations() {
  const load = useCallback(() => api<ClientList>("/api/integraciones/"), []);
  const clients = useLoad(load);
  const [created, setCreated] = useState<NewClient | null>(null);
  const [removing, setRemoving] = useState<Client | null>(null);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");

  async function create(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const target = event.currentTarget;
    const form = new FormData(target);
    setSaving(true);
    setError("");
    setCreated(null);
    try {
      setCreated(
        await api<NewClient>("/api/integraciones/", {
          method: "POST",
          body: JSON.stringify({
            displayName: textValue(form, "displayName"),
            role: textValue(form, "role"),
          }),
        }),
      );
      target.reset();
      clients.reload();
    } catch (e) {
      setError(explain(e));
    } finally {
      setSaving(false);
    }
  }

  return (
    <>
      <PageHeading
        eyebrow="CONFIGURACIÓN"
        title="Integraciones"
        description="Credenciales OAuth 2.0 (client credentials) para que otros sistemas consulten la API con un rol limitado."
      />
      <Messages error={error || clients.error} />
      {created && (
        <section className="result-card success" aria-live="polite">
          <p className="eyebrow">CREDENCIAL CREADA</p>
          <h2>{created.displayName}</h2>
          <p>
            Copia el secreto ahora: <strong>no se vuelve a mostrar</strong>. El
            sistema solo guarda su hash.
          </p>
          <Facts
            items={[
              ["client_id", <span key="id" className="mono">{created.clientId}</span>],
              ["Rol", created.role ?? "—"],
              [
                "Endpoint de token",
                <span key="endpoint" className="mono">{created.tokenEndpoint}</span>,
              ],
              ["Scope", <span key="scope" className="mono">{created.scope}</span>],
            ]}
          />
          <code className="secret">{created.clientSecret}</code>
        </section>
      )}
      <section className="editor">
        <div className="section-heading">
          <h2>Nueva integración</h2>
        </div>
        <form onSubmit={create}>
          <div className="form-grid">
            <label>
              Nombre del sistema
              <input name="displayName" required maxLength={100} />
            </label>
            <label>
              Rol
              <select name="role" required defaultValue="Consulta">
                <option value="Consulta">Consulta (solo lectura)</option>
                <option value="Auditor">Auditor</option>
                <option value="Supervisor">Supervisor</option>
              </select>
            </label>
          </div>
          <div className="form-actions">
            <small>Nunca se permite el rol Administrador ni Despachador.</small>
            <button className="primary" disabled={saving}>
              {saving ? "Creando…" : "Crear credencial"}
            </button>
          </div>
        </form>
      </section>
      <section className="data-panel">
        <div className="section-heading">
          <h2>
            Integraciones activas{" "}
            {clients.data && (
              <span className="count">{clients.data.items.length}</span>
            )}
          </h2>
        </div>
        {!clients.data ? (
          <Loading />
        ) : clients.data.items.length === 0 ? (
          <Empty
            title="Sin integraciones"
            text="Crea una credencial cuando un sistema externo la necesite."
          />
        ) : (
          <div className="table-scroll">
            <table>
              <thead>
                <tr>
                  <th>Nombre</th>
                  <th>client_id</th>
                  <th>Rol</th>
                  <th>Acciones</th>
                </tr>
              </thead>
              <tbody>
                {clients.data.items.map((c) => (
                  <tr key={c.clientId}>
                    <td>{c.displayName}</td>
                    <td className="mono">{c.clientId}</td>
                    <td>{c.role ?? "—"}</td>
                    <td>
                      <button
                        className="text-button"
                        onClick={() => setRemoving(c)}
                      >
                        Revocar
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>
      {removing && (
        <ConfirmDialog
          title="Revocar integración"
          description={`Se revocan los tokens emitidos y se elimina «${removing.displayName}». El sistema externo dejará de tener acceso de inmediato.`}
          confirmLabel="Revocar"
          danger
          onClose={() => setRemoving(null)}
          onConfirm={async () => {
            await api(
              `/api/integraciones/${encodeURIComponent(removing.clientId)}`,
              { method: "DELETE" },
            );
            clients.reload();
          }}
        />
      )}
    </>
  );
}
