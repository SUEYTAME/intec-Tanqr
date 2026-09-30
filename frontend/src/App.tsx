import { useCallback, useEffect, useState, useSyncExternalStore } from "react";
import type { FormEvent } from "react";
import { api, login, logout, sessionStore } from "./api";
import { Users, Account } from "./Administration";
import { Catalog } from "./Catalogs";
import { DailyClose } from "./DailyClose";
import { Dashboard } from "./Dashboard";
import { Dispatch } from "./Dispatch";
import { Inventory } from "./Inventory";
import { Notifications } from "./Notifications";
import { Reports } from "./Reports";
import { Requests } from "./Requests";
import { Schedules } from "./Schedules";
import { Integrations, Settings } from "./Settings";
import { Tickets } from "./Tickets";
import {
  auditActionLabels,
  auditActorLabels,
  auditEntityLabels,
  explain,
  formatDateTime,
  hasRole,
  labelOf,
  saveBlob,
  useOnline,
} from "./lib";
import "./App.css";
import "./screens.css";

type Page =
  | "tablero"
  | "solicitudes"
  | "tickets"
  | "programaciones"
  | "despacho"
  | "cierre"
  | "inventario"
  | "reportes"
  | "notificaciones"
  | "departamentos"
  | "empleados"
  | "vehiculos"
  | "parametros"
  | "integraciones"
  | "auditoria"
  | "usuarios"
  | "seguridad";
