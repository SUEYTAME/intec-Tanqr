import { useCallback, useState } from "react";
import type { FormEvent } from "react";
import { api } from "./api";
import { Catalog } from "./Catalogs";
import {
  Badge,
  ConfirmDialog,
  Empty,
  LevelBar,
  Loading,
  Messages,
  PageHeading,
  Pager,
  Tabs,
} from "./components";
import {
  explain,
  formatDateTime,
  formatGallons,
  localDay,
  movementKindLabels,
  numberValue,
  textValue,
  useLoad,
} from "./lib";

type StockTank = {
  id: string;
  code: string;
  active: boolean;
  capacity: number;
  balance: number;
  criticalLevel: number;
  stationId: string;
  station: string;
  fuelTypeId: string;
  fuelType: string;
  critical: boolean;
  freeSpace: number;
  dailyConsumption: number;
  monthlyConsumption: number;
};
type Stock = {
  tanks: StockTank[];
  fuels: {
    fuelTypeId: string;
    fuelType: string;
    stock: number;
    committed: number;
    available: number;
  }[];
};
type Movement = {
  id: number;
  tank: string;
  station: string;
  fuelType: string;
  kind: string;
  quantity: number;
  balanceAfter: number;
  occurredAt: string;
  actor: string;
  reason: string;
};
type Receipt = {
  id: string;
  kind: string;
  supplierRnc: string;
  supplierName: string;
  invoice: string;
  quantity: number;
  receivedOn: string;
  tank: string;
  recordedAt: string;
};
type Tab =
  | "existencias"
  | "recepciones"
  | "transferencias"
  | "ajustes"
  | "movimientos"
  | "combustibles"
  | "estaciones"
  | "tanques";

const tankLabel = (t: StockTank) =>
  `${t.code} · ${t.station} · ${t.fuelType} · ${formatGallons(t.balance)}`;

