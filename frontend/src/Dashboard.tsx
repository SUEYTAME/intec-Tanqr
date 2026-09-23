import { useCallback } from "react";
import { api } from "./api";
import { Empty, LevelBar, Loading, Messages, PageHeading } from "./components";
import {
  formatDateTime,
  formatGallons,
  formatNumber,
  useLoad,
} from "./lib";

type Bar = { name: string; volume: number; count: number };
type DashboardData = {
  generatedAt: string;
  inventory: {
    id: string;
    code: string;
    station: string;
    fuelType: string;
    balance: number;
    capacity: number;
    criticalLevel: number;
    critical: boolean;
  }[];
  dispatchedToday: number;
  dispatchedMonth: number;
  activeTickets: number;
  nearExpiryTickets: number;
  expiredTickets: number;
  expiredThisMonth: number;
  pendingRequests: number;
  byDepartment: Bar[];
  byVehicle: Bar[];
};

function Bars({ title, rows }: { title: string; rows: Bar[] }) {
  const max = Math.max(...rows.map((r) => r.volume), 0);
  return (
    <section className="data-panel chart">
      <div className="section-heading">
        <h2>{title}</h2>
      </div>
      {rows.length === 0 ? (
        <Empty
          title="Sin despachos este mes"
          text="Las barras aparecen cuando se confirman despachos."
        />
      ) : (
        <ul className="bars">
          {rows.map((row) => (
            <li key={row.name}>
              <span className="bar-label">{row.name}</span>
              <span className="bar-track" aria-hidden="true">
                <span
                  className="bar-fill"
                  style={{ width: `${max > 0 ? (row.volume / max) * 100 : 0}%` }}
                />
              </span>
              <span className="bar-value">
                {formatGallons(row.volume)}
                <small>
                  {row.count} {row.count === 1 ? "despacho" : "despachos"}
                </small>
              </span>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}

// RF-22: dashboard ejecutivo.
export function Dashboard() {
  const load = useCallback(() => api<DashboardData>("/api/dashboard"), []);
  const { data, error, loading, reload } = useLoad(load);
  const tiles: [string, string, string?][] = data
    ? [
        ["Despachado hoy", formatGallons(data.dispatchedToday)],
        ["Despachado este mes", formatGallons(data.dispatchedMonth)],
        ["Tickets activos", formatNumber(data.activeTickets)],
        [
          "Próximos a vencer",
          formatNumber(data.nearExpiryTickets),
          data.nearExpiryTickets > 0 ? "warn" : undefined,
        ],
        [
          "Vencidos este mes",
          formatNumber(data.expiredThisMonth),
          data.expiredThisMonth > 0 ? "danger" : undefined,
        ],
        ["Vencidos (total)", formatNumber(data.expiredTickets)],
        [
          "Solicitudes pendientes",
          formatNumber(data.pendingRequests),
          data.pendingRequests > 0 ? "warn" : undefined,
        ],
      ]
    : [];
  return (
    <>
      <PageHeading
        eyebrow="PANEL"
        title="Panel"
        description="Existencias, despachos y tickets de un vistazo."
      >
        <button className="subtle" onClick={reload} disabled={loading}>
          Actualizar
        </button>
      </PageHeading>
      <Messages error={error} />
      {!data ? (
        loading && <Loading text="Cargando indicadores…" />
      ) : (
        <>
          <p className="hint">
            Generado el {formatDateTime(data.generatedAt)} (hora de Santo
            Domingo).
          </p>
          <div className="kpis">
            {tiles.map(([label, value, tone]) => (
              <div key={label} className={`kpi ${tone ?? ""}`}>
                <span>{label}</span>
                <strong>{value}</strong>
              </div>
            ))}
          </div>
          <section className="data-panel">
            <div className="section-heading">
              <h2>Nivel de tanques</h2>
            </div>
            {data.inventory.length === 0 ? (
              <Empty
                title="No hay tanques activos"
                text="Registra estaciones y tanques en Inventario."
              />
            ) : (
              <ul className="tank-levels">
                {data.inventory.map((tank) => (
                  <li key={tank.id}>
                    <div>
                      <strong>{tank.code}</strong>
                      <span>
                        {tank.station} · {tank.fuelType}
                      </span>
                      {tank.critical && (
                        <span className="badge danger">Nivel crítico</span>
                      )}
                    </div>
                    <LevelBar
                      label={`Nivel del tanque ${tank.code}`}
                      balance={tank.balance}
                      capacity={tank.capacity}
                      critical={tank.critical}
                      criticalLevel={tank.criticalLevel}
                    />
                    <small>
                      {formatGallons(tank.balance)} de{" "}
                      {formatGallons(tank.capacity)} · crítico{" "}
                      {formatGallons(tank.criticalLevel)}
                    </small>
                  </li>
                ))}
              </ul>
            )}
          </section>
          <div className="two-columns">
            <Bars
              title="Consumo del mes por departamento"
              rows={data.byDepartment}
            />
            <Bars
              title="Consumo del mes por vehículo (10 mayores)"
              rows={data.byVehicle}
            />
          </div>
        </>
      )}
    </>
  );
}
