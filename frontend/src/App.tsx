import { useEffect, useState, useSyncExternalStore } from "react";
import type { FormEvent } from "react";
import { api, login, logout, sessionStore } from "./api";
import { Users, Account } from "./Administration";
import "./App.css";

type Row = Record<string, string | number | boolean> & {
  id: string;
  version: string;
  active: boolean;
};
type Field = { key: string; label: string; type?: string; max?: number };
const sections = {
  departamentos: {
    title: "Departamentos",
    description:
      "Organiza las unidades a las que pertenecen empleados y vehículos.",
    fields: [
      { key: "code", label: "Código", max: 30 },
      { key: "name", label: "Nombre", max: 150 },
    ],
  },
  empleados: {
    title: "Empleados",
    description:
      "Mantén el registro de las personas autorizadas de la institución.",
    fields: [
      { key: "code", label: "Código", max: 30 },
      { key: "fullName", label: "Nombre completo", max: 200 },
      { key: "nationalId", label: "Cédula (11 dígitos)", max: 11 },
      { key: "departmentId", label: "Departamento", type: "department" },
      { key: "position", label: "Cargo", max: 100 },
      { key: "email", label: "Correo", type: "email", max: 254 },
      { key: "mobile", label: "Teléfono móvil", type: "tel", max: 25 },
    ],
  },
  vehiculos: {
    title: "Vehículos",
    description:
      "Identifica cada vehículo y conserva sus datos de capacidad y kilometraje.",
    fields: [
      { key: "plate", label: "Placa", max: 20 },
      { key: "internalCode", label: "Ficha", max: 30 },
      { key: "make", label: "Marca", max: 80 },
      { key: "model", label: "Modelo", max: 80 },
      { key: "year", label: "Año", type: "number" },
      { key: "kind", label: "Tipo", max: 80 },
      { key: "departmentId", label: "Departamento", type: "department" },
      { key: "tankCapacity", label: "Capacidad (galones)", type: "number" },
      { key: "odometer", label: "Odómetro (km)", type: "number" },
    ],
  },
} satisfies Record<
  string,
  { title: string; description: string; fields: Field[] }
>;
type Section = keyof typeof sections;
const message = (error: unknown) =>
  error instanceof Error
    ? error.message
    : "No se pudo conectar con el servidor.";

function Login() {
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setBusy(true);
    setError("");
    const form = new FormData(event.currentTarget);
    try {
      await login(
        String(form.get("email")),
        String(form.get("password")),
        String(form.get("code")),
        String(form.get("recovery") ?? ""),
      );
    } catch (e) {
      setError(message(e));
    } finally {
      setBusy(false);
    }
  }
  return (
    <main className="login-layout">
      <section className="login-intro">
        <div className="brand">
          INTEC<span>COMBUSTIBLE</span>
        </div>
        <div>
          <p className="eyebrow">GESTIÓN INSTITUCIONAL</p>
          <h1>
            El control empieza
            <br />
            con un buen registro.
          </h1>
          <p>
            Empleados, vehículos y departamentos, conectados en un solo lugar.
          </p>
        </div>
        <small>Plataforma de tickets digitales de combustible</small>
      </section>
      <section className="login-panel">
        <form onSubmit={submit}>
          <p className="eyebrow">ACCESO AL SISTEMA</p>
          <h2>Bienvenido</h2>
          <p>Ingresa con tu cuenta institucional.</p>
          <label>
            Correo electrónico
            <input name="email" type="email" autoComplete="username" required />
          </label>
          <label>
            Contraseña
            <input
              name="password"
              type="password"
              autoComplete="current-password"
              maxLength={128}
              required
            />
          </label>
          <details>
            <summary>Usar código de autenticación</summary>
            <label>
              Código MFA
              <input
                name="code"
                inputMode="numeric"
                autoComplete="one-time-code"
                maxLength={6}
              />
            </label>
            <label>
              Código de recuperación (si no tienes el autenticador)
              <input name="recovery" autoComplete="off" maxLength={30} />
            </label>
          </details>
          {error && (
            <p role="alert" className="error">
              {error}
            </p>
          )}
          <button className="primary" disabled={busy}>
            {busy ? "Ingresando…" : "Iniciar sesión"}
            <span aria-hidden="true">→</span>
          </button>
          <small>La sesión se cierra al recargar o cerrar esta pestaña.</small>
        </form>
      </section>
    </main>
  );
}

