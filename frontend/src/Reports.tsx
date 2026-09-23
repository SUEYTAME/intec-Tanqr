import { useCallback, useRef, useState } from "react";
import type { FormEvent, ReactNode } from "react";
import { api, apiBlob } from "./api";
import {
  Empty,
  Loading,
  Messages,
  PageHeading,
  Tabs,
  TicketStatus,
} from "./components";
import {
  explain,
  formatDateTime,
  formatGallons,
  formatNumber,
  loadAll,
  localDay,
  movementKindLabels,
  saveBlob,
  ticketStatusLabels,
  useCatalogs,
  useLoad,
} from "./lib";
import type { Tank } from "./lib";

type Kind = "tickets" | "despachos" | "movimientos" | "consumo";
type Format = "csv" | "xlsx" | "pdf";
type Row = Record<string, unknown>;
type Result = { items: Row[]; total: number; truncated: boolean };

const kinds: [Kind, string][] = [
  ["tickets", "Tickets"],
  ["despachos", "Despachos"],
  ["movimientos", "Movimientos de inventario"],
  ["consumo", "Consumo"],
];

const text = (value: unknown) =>
  value === null || value === undefined ? "" : String(value);
const gallons = (value: unknown) => formatGallons(value as number | null);

// Columnas visibles por reporte; las exportaciones las arma el servidor.
const columns: Record<Kind, [string, (row: Row) => ReactNode][]> = {
  tickets: [
    ["Número", (r) => <span className="mono">{text(r.number)}</span>],
    ["Estado", (r) => <TicketStatus status={text(r.status)} />],
    ["Emitido", (r) => formatDateTime(text(r.issuedAt))],
    ["Vence", (r) => formatDateTime(text(r.expiresAt))],
    ["Empleado", (r) => text(r.employeeName)],
    ["Vehículo", (r) => text(r.vehiclePlate)],
    ["Departamento", (r) => text(r.department)],
    ["Combustible", (r) => text(r.fuelType)],
    ["Autorizado", (r) => gallons(r.authorizedQuantity)],
    ["Despachado", (r) => gallons(r.dispatched)],
  ],
  despachos: [
    ["Fecha y hora", (r) => formatDateTime(text(r.occurredAt))],
    ["Ticket", (r) => <span className="mono">{text(r.ticketNumber)}</span>],
    ["Estación", (r) => text(r.station)],
    ["Tanque", (r) => text(r.tank)],
    ["Empleado", (r) => text(r.employeeName)],
    ["Vehículo", (r) => text(r.vehiclePlate)],
    ["Departamento", (r) => text(r.department)],
    ["Autorizado", (r) => gallons(r.authorizedQuantity)],
    ["Servido", (r) => gallons(r.quantity)],
    ["Diferencia", (r) => gallons(r.difference)],
  ],
  movimientos: [
    ["Fecha y hora", (r) => formatDateTime(text(r.occurredAt))],
    ["Estación", (r) => text(r.station)],
    ["Tanque", (r) => text(r.tank)],
    ["Tipo", (r) => movementKindLabels[text(r.kind)] ?? text(r.kind)],
    ["Cantidad", (r) => gallons(r.quantity)],
    ["Saldo resultante", (r) => gallons(r.balanceAfter)],
    ["Motivo", (r) => text(r.reason)],
  ],
  consumo: [
    ["Nombre", (r) => text(r.name)],
    ["Despachos", (r) => formatNumber(Number(r.count))],
    ["Volumen", (r) => gallons(r.volume)],
  ],
};

function queryOf(form: HTMLFormElement) {
  const params = new URLSearchParams();
  for (const [key, value] of new FormData(form)) {
    const v = String(value).trim();
    if (v) params.set(key, v);
  }
  return params.toString();
}