function Existences({ stock }: { stock: Stock }) {
  return (
    <>
      <section className="data-panel">
        <div className="section-heading">
          <h2>Disponibilidad por combustible</h2>
        </div>
        {stock.fuels.length === 0 ? (
          <Empty
            title="Sin tanques activos"
            text="Crea combustibles, estaciones y tanques en las pestañas de catálogo."
          />
        ) : (
          <div className="table-scroll">
            <table>
              <thead>
                <tr>
                  <th>Combustible</th>
                  <th>Existencia</th>
                  <th>Comprometido en tickets activos</th>
                  <th>Disponible</th>
                </tr>
              </thead>
              <tbody>
                {stock.fuels.map((f) => (
                  <tr key={f.fuelTypeId}>
                    <td>{f.fuelType}</td>
                    <td>{formatGallons(f.stock)}</td>
                    <td>{formatGallons(f.committed)}</td>
                    <td className={f.available < 0 ? "negative" : ""}>
                      {formatGallons(f.available)}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>
      <section className="data-panel">
        <div className="section-heading">
          <h2>Tanques</h2>
        </div>
        {stock.tanks.length === 0 ? (
          <Empty title="No hay tanques" text="Registra tanques en la pestaña Tanques." />
        ) : (
          <div className="table-scroll">
            <table>
              <thead>
                <tr>
                  <th>Tanque</th>
                  <th>Estación</th>
                  <th>Combustible</th>
                  <th>Nivel</th>
                  <th>Existencia</th>
                  <th>Capacidad</th>
                  <th>Espacio libre</th>
                  <th>Consumo hoy</th>
                  <th>Consumo del mes</th>
                  <th>Estado</th>
                </tr>
              </thead>
              <tbody>
                {stock.tanks.map((t) => (
                  <tr key={t.id}>
                    <td>{t.code}</td>
                    <td>{t.station}</td>
                    <td>{t.fuelType}</td>
                    <td className="level-cell">
                      <LevelBar
                        label={`Nivel del tanque ${t.code}`}
                        balance={t.balance}
                        capacity={t.capacity}
                        critical={t.critical}
                        criticalLevel={t.criticalLevel}
                      />
                    </td>
                    <td>{formatGallons(t.balance)}</td>
                    <td>{formatGallons(t.capacity)}</td>
                    <td>{formatGallons(t.freeSpace)}</td>
                    <td>{formatGallons(t.dailyConsumption)}</td>
                    <td>{formatGallons(t.monthlyConsumption)}</td>
                    <td>
                      {!t.active ? (
                        <Badge tone="inactive">Inactivo</Badge>
                      ) : t.critical ? (
                        <Badge tone="danger">Nivel crítico</Badge>
                      ) : (
                        <Badge>Normal</Badge>
                      )}
                    </td>
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

// RF-16: recepción con RNC, suplidor, factura, volumen, fecha y tanque.
function Receipts({
  tanks,
  canWrite,
  onMoved,
}: {
  tanks: StockTank[];
  canWrite: boolean;
  onMoved: () => void;
}) {
  const [page, setPage] = useState(1);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const load = useCallback(
    () =>
      api<{ items: Receipt[]; total: number }>(
        `/api/inventario/recepciones?page=${page}`,
      ),
    [page],
  );
  const list = useLoad(load);
  const [today] = useState(() => localDay());
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const form = event.currentTarget;
    const data = new FormData(form);
    setBusy(true);
    setError("");
    setNotice("");
    try {
      const result = await api<{ id: string; balance: number }>(
        "/api/inventario/recepciones",
        {
          method: "POST",
          body: JSON.stringify({
            kind: textValue(data, "kind"),
            supplierRnc: textValue(data, "supplierRnc"),
            supplierName: textValue(data, "supplierName"),
            invoice: textValue(data, "invoice"),
            quantity: numberValue(data, "quantity"),
            receivedOn: textValue(data, "receivedOn"),
            tankId: textValue(data, "tankId"),
          }),
        },
      );
      form.reset();
      setNotice(
        `Recepción registrada. Nuevo saldo del tanque: ${formatGallons(result.balance)}.`,
      );
      list.reload();
      onMoved();
    } catch (e) {
      setError(explain(e));
    } finally {
      setBusy(false);
    }
  }
  return (
    <>
      {canWrite && (
        <section className="editor">
          <div className="section-heading">
            <h2>Registrar recepción o compra</h2>
          </div>
          <form onSubmit={submit}>
            <Messages error={error} notice={notice} />
            <div className="form-grid">
              <label>
                Tipo
                <select name="kind" required defaultValue="Receipt">
                  <option value="Receipt">Recepción</option>
                  <option value="Purchase">Compra</option>
                </select>
              </label>
              <label>
                RNC del suplidor
                <input
                  name="supplierRnc"
                  required
                  inputMode="numeric"
                  pattern="[0-9]{9}|[0-9]{11}"
                  title="9 u 11 dígitos, sin guiones"
                  maxLength={11}
                />
              </label>
              <label>
                Nombre del suplidor
                <input name="supplierName" required maxLength={200} />
              </label>
              <label>
                Factura
                <input name="invoice" required maxLength={50} />
              </label>
              <label>
                Cantidad (galones)
                <input
                  name="quantity"
                  type="number"
                  inputMode="decimal"
                  required
                  min={0.001}
                  step={0.001}
                />
              </label>
              <label>
                Fecha de recepción
                <input
                  name="receivedOn"
                  type="date"
                  required
                  max={today}
                  defaultValue={today}
                />
              </label>
              <label>
                Tanque
                <select name="tankId" required defaultValue="">
                  <option value="">Selecciona el tanque</option>
                  {tanks
                    .filter((t) => t.active)
                    .map((t) => (
                      <option key={t.id} value={t.id}>
                        {tankLabel(t)}
                      </option>
                    ))}
                </select>
              </label>
            </div>
            <div className="form-actions">
              <span />
              <button className="primary" disabled={busy}>
                {busy ? "Registrando…" : "Registrar recepción"}
              </button>
            </div>
          </form>
        </section>
      )}
      <section className="data-panel">
        <div className="section-heading">
          <h2>Recepciones registradas</h2>
        </div>
        <Messages error={list.error} />
        {!list.data ? (
          list.loading && <Loading />
        ) : list.data.items.length === 0 ? (
          <Empty title="Sin recepciones" text="Todavía no se registran entradas de combustible." />
        ) : (
          <div className="table-scroll">
            <table>
              <thead>
                <tr>
                  <th>Fecha</th>
                  <th>Tipo</th>
                  <th>Suplidor</th>
                  <th>RNC</th>
                  <th>Factura</th>
                  <th>Tanque</th>
                  <th>Cantidad</th>
                  <th>Registrada</th>
                </tr>
              </thead>
              <tbody>
                {list.data.items.map((r) => (
                  <tr key={r.id}>
                    <td>{r.receivedOn}</td>
                    <td>{movementKindLabels[r.kind] ?? r.kind}</td>
                    <td>{r.supplierName}</td>
                    <td className="mono">{r.supplierRnc}</td>
                    <td>{r.invoice}</td>
                    <td>{r.tank}</td>
                    <td>{formatGallons(r.quantity)}</td>
                    <td>{formatDateTime(r.recordedAt)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
        {list.data && (
          <Pager page={page} total={list.data.total} loading={list.loading} onPage={setPage} unit="recepciones" />
        )}
      </section>
    </>
  );
}

type PendingMove = { summary: string; path: string; body: object; done: (r: Record<string, number>) => string };

// RF-14: transferencias entre tanques del mismo combustible, ajustes y mermas.
function Movements({
  mode,
  tanks,
  onMoved,
}: {
  mode: "transfer" | "adjust";
  tanks: StockTank[];
  onMoved: () => void;
}) {
  const [fromId, setFromId] = useState("");
  const [pending, setPending] = useState<PendingMove | null>(null);
  const [notice, setNotice] = useState("");
  const [formKey, setFormKey] = useState(0);
  const active = tanks.filter((t) => t.active);
  const source = active.find((t) => t.id === fromId);
  const byId = (id: string) => active.find((t) => t.id === id);
  function review(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const data = new FormData(event.currentTarget);
    const quantity = numberValue(data, "quantity") ?? 0;
    const reason = textValue(data, "reason");
    setNotice("");
    if (mode === "transfer") {
      const target = byId(textValue(data, "toTankId"));
      setPending({
        summary: `Transferir ${formatGallons(quantity)} de ${source?.code} a ${target?.code}. Motivo: ${reason}`,
        path: "/api/inventario/transferencias",
        body: { fromTankId: fromId, toTankId: target?.id, quantity, reason },
        done: (r) =>
          `Transferencia registrada. Saldo de ${source?.code}: ${formatGallons(r.fromBalance)}; saldo de ${target?.code}: ${formatGallons(r.toBalance)}.`,
      });
    } else {
      const tank = byId(textValue(data, "tankId"));
      const kind = textValue(data, "kind");
      setPending({
        summary: `${movementKindLabels[kind]} de ${formatGallons(quantity)} en el tanque ${tank?.code}. Motivo: ${reason}`,
        path: "/api/inventario/ajustes",
        body: { tankId: tank?.id, kind, quantity, reason },
        done: (r) =>
          `${movementKindLabels[kind]} registrado. Nuevo saldo de ${tank?.code}: ${formatGallons(r.balance)}.`,
      });
    }
  }
  return (
    <section className="editor">
      <div className="section-heading">
        <h2>
          {mode === "transfer"
            ? "Transferencia entre tanques"
            : "Ajuste o merma de inventario"}
        </h2>
      </div>
      <form onSubmit={review} key={formKey}>
        <Messages notice={notice} />
        <div className="form-grid">
          {mode === "transfer" ? (
            <>
              <label>
                Tanque de origen
                <select
                  name="fromTankId"
                  required
                  value={fromId}
                  onChange={(e) => setFromId(e.target.value)}
                >
                  <option value="">Selecciona el origen</option>
                  {active.map((t) => (
                    <option key={t.id} value={t.id}>
                      {tankLabel(t)}
                    </option>
                  ))}
                </select>
              </label>
              <label>
                Tanque de destino
                <select name="toTankId" required defaultValue="">
                  <option value="">
                    {source ? "Selecciona el destino" : "Elige primero el origen"}
                  </option>
                  {active
                    .filter(
                      (t) =>
                        source &&
                        t.id !== source.id &&
                        t.fuelTypeId === source.fuelTypeId,
                    )
                    .map((t) => (
                      <option key={t.id} value={t.id}>
                        {tankLabel(t)}
                      </option>
                    ))}
                </select>
              </label>
            </>
          ) : (
            <>
              <label>
                Tanque
                <select name="tankId" required defaultValue="">
                  <option value="">Selecciona el tanque</option>
                  {active.map((t) => (
                    <option key={t.id} value={t.id}>
                      {tankLabel(t)}
                    </option>
                  ))}
                </select>
              </label>
              <label>
                Tipo de movimiento
                <select name="kind" required defaultValue="">
                  <option value="">Selecciona el tipo</option>
                  <option value="PositiveAdjustment">Ajuste positivo</option>
                  <option value="NegativeAdjustment">Ajuste negativo</option>
                  <option value="Shrinkage">Merma</option>
                </select>
              </label>
            </>
          )}
          <label>
            Cantidad (galones)
            <input
              name="quantity"
              type="number"
              inputMode="decimal"
              required
              min={0.001}
              step={0.001}
            />
          </label>
        </div>
        <label className="wide">
          Motivo (mínimo 5 caracteres)
          <textarea name="reason" required minLength={5} maxLength={500} rows={2} />
        </label>
        <div className="form-actions">
          <small>Los movimientos son inmutables y quedan en la auditoría.</small>
          <button className="primary">Revisar y registrar</button>
        </div>
      </form>
      {pending && (
        <ConfirmDialog
          title={mode === "transfer" ? "Confirmar transferencia" : "Confirmar ajuste"}
          confirmLabel="Registrar movimiento"
          danger={mode === "adjust"}
          description={<p>{pending.summary}</p>}
          onConfirm={async () => {
            const result = await api<Record<string, number>>(pending.path, {
              method: "POST",
              body: JSON.stringify(pending.body),
            });
            setNotice(pending.done(result));
            setFromId("");
            setFormKey((x) => x + 1);
            onMoved();
          }}
          onClose={() => setPending(null)}
        />
      )}
    </section>
  );
}

function MovementLog({ tanks }: { tanks: StockTank[] }) {
  const [filters, setFilters] = useState({ tankId: "", kind: "", from: "", to: "" });
  const [page, setPage] = useState(1);
  const load = useCallback(() => {
    const params = new URLSearchParams({ page: String(page) });
    for (const [key, value] of Object.entries(filters))
      if (value) params.set(key, value);
    return api<{ items: Movement[]; total: number }>(
      `/api/inventario/movimientos?${params}`,
    );
  }, [filters, page]);
  const { data, error, loading } = useLoad(load);
  function apply(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const form = new FormData(event.currentTarget);
    setFilters({
      tankId: textValue(form, "tankId"),
      kind: textValue(form, "kind"),
      from: textValue(form, "from"),
      to: textValue(form, "to"),
    });
    setPage(1);
  }
  return (
    <>
      <form className="filters" onSubmit={apply}>
        <label>
          Tanque
          <select name="tankId" defaultValue="">
            <option value="">Todos</option>
            {tanks.map((t) => (
              <option key={t.id} value={t.id}>
                {t.code} · {t.station}
              </option>
            ))}
          </select>
        </label>
        <label>
          Tipo
          <select name="kind" defaultValue="">
            <option value="">Todos</option>
            {Object.entries(movementKindLabels).map(([key, label]) => (
              <option key={key} value={key}>
                {label}
              </option>
            ))}
          </select>
        </label>
        <label>
          Desde
          <input name="from" type="date" />
        </label>
        <label>
          Hasta
          <input name="to" type="date" />
        </label>
        <button type="submit">Filtrar</button>
      </form>
      <Messages error={error} />
      <section className="data-panel">
        {!data ? (
          loading && <Loading />
        ) : data.items.length === 0 ? (
          <Empty title="Sin movimientos" text="No hay movimientos con estos filtros." />
        ) : (
          <div className="table-scroll">
            <table>
              <thead>
                <tr>
                  <th>Fecha y hora</th>
                  <th>Estación</th>
                  <th>Tanque</th>
                  <th>Combustible</th>
                  <th>Tipo</th>
                  <th>Cantidad</th>
                  <th>Saldo resultante</th>
                  <th>Usuario</th>
                  <th>Motivo</th>
                </tr>
              </thead>
              <tbody>
                {data.items.map((m) => (
                  <tr key={m.id}>
                    <td>{formatDateTime(m.occurredAt)}</td>
                    <td>{m.station}</td>
                    <td>{m.tank}</td>
                    <td>{m.fuelType}</td>
                    <td>{movementKindLabels[m.kind] ?? m.kind}</td>
                    <td className={m.quantity < 0 ? "negative" : "positive"}>
                      {m.quantity > 0 ? "+" : ""}
                      {formatGallons(m.quantity)}
                    </td>
                    <td>{formatGallons(m.balanceAfter)}</td>
                    <td>{m.actor}</td>
                    <td className="wrap">{m.reason}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
        {data && (
          <Pager page={page} total={data.total} loading={loading} onPage={setPage} unit="movimientos" />
        )}
      </section>
    </>
  );
}

// RF-14..RF-17: existencias, entradas, transferencias, ajustes y catálogos de inventario.
export function Inventory({ canWrite }: { canWrite: boolean }) {
  const [tab, setTab] = useState<Tab>("existencias");
  const load = useCallback(() => api<Stock>("/api/inventario/"), []);
  const stock = useLoad(load);
  const tabs: [Tab, string][] = [
    ["existencias", "Existencias"],
    ["recepciones", "Recepciones"],
    ...(canWrite
      ? ([
          ["transferencias", "Transferencias"],
          ["ajustes", "Ajustes y mermas"],
        ] as [Tab, string][])
      : []),
    ["movimientos", "Movimientos"],
    ["combustibles", "Combustibles"],
    ["estaciones", "Estaciones"],
    ["tanques", "Tanques"],
  ];
  const tanks = stock.data?.tanks ?? [];
  return (
    <>
      <PageHeading
        eyebrow="INVENTARIO"
        title="Inventario"
        description="Existencias por tanque, entradas, transferencias, ajustes y su historial."
      >
        <button className="subtle" onClick={stock.reload} disabled={stock.loading}>
          Actualizar
        </button>
      </PageHeading>
      <Tabs
        label="Secciones de inventario"
        tabs={tabs}
        value={tab}
        onChange={(next) => {
          setTab(next);
          stock.reload();
        }}
      />
      <Messages error={stock.error} />
      {tab === "combustibles" || tab === "estaciones" || tab === "tanques" ? (
        <Catalog key={tab} section={tab} canWrite={canWrite} embedded />
      ) : !stock.data ? (
        stock.loading && <Loading />
      ) : tab === "existencias" ? (
        <Existences stock={stock.data} />
      ) : tab === "recepciones" ? (
        <Receipts tanks={tanks} canWrite={canWrite} onMoved={stock.reload} />
      ) : tab === "transferencias" ? (
        <Movements key="transfer" mode="transfer" tanks={tanks} onMoved={stock.reload} />
      ) : tab === "ajustes" ? (
        <Movements key="adjust" mode="adjust" tanks={tanks} onMoved={stock.reload} />
      ) : (
        <MovementLog tanks={tanks} />
      )}
    </>
  );
}
