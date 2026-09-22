import { useEffect, useState } from "react";
import type { FormEvent } from "react";
import { api, sessionStore } from "./api";
import type { Session } from "./api";

type User = {
  id: string;
  email: string;
  displayName: string;
  active: boolean;
  twoFactorEnabled: boolean;
  roles: string[];
  version: string;
};
const roles = [
  "Administrador",
  "Supervisor",
  "Despachador",
  "Auditor",
  "Consulta",
];
const explain = (error: unknown) =>
  error instanceof Error ? error.message : "No se pudo completar la operación.";

export function Users() {
  const [users, setUsers] = useState<User[]>([]);
  const [revision, setRevision] = useState(0);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [editing, setEditing] = useState<User | null | undefined>(undefined);
  const [busy, setBusy] = useState(false);
  const [page, setPage] = useState(1);
  const [total, setTotal] = useState(0);
  useEffect(() => {
    let alive = true;
    api<{ items: User[]; total: number }>(`/api/usuarios/?page=${page}`)
      .then((data) => {
        if (alive) {
          setUsers(data.items);
          setTotal(data.total);
        }
      })
      .catch((e) => {
        if (alive) setError(explain(e));
      });
    return () => {
      alive = false;
    };
  }, [revision, page]);
  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setBusy(true);
    setError("");
    setNotice("");
    const data = new FormData(event.currentTarget);
    try {
      if (editing) {
        await api(`/api/usuarios/${editing.id}`, {
          method: "PUT",
          body: JSON.stringify({
            email: data.get("email"),
            displayName: data.get("displayName"),
            role: data.get("role"),
            active: data.get("active") === "on",
            version: editing.version,
          }),
        });
      } else {
        await api("/api/usuarios/", {
          method: "POST",
          body: JSON.stringify({
            email: data.get("email"),
            displayName: data.get("displayName"),
            role: data.get("role"),
            password: data.get("password"),
          }),
        });
      }
      setEditing(undefined);
      setRevision((x) => x + 1);
      setNotice(
        "Usuario guardado. Los cambios de acceso invalidan sus sesiones anteriores.",
      );
    } catch (e) {
      setError(explain(e));
    } finally {
      setBusy(false);
    }
  }
  async function reset(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!editing) return;
    setBusy(true);
    setError("");
    setNotice("");
    const form = event.currentTarget;
    try {
      await api(`/api/usuarios/${editing.id}/password`, {
        method: "POST",
        body: JSON.stringify({ password: new FormData(form).get("password") }),
      });
      form.reset();
      setNotice(
        "Contraseña restablecida. Comparte la nueva contraseña por un canal seguro.",
      );
    } catch (e) {
      setError(explain(e));
    } finally {
      setBusy(false);
    }
  }
  return (
    <>
      <div className="page-heading">
        <div>
          <p className="eyebrow">ADMINISTRACIÓN DE ACCESO</p>
          <h1>Usuarios</h1>
          <p>Crea cuentas y asigna solo los permisos necesarios.</p>
        </div>
        <button
          className="primary"
          onClick={() => {
            setEditing(null);
            setError("");
            setNotice("");
          }}
        >
          + Nuevo usuario
        </button>
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
            <h2>{editing ? "Editar usuario" : "Nuevo usuario"}</h2>
            <button
              className="subtle"
              disabled={busy}
              onClick={() => setEditing(undefined)}
            >
              Cancelar
            </button>
          </div>
          <form key={editing?.id ?? "new"} onSubmit={save}>
            <div className="form-grid">
              <label>
                Nombre
                <input
                  name="displayName"
                  required
                  maxLength={150}
                  defaultValue={editing?.displayName}
                />
              </label>
              <label>
                Correo
                <input
                  name="email"
                  type="email"
                  required
                  maxLength={254}
                  defaultValue={editing?.email}
                />
              </label>
              <label>
                Rol
                <select
                  name="role"
                  defaultValue={editing?.roles[0] ?? "Consulta"}
                >
                  {roles.map((role) => (
                    <option key={role}>{role}</option>
                  ))}
                </select>
              </label>
              {!editing && (
                <label>
                  Contraseña inicial
                  <input
                    name="password"
                    type="password"
                    required
                    minLength={15}
                    maxLength={128}
                    autoComplete="new-password"
                  />
                </label>
              )}
            </div>
            <div className="form-actions">
              <label className="check">
                <input
                  type="checkbox"
                  name="active"
                  defaultChecked={editing?.active ?? true}
                  disabled={!editing}
                />{" "}
                Cuenta activa
              </label>
              <button className="primary" disabled={busy}>
                Guardar usuario
              </button>
            </div>
          </form>
          {editing && (
            <form onSubmit={reset}>
              <label>
                Nueva contraseña (restablecimiento)
                <input
                  name="password"
                  type="password"
                  minLength={15}
                  maxLength={128}
                  autoComplete="new-password"
                  required
                />
              </label>
              <button style={{ marginTop: 14 }} disabled={busy}>
                Restablecer contraseña
              </button>
            </form>
          )}
        </section>
      )}
      <section className="data-panel">
        <div className="table-scroll">
          <table>
            <thead>
              <tr>
                <th>Nombre</th>
                <th>Correo</th>
                <th>Rol</th>
                <th>Estado</th>
                <th>Acciones</th>
              </tr>
            </thead>
            <tbody>
              {users.map((user) => (
                <tr key={user.id}>
                  <td>{user.displayName}</td>
                  <td>{user.email}</td>
                  <td>{user.roles.join(", ")}</td>
                  <td>
                    <span className={`badge ${user.active ? "" : "inactive"}`}>
                      {user.active ? "Activo" : "Inactivo"}
                    </span>
                  </td>
                  <td>
                    {user.id !== sessionStore.get()?.userId ? (
                      <button
                        className="text-button"
                        onClick={() => {
                          setEditing(user);
                          setError("");
                          setNotice("");
                        }}
                      >
                        Editar
                      </button>
                    ) : (
                      <small>Tu cuenta</small>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        <div className="pagination">
          <span>
            {total} usuarios · Página {page}
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

export function Account() {
  const [mfa, setMfa] = useState<boolean | null>(null);
  const [secret, setSecret] = useState("");
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  const [complete, setComplete] = useState(false);
  const [recoveryCodes, setRecoveryCodes] = useState<string[]>([]);
  useEffect(() => {
    let alive = true;
    api<{ twoFactorEnabled: boolean }>("/api/auth/me")
      .then((data) => {
        if (alive) setMfa(data.twoFactorEnabled);
      })
      .catch((e) => {
        if (alive) setError(explain(e));
      });
    return () => {
      alive = false;
    };
  }, []);
  async function enroll(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setBusy(true);
    setError("");
    const data = new FormData(event.currentTarget);
    try {
      if (!secret) {
        const result = await api<{ secret: string; session: Session }>(
          "/api/auth/mfa/setup",
          {
            method: "POST",
            body: JSON.stringify({ password: data.get("password") }),
          },
        );
        sessionStore.set(result.session);
        setSecret(result.secret);
      } else {
        const result = await api<{ recoveryCodes: string[] }>(
          "/api/auth/mfa/enable",
          {
            method: "POST",
            body: JSON.stringify({
              password: data.get("password"),
              code: data.get("code"),
            }),
          },
        );
        setRecoveryCodes(result.recoveryCodes);
        setSecret("");
        setComplete(true);
      }
    } catch (e) {
      setError(explain(e));
    } finally {
      setBusy(false);
    }
  }
  return (
    <>
      <div className="page-heading">
        <div>
          <p className="eyebrow">MI CUENTA</p>
          <h1>Seguridad</h1>
          <p>Protege tu acceso con un código de tu aplicación autenticadora.</p>
        </div>
      </div>
      {error && (
        <p role="alert" className="error">
          {error}
        </p>
      )}
      <section className="editor">
        <div className="section-heading">
          <h2>Autenticación en dos pasos</h2>
          <span className={`badge ${mfa ? "" : "inactive"}`}>
            {mfa === null ? "Consultando…" : mfa ? "Activada" : "No activada"}
          </span>
        </div>
        {complete ? (
          <div style={{ padding: 24 }}>
            <p className="notice">
              Autenticación activada. Vuelve a ingresar con tu contraseña y un
              código.
            </p>
            <p>
              Guarda estos códigos en un lugar seguro. Cada uno permite un solo
              acceso de recuperación y se muestra únicamente ahora.
            </p>
            <pre className="setup-key">{recoveryCodes.join("\n")}</pre>
            <button className="primary" onClick={() => sessionStore.set(null)}>
              He guardado los códigos · Iniciar sesión
            </button>
          </div>
        ) : (
          mfa === false && (
            <form onSubmit={enroll}>
              <p>
                Necesitas una aplicación compatible con TOTP. Conserva acceso a
                ella para poder ingresar.
              </p>
              <label>
                Confirma tu contraseña
                <input
                  name="password"
                  type="password"
                  autoComplete="current-password"
                  required
                  maxLength={128}
                />
              </label>
              {secret && (
                <>
                  <p style={{ marginTop: 20 }}>
                    Añade una cuenta con esta clave en tu autenticador:
                  </p>
                  <code className="setup-key">{secret}</code>
                  <label>
                    Código de seis dígitos
                    <input
                      name="code"
                      inputMode="numeric"
                      pattern="[0-9]{6}"
                      autoComplete="one-time-code"
                      required
                    />
                  </label>
                </>
              )}
              <button
                className="primary"
                style={{ marginTop: 20 }}
                disabled={busy}
              >
                {busy
                  ? "Procesando…"
                  : secret
                    ? "Confirmar y activar"
                    : "Preparar autenticador"}
              </button>
            </form>
          )
        )}
      </section>
    </>
  );
}