function Catalog({
  section,
  canWrite,
}: {
  section: Section;
  canWrite: boolean;
}) {
  const schema = sections[section];
  const [rows, setRows] = useState<Row[]>([]);
  const [departments, setDepartments] = useState<Row[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [editing, setEditing] = useState<Row | null | undefined>(undefined);
  const [saving, setSaving] = useState(false);
  const [revision, setRevision] = useState(0);
  useEffect(() => {
    let alive = true;
    api<{ items: Row[]; total: number }>(`/api/${section}/?page=${page}`)
      .then((data) => {
        if (alive) {
          setRows(data.items);
          setTotal(data.total);
        }
      })
      .catch((e) => {
        if (alive) setError(message(e));
      })
      .finally(() => {
        if (alive) setLoading(false);
      });
    return () => {
      alive = false;
    };
  }, [section, page, revision]);
  useEffect(() => {
    if (section === "departamentos") return;
    let alive = true;
    async function loadDepartments() {
      const all: Row[] = [];
      for (let p = 1; ; p++) {
        const data = await api<{ items: Row[]; total: number }>(
          `/api/departamentos/?page=${p}&pageSize=100`,
        );
        all.push(...data.items);
        if (all.length >= data.total) break;
      }
      if (alive) setDepartments(all);
    }
    void loadDepartments().catch((e) => {
      if (alive) setError(message(e));
    });
    return () => {
      alive = false;
    };
  }, [section, revision]);
  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setSaving(true);
    setError("");
    setNotice("");
    const data = new FormData(event.currentTarget);
    const body: Record<string, unknown> = {
      active: data.get("active") === "on",
    };
    for (const field of schema.fields as Field[])
      body[field.key] =
        field.type === "number"
          ? Number(data.get(field.key))
          : String(data.get(field.key));
    try {
      await api(`/api/${section}/${editing?.id ?? ""}`, {
        method: editing ? "PUT" : "POST",
        headers: editing ? { "If-Match": `"${editing.version}"` } : {},
        body: JSON.stringify(body),
      });
      setEditing(undefined);
      setRevision((x) => x + 1);
      setNotice("Registro guardado. La operación quedó en la auditoría.");
    } catch (e) {
      setError(message(e));
    } finally {
      setSaving(false);
    }
  }
  return (
    <>
      <div className="page-heading">
        <div>
          <p className="eyebrow">CATÁLOGOS / {schema.title.toUpperCase()}</p>
          <h1>{schema.title}</h1>
          <p>{schema.description}</p>
        </div>
        {canWrite && (
          <button
            className="primary"
            onClick={() => {
              setEditing(null);
              setError("");
              setNotice("");
            }}
          >
            + Nuevo registro
          </button>
        )}
      </div>
      {error && (
        <p className="error" role="alert">
          {error}
        </p>
      )}
      {notice && (
        <p className="notice" role="status">
          {notice}
        </p>
      )}
      {editing !== undefined && (
        <section className="editor">
          <div className="section-heading">
            <h2>{editing ? "Editar registro" : "Nuevo registro"}</h2>
            <button
              className="subtle"
              onClick={() => setEditing(undefined)}
              disabled={saving}
            >
              Cancelar
            </button>
          </div>
          <form onSubmit={save} key={editing?.id ?? "new"}>
            <div className="form-grid">
              {(schema.fields as Field[]).map((field) => (
                <label key={field.key}>
                  {field.label}
                  {field.type === "department" ? (
                    <select
                      name={field.key}
                      required
                      defaultValue={String(editing?.[field.key] ?? "")}
                    >
                      <option value="">Selecciona un departamento</option>
                      {departments
                        .filter(
                          (d) => d.active || d.id === editing?.departmentId,
                        )
                        .map((d) => (
                          <option key={d.id} value={d.id}>
                            {String(d.name)}
                            {d.active ? "" : " (inactivo)"}
                          </option>
                        ))}
                    </select>
                  ) : (
                    <input
                      name={field.key}
                      type={field.type ?? "text"}
                      required
                      maxLength={field.max}
                      defaultValue={String(editing?.[field.key] ?? "")}
                      min={
                        field.key === "year"
                          ? 1900
                          : field.key === "tankCapacity"
                            ? 0.001
                            : field.type === "number"
                              ? 0
                              : undefined
                      }
                      max={field.key === "year" ? 2200 : undefined}
                      step={
                        field.key === "tankCapacity"
                          ? 0.001
                          : field.type === "number"
                            ? 1
                            : undefined
                      }
                    />
                  )}
                </label>
              ))}
            </div>
            <div className="form-actions">
              <label className="check">
                <input
                  name="active"
                  type="checkbox"
                  defaultChecked={editing?.active ?? true}
                />{" "}
                Registro activo
              </label>
              <button className="primary" disabled={saving}>
                {saving ? "Guardando…" : "Guardar registro"}
              </button>
            </div>
          </form>
        </section>
      )}
      <section className="data-panel">
        <div className="section-heading">
          <h2>
            Registros <span className="count">{total}</span>
          </h2>
          <button
            className="subtle"
            onClick={() => setRevision((x) => x + 1)}
            disabled={loading}
          >
            Actualizar
          </button>
        </div>
        {loading ? (
          <p className="empty" role="status">
            Cargando registros…
          </p>
        ) : rows.length === 0 ? (
          <div className="empty">
            <strong>No hay registros todavía</strong>
            <p>
              {canWrite
                ? "Crea el primer registro para empezar."
                : "Los registros aparecerán cuando un usuario autorizado los cree."}
            </p>
          </div>
        ) : (
          <div className="table-scroll">
            <table>
              <thead>
                <tr>
                  {schema.fields.slice(0, 3).map((f) => (
                    <th key={f.key}>{f.label}</th>
                  ))}
                  <th>Estado</th>
                  {canWrite && <th>Acciones</th>}
                </tr>
              </thead>
              <tbody>
                {rows.map((row) => (
                  <tr key={row.id}>
                    {schema.fields.slice(0, 3).map((f) => (
                      <td key={f.key}>{String(row[f.key])}</td>
                    ))}
                    <td>
                      <span className={`badge ${row.active ? "" : "inactive"}`}>
                        {row.active ? "Activo" : "Inactivo"}
                      </span>
                    </td>
                    {canWrite && (
                      <td>
                        <button
                          className="text-button"
                          onClick={() => {
                            setEditing(row);
                            setError("");
                            setNotice("");
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
        <div className="pagination">
          <span>
            Página {page} · {total} registros
          </span>
          <div>
            <button
              disabled={page === 1 || loading}
              onClick={() => setPage((x) => x - 1)}
            >
              Anterior
            </button>
            <button
              disabled={page * 50 >= total || loading}
              onClick={() => setPage((x) => x + 1)}
            >
              Siguiente
            </button>
          </div>
        </div>
      </section>
    </>
  );
}

function Audit() {
  const [rows, setRows] = useState<Record<string, string>[]>([]);
  const [error, setError] = useState("");
  const [page, setPage] = useState(1);
  const [total, setTotal] = useState(0);
  useEffect(() => {
    let alive = true;
    api<{ items: Record<string, string>[]; total: number }>(
      `/api/auditoria?page=${page}`,
    )
      .then((data) => {
        if (alive) {
          setRows(data.items);
          setTotal(data.total);
        }
      })
      .catch((e) => {
        if (alive) setError(message(e));
      });
    return () => {
      alive = false;
    };
  }, [page]);
  return (
    <>
      <div className="page-heading">
        <div>
          <p className="eyebrow">CONTROL Y SEGUIMIENTO</p>
          <h1>Auditoría</h1>
          <p>Accesos y cambios registrados por el sistema.</p>
        </div>
      </div>
      {error && (
        <p role="alert" className="error">
          {error}
        </p>
      )}
      <section className="data-panel">
        <div className="table-scroll">
          <table>
            <thead>
              <tr>
                <th>Fecha y hora</th>
                <th>Acción</th>
                <th>Entidad</th>
                <th>Actor</th>
                <th>IP</th>
              </tr>
            </thead>
            <tbody>
              {rows.map((row) => (
                <tr key={row.id}>
                  <td>
                    {new Date(row.occurredAt).toLocaleString("es-DO", {
                      timeZone: "America/Santo_Domingo",
                    })}
                  </td>
                  <td>{row.action}</td>
                  <td>{row.entity}</td>
                  <td className="mono">{row.actor}</td>
                  <td>{row.ip}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        <div className="pagination">
          <span>
            {total} eventos · Página {page}
          </span>
          <div>
            <button disabled={page === 1} onClick={() => setPage((x) => x - 1)}>
              Anterior
            </button>
            <button
              disabled={page * 50 >= total}
              onClick={() => setPage((x) => x + 1)}
            >
              Siguiente
            </button>
          </div>
        </div>
      </section>
    </>
  );
}

export default function App() {
  const session = useSyncExternalStore(
    sessionStore.subscribe,
    sessionStore.get,
  );
  const [section, setSection] = useState<
    Section | "auditoria" | "usuarios" | "seguridad"
  >("departamentos");
  const [error, setError] = useState("");
  if (!session) return <Login />;
  const canWrite = session.roles.some((role) =>
    ["Administrador", "Supervisor"].includes(role),
  );
  const canAudit = session.roles.some((role) =>
    ["Administrador", "Auditor"].includes(role),
  );
  return (
    <div className="shell">
      <aside className="sidebar">
        <div className="brand">
          INTEC<span>COMBUSTIBLE</span>
        </div>
        <p className="nav-label">ADMINISTRACIÓN</p>
        <nav aria-label="Navegación principal">
          {Object.entries(sections).map(([key, value]) => (
            <button
              key={key}
              aria-current={section === key ? "page" : undefined}
              onClick={() => setSection(key as Section)}
            >
              {value.title}
            </button>
          ))}
          {canAudit && (
            <button
              aria-current={section === "auditoria" ? "page" : undefined}
              onClick={() => setSection("auditoria")}
            >
              Auditoría
            </button>
          )}
          {session.roles.includes("Administrador") && (
            <button
              aria-current={section === "usuarios" ? "page" : undefined}
              onClick={() => setSection("usuarios")}
            >
              Usuarios
            </button>
          )}
          <button
            aria-current={section === "seguridad" ? "page" : undefined}
            onClick={() => setSection("seguridad")}
          >
            Mi seguridad
          </button>
        </nav>
        <div className="sidebar-bottom">
          <span className="online-dot" /> Entorno de desarrollo
        </div>
      </aside>
      <div className="workspace">
        <header>
          <span>Plataforma de gestión de combustible</span>
          <div>
            <span>
              {session.displayName}
              <small>{session.roles.join(" · ")}</small>
            </span>
            <button
              className="subtle"
              onClick={() => {
                void logout()
                  .then(() => {
                    setSection("departamentos");
                    setError("");
                  })
                  .catch((e) => setError(message(e)));
              }}
            >
              Cerrar sesión
            </button>
          </div>
        </header>
        <main className="content">
          {error && (
            <p className="error" role="alert">
              {error}
            </p>
          )}
          {section === "usuarios" && session.roles.includes("Administrador") ? (
            <Users />
          ) : section === "seguridad" ? (
            <Account />
          ) : section === "auditoria" && canAudit ? (
            <Audit />
          ) : (
            <Catalog
              key={section}
              section={
                section in sections ? (section as Section) : "departamentos"
              }
              canWrite={canWrite}
            />
          )}
        </main>
        <footer>
          INTEC · Gestión de combustible
          <span>Administración y trazabilidad</span>
        </footer>
      </div>
    </div>
  );
}