// RF-19/RF-20/RF-21: reportes filtrables y exportables a CSV, Excel y PDF.
export function Reports() {
  const [kind, setKind] = useState<Kind>("tickets");
  const [query, setQuery] = useState<string | null>(null);
  const [error, setError] = useState("");
  const [exporting, setExporting] = useState<Format | null>(null);
  const form = useRef<HTMLFormElement>(null);
  const catalogs = useCatalogs();
  const loadTanks = useCallback(() => loadAll<Tank>("tanques"), []);
  const tanks = useLoad(loadTanks);
  const loadReport = useCallback(
    () =>
      query === null
        ? Promise.resolve(null)
        : api<Result>(`/api/reportes/${kind}?${query}&format=json`),
    [kind, query],
  );
  const report = useLoad(loadReport);

  function apply(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setError("");
    setQuery(queryOf(event.currentTarget));
  }
  async function download(format: Format) {
    if (!form.current) return;
    setExporting(format);
    setError("");
    try {
      const { blob, filename } = await apiBlob(
        `/api/reportes/${kind}?${queryOf(form.current)}&format=${format}`,
      );
      saveBlob(blob, filename ?? `${kind}.${format}`);
    } catch (e) {
      setError(explain(e));
    } finally {
      setExporting(null);
    }
  }

  const today = localDay();
  const monthStart = `${today.slice(0, 8)}01`;
  const statusFilter = kind === "tickets" || kind === "despachos";
  const peopleFilter = kind !== "movimientos";

  return (
    <>
      <PageHeading
        eyebrow="REPORTES"
        title="Reportes"
        description="Filtra por periodo y criterios, revisa en pantalla y exporta a CSV, Excel o PDF. Cada exportación queda en la auditoría."
      />
      <Tabs
        label="Tipo de reporte"
        tabs={kinds}
        value={kind}
        onChange={(value) => {
          setKind(value);
          setQuery(null);
        }}
      />
      <Messages
        error={error || catalogs.error || tanks.error || report.error}
      />
      <form key={kind} ref={form} className="filters" onSubmit={apply}>
        <label>
          Desde
          <input name="from" type="date" defaultValue={monthStart} />
        </label>
        <label>
          Hasta
          <input name="to" type="date" defaultValue={today} />
        </label>
        {peopleFilter && (
          <>
            <label>
              Departamento
              <select name="departmentId" defaultValue="">
                <option value="">Todos</option>
                {catalogs.data?.departments.map((d) => (
                  <option key={d.id} value={d.id}>
                    {d.name}
                  </option>
                ))}
              </select>
            </label>
            <label>
              Vehículo
              <select name="vehicleId" defaultValue="">
                <option value="">Todos</option>
                {catalogs.data?.vehicles.map((v) => (
                  <option key={v.id} value={v.id}>
                    {v.plate}
                  </option>
                ))}
              </select>
            </label>
            <label>
              Empleado
              <select name="employeeId" defaultValue="">
                <option value="">Todos</option>
                {catalogs.data?.employees.map((e) => (
                  <option key={e.id} value={e.id}>
                    {e.fullName}
                  </option>
                ))}
              </select>
            </label>
            <label>
              Combustible
              <select name="fuelTypeId" defaultValue="">
                <option value="">Todos</option>
                {catalogs.data?.fuels.map((f) => (
                  <option key={f.id} value={f.id}>
                    {f.name}
                  </option>
                ))}
              </select>
            </label>
          </>
        )}
        {statusFilter && (
          <label>
            Estado del ticket
            <select name="status" defaultValue="">
              <option value="">Todos</option>
              {Object.entries(ticketStatusLabels).map(([key, label]) => (
                <option key={key} value={key}>
                  {label}
                </option>
              ))}
            </select>
          </label>
        )}
        {kind === "movimientos" && (
          <>
            <label>
              Tanque
              <select name="tankId" defaultValue="">
                <option value="">Todos</option>
                {tanks.data?.map((t) => (
                  <option key={t.id} value={t.id}>
                    {t.code} · {t.name}
                  </option>
                ))}
              </select>
            </label>
            <label>
              Tipo de movimiento
              <select name="kind" defaultValue="">
                <option value="">Todos</option>
                {Object.entries(movementKindLabels).map(([key, label]) => (
                  <option key={key} value={key}>
                    {label}
                  </option>
                ))}
              </select>
            </label>
          </>
        )}
        {kind === "consumo" && (
          <label>
            Agrupar por
            <select name="groupBy" defaultValue="departamento">
              <option value="departamento">Departamento</option>
              <option value="vehiculo">Vehículo</option>
            </select>
          </label>
        )}
        <button className="primary">Ver reporte</button>
      </form>
      <section className="data-panel">
        <div className="section-heading">
          <h2>
            Resultado{" "}
            {report.data && <span className="count">{report.data.total}</span>}
          </h2>
          <div className="button-row">
            {(["csv", "xlsx", "pdf"] as Format[]).map((format) => (
              <button
                key={format}
                type="button"
                disabled={exporting !== null}
                onClick={() => void download(format)}
              >
                {exporting === format
                  ? "Generando…"
                  : `Exportar ${format.toUpperCase()}`}
              </button>
            ))}
          </div>
        </div>
        {query === null ? (
          <Empty
            title="Elige los filtros"
            text="Pulsa «Ver reporte» para consultar, o exporta directamente con los filtros actuales."
          />
        ) : report.loading ? (
          <Loading text="Consultando…" />
        ) : !report.data || report.data.items.length === 0 ? (
          <Empty
            title="Sin resultados"
            text="No hay registros con estos filtros."
          />
        ) : (
          <>
            {report.data.truncated && (
              <p className="hint detail-body">
                Se muestran los primeros {report.data.items.length} de{" "}
                {report.data.total}. Exporta para obtenerlos todos.
              </p>
            )}
            <div className="table-scroll">
              <table>
                <thead>
                  <tr>
                    {columns[kind].map(([label]) => (
                      <th key={label}>{label}</th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {report.data.items.map((row, index) => (
                    <tr key={text(row.id) || index}>
                      {columns[kind].map(([label, cell]) => (
                        <td key={label}>{cell(row)}</td>
                      ))}
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </>
        )}
      </section>
    </>
  );
}