type NavItem = { page: Page; label: string; visible: boolean };

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
      setError(explain(e));
    } finally {
      setBusy(false);
    }
  }
  return (
    <main className="login-layout">
      <section className="login-intro">
        <div className="brand">
          INTEC<span>TanQR</span>
        </div>
        <div>
          <p className="eyebrow">GESTIÓN INSTITUCIONAL</p>
          <h1>
            El control empieza
            <br />
            con un buen registro.
          </h1>
          <p>
            Solicitudes, tickets con QR firmado, despacho, inventario y
            reportes en un solo lugar.
          </p>
        </div>
        <small>TanQR · Tickets digitales de combustible</small>
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

// RS-06: bitácora encadenada y ancla firmada para guardar fuera del sistema.
function Audit() {
  const [rows, setRows] = useState<Record<string, string>[]>([]);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [page, setPage] = useState(1);
  const [total, setTotal] = useState(0);
  const [busy, setBusy] = useState(false);
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
        if (alive) setError(explain(e));
      });
    return () => {
      alive = false;
    };
  }, [page]);
  async function anchor() {
    setBusy(true);
    setError("");
    setNotice("");
    try {
      const data = await api<{ lastId: number; issuedAt: number }>(
        "/api/auditoria/ancla",
      );
      saveBlob(
        new Blob([JSON.stringify(data, null, 2)], {
          type: "application/json",
        }),
        `ancla-auditoria-${data.lastId}.json`,
      );
      setNotice(
        `Ancla firmada hasta el evento ${data.lastId} (${formatDateTime(new Date(data.issuedAt).toISOString())}). Guárdala fuera del sistema: correo, papel o almacenamiento de solo escritura.`,
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
          <p className="eyebrow">CONTROL Y SEGUIMIENTO</p>
          <h1>Auditoría</h1>
          <p>
            Accesos y cambios registrados por el sistema, encadenados con hash.
          </p>
        </div>
        <button onClick={() => void anchor()} disabled={busy}>
          {busy ? "Firmando…" : "Descargar ancla firmada"}
        </button>
      </div>
      {error && (
        <p role="alert" className="error">
          {error}
        </p>
      )}
      {notice && (
        <p role="status" className="notice">
          {notice}
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
                  <td>{formatDateTime(row.occurredAt)}</td>
                  <td>{labelOf(auditActionLabels, row.action)}</td>
                  <td>{labelOf(auditEntityLabels, row.entity)}</td>
                  <td className="mono">
                    {labelOf(auditActorLabels, row.actor)}
                  </td>
                  <td>{row.ip === "local" ? "Servidor" : row.ip}</td>
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

// Alertas sin leer para el contador del menú; se refresca cada minuto.
// Si la consulta falla, el contador muestra "?" (la pantalla de notificaciones da el detalle).
function useUnread(enabled: boolean) {
  const [unread, setUnread] = useState<number | null>(0);
  const [revision, setRevision] = useState(0);
  useEffect(() => {
    if (!enabled) return;
    let alive = true;
    const read = () =>
      api<{ unread: number }>("/api/notificaciones/?unread=true").then(
        (data) => {
          if (alive) setUnread(data.unread);
        },
        () => {
          if (alive) setUnread(null);
        },
      );
    void read();
    const timer = window.setInterval(() => void read(), 60000);
    return () => {
      alive = false;
      window.clearInterval(timer);
    };
  }, [enabled, revision]);
  const refresh = useCallback(() => setRevision((x) => x + 1), []);
  return { unread, refresh };
}

export default function App() {
  const session = useSyncExternalStore(
    sessionStore.subscribe,
    sessionStore.get,
  );
  const online = useOnline();
  const [page, setPage] = useState<Page | null>(null);
  const [error, setError] = useState("");
  const roles = session?.roles ?? [];
  const admin = hasRole(roles, "Administrador");
  const manager = hasRole(roles, "Administrador", "Supervisor");
  // Solo el Despachador despacha: quien aprueba no despacha (ADR-016).
  const operator = hasRole(roles, "Despachador");
  const auditor = hasRole(roles, "Administrador", "Auditor");
  const dashboard = hasRole(roles, "Administrador", "Supervisor", "Auditor");
  const reporter = hasRole(roles, "Administrador", "Supervisor", "Auditor");
  const requester = hasRole(roles, "Administrador", "Supervisor", "Consulta");
  const ticketReader = hasRole(
    roles,
    "Administrador",
    "Supervisor",
    "Despachador",
    "Consulta",
  );
  const notifications = useUnread(session !== null && manager);
  if (!session) return <Login />;

  const unreadLabel =
    notifications.unread === null
      ? " (?)"
      : notifications.unread > 0
        ? ` (${notifications.unread})`
        : "";

  const groups: [string, NavItem[]][] = [
    [
      "OPERACIÓN",
      [
        { page: "tablero", label: "Tablero", visible: dashboard },
        { page: "solicitudes", label: "Solicitudes", visible: requester },
        { page: "tickets", label: "Tickets", visible: ticketReader },
        { page: "programaciones", label: "Programaciones", visible: manager },
        { page: "despacho", label: "Despacho", visible: operator },
        { page: "cierre", label: "Cierre diario", visible: operator },
        { page: "inventario", label: "Inventario", visible: manager },
        { page: "reportes", label: "Reportes", visible: reporter },
        {
          page: "notificaciones",
          label: `Notificaciones${unreadLabel}`,
          visible: manager,
        },
      ],
    ],
    [
      "CATÁLOGOS",
      [
        { page: "departamentos", label: "Departamentos", visible: manager },
        { page: "empleados", label: "Empleados", visible: manager },
        { page: "vehiculos", label: "Vehículos", visible: manager },
      ],
    ],
    [
      "ADMINISTRACIÓN",
      [
        { page: "parametros", label: "Parámetros", visible: admin },
        { page: "integraciones", label: "Integraciones", visible: admin },
        { page: "auditoria", label: "Auditoría", visible: auditor },
        { page: "usuarios", label: "Usuarios", visible: admin },
        { page: "seguridad", label: "Mi seguridad", visible: true },
      ],
    ],
  ];
  const allowed = new Set(
    groups.flatMap(([, items]) =>
      items.filter((i) => i.visible).map((i) => i.page),
    ),
  );
  const fallback: Page = operator
    ? "despacho"
    : dashboard
      ? "tablero"
      : requester
        ? "solicitudes"
        : "seguridad";
  const current = page && allowed.has(page) ? page : fallback;

  function screen() {
    switch (current) {
      case "tablero":
        return <Dashboard />;
      case "solicitudes":
        return <Requests canCreate={requester} canApprove={manager} />;
      case "tickets":
        return <Tickets canManage={manager} />;
      case "programaciones":
        return <Schedules canWrite={manager} />;
      case "despacho":
        return <Dispatch />;
      case "cierre":
        return <DailyClose />;
      case "inventario":
        return <Inventory canWrite={manager} />;
      case "reportes":
        return <Reports />;
      case "notificaciones":
        return <Notifications onChange={notifications.refresh} />;
      case "departamentos":
      case "empleados":
      case "vehiculos":
        return <Catalog key={current} section={current} canWrite={manager} />;
      case "parametros":
        return <Settings canWrite={admin} />;
      case "integraciones":
        return <Integrations />;
      case "auditoria":
        return <Audit />;
      case "usuarios":
        return <Users />;
      case "seguridad":
        return <Account />;
    }
  }

  return (
    <div className="shell">
      <aside className="sidebar">
        <div className="brand">
          INTEC<span>TanQR</span>
        </div>
        <nav aria-label="Navegación principal">
          {groups.filter(([, items]) => items.some((item) => item.visible)).map(([title, items]) => (
            <div key={title} className="nav-group">
              <p className="nav-label">{title}</p>
              {items
                .filter((item) => item.visible)
                .map((item) => (
                  <button
                    key={item.page}
                    aria-current={current === item.page ? "page" : undefined}
                    onClick={() => setPage(item.page)}
                  >
                    {item.label}
                  </button>
                ))}
            </div>
          ))}
        </nav>
        <div className="sidebar-bottom">
          <span className="online-dot" />{" "}
          {online ? "Conectado" : "Sin conexión"}
        </div>
      </aside>
      <div className="workspace">
        {!online && (
          <p className="offline-banner app-offline" role="alert">
            <strong>Sin conexión.</strong> Nada se guarda sin internet: las
            operaciones fallarán hasta que vuelva la conexión.
          </p>
        )}
        <header>
          <span>TanQR · Gestión de combustible</span>
          <div>
            <span>
              {session.displayName}
              <small>{roles.join(" · ")}</small>
            </span>
            <button
              className="subtle"
              onClick={() => {
                void logout()
                  .then(() => {
                    setPage(null);
                    setError("");
                  })
                  .catch((e) => setError(explain(e)));
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
          {screen()}
        </main>
        <footer>
          INTEC · TanQR
          <span>Trazabilidad completa de cada galón</span>
        </footer>
      </div>
    </div>
  );
}
