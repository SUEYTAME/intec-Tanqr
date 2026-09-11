import { useCallback, useState } from "react";
import type { FormEvent } from "react";
import { api, apiBlob } from "./api";
import {
  ConfirmDialog,
  Empty,
  Loading,
  Messages,
  PageHeading,
  Pager,
} from "./components";
import {
  explain,
  formatDateTime,
  formatGallons,
  loadAll,
  localDay,
  saveBlob,
  textValue,
  useLoad,
} from "./lib";
import type { CodeName } from "./lib";

type Line = {
  tankId: string;
  tank: string;
  fuelType: string;
  opening: number;
  inputs: number;
  outputs: number;
  expected: number;
};
type Preview = {
  dispatchCount: number;
  dispatchedVolume: number;
  lines: Line[];
};
type Close = {
  id: string;
  stationId: string;
  station: string;
  day: string;
  dispatchCount: number;
  dispatchedVolume: number;
  closedAt: string;
  notes: string;
};

async function downloadActa(id: string, fallback: string) {
  const { blob, filename } = await apiBlob(`/api/cierres/${id}/pdf`);
  saveBlob(blob, filename ?? fallback);
}

// RF-18: cierre diario por estación con conteo físico y acta PDF.
export function DailyClose() {
  const loadStations = useCallback(() => loadAll<CodeName>("estaciones"), []);
  const stations = useLoad(loadStations);
  const [today] = useState(() => localDay());
  const [target, setTarget] = useState<{ stationId: string; day: string } | null>(null);
  const [preview, setPreview] = useState<Preview | null>(null);
  const [counts, setCounts] = useState<Record<string, string>>({});
  const [notes, setNotes] = useState("");
  const [confirming, setConfirming] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [closed, setClosed] = useState<{ id: string; name: string } | null>(null);
  const [page, setPage] = useState(1);
  const loadCloses = useCallback(
    () => api<{ items: Close[]; total: number }>(`/api/cierres/?page=${page}`),
    [page],
  );
  const history = useLoad(loadCloses);

  async function loadPreview(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const form = new FormData(event.currentTarget);
    const next = { stationId: textValue(form, "stationId"), day: textValue(form, "day") };
    setBusy(true);
    setError("");
    setClosed(null);
    setPreview(null);
    try {
      const data = await api<Preview>(
        `/api/cierres/previo?stationId=${next.stationId}&day=${next.day}`,
      );
      setTarget(next);
      setPreview(data);
      setCounts({});
      setNotes("");
    } catch (e) {
      setError(explain(e));
    } finally {
      setBusy(false);
    }
  }
  const station = stations.data?.find((s) => s.id === target?.stationId);
  const difference = (line: Line) => {
    const raw = counts[line.tankId];
    return raw === undefined || raw === "" ? null : Number(raw) - line.expected;
  };
  return (
    <>
      <PageHeading
        eyebrow="DESPACHO / CIERRE DIARIO"
        title="Cierre diario"
        description="Compara lo esperado por tanque con la medición física y genera el acta del día."
      />
      <Messages error={error || stations.error} />
      <section className="editor">
        <div className="section-heading">
          <h2>1. Estación y día</h2>
        </div>
        <form className="filters inline" onSubmit={loadPreview}>
          <label>
            Estación
            <select name="stationId" required defaultValue="">
              <option value="">Selecciona la estación</option>
              {(stations.data ?? [])
                .filter((s) => s.active)
                .map((s) => (
                  <option key={s.id} value={s.id}>
                    {s.name} ({s.code})
                  </option>
                ))}
            </select>
          </label>
          <label>
            Día operativo
            <input name="day" type="date" required max={today} defaultValue={today} />
          </label>
          <button type="submit" disabled={busy}>
            Ver previo
          </button>
        </form>
      </section>
      {closed && (
        <section className="result-card success" aria-live="polite">
          <p className="eyebrow">DÍA CERRADO</p>
          <h2>Cierre registrado</h2>
          <p>
            Esa estación ya no admite movimientos de inventario para ese día.
          </p>
          <button
            className="primary"
            onClick={() =>
              void downloadActa(closed.id, closed.name).catch((e) =>
                setError(explain(e)),
              )
            }
          >
            Descargar acta PDF
          </button>
        </section>
      )}
      {preview && target && station && (
        <section className="editor">
          <div className="section-heading">
            <h2>
              2. Medición física · {station.name} · {target.day}
            </h2>
          </div>
          <div className="detail-body">
            <p className="hint">
              {preview.dispatchCount} despachos confirmados ·{" "}
              {formatGallons(preview.dispatchedVolume)} despachados.
            </p>
            {preview.lines.length === 0 ? (
              <Empty
                title="La estación no tiene tanques"
                text="No hay nada que medir para este día."
              />
            ) : (
              <div className="table-scroll">
                <table>
                  <thead>
                    <tr>
                      <th>Tanque</th>
                      <th>Combustible</th>
                      <th>Apertura</th>
                      <th>Entradas</th>
                      <th>Salidas</th>
                      <th>Esperado</th>
                      <th>Medido (galones)</th>
                      <th>Diferencia</th>
                    </tr>
                  </thead>
                  <tbody>
                    {preview.lines.map((line) => {
                      const diff = difference(line);
                      return (
                        <tr key={line.tankId}>
                          <td>{line.tank}</td>
                          <td>{line.fuelType}</td>
                          <td>{formatGallons(line.opening)}</td>
                          <td>{formatGallons(line.inputs)}</td>
                          <td>{formatGallons(line.outputs)}</td>
                          <td>{formatGallons(line.expected)}</td>
                          <td>
                            <input
                              className="count-input"
                              type="number"
                              inputMode="decimal"
                              min={0}
                              step={0.001}
                              required
                              aria-label={`Medido en el tanque ${line.tank}`}
                              value={counts[line.tankId] ?? ""}
                              onChange={(e) =>
                                setCounts((c) => ({ ...c, [line.tankId]: e.target.value }))
                              }
                            />
                          </td>
                          <td className={diff === null || diff === 0 ? "" : "negative"}>
                            {diff === null ? "—" : formatGallons(diff)}
                          </td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              </div>
            )}
            <label className="wide">
              Observaciones (opcional)
              <textarea
                maxLength={1000}
                rows={2}
                value={notes}
                onChange={(e) => setNotes(e.target.value)}
              />
            </label>
            <div className="form-actions">
              <small>
                Las diferencias generan una alerta; regístralas como ajuste si
                corresponde.
              </small>
              <button
                className="primary"
                disabled={
                  busy ||
                  preview.lines.some((l) => (counts[l.tankId] ?? "") === "")
                }
                onClick={() => setConfirming(true)}
              >
                Cerrar el día
              </button>
            </div>
          </div>
        </section>
      )}
      {confirming && preview && target && station && (
        <ConfirmDialog
          title={`Cerrar ${station.name} · ${target.day}`}
          confirmLabel="Cerrar el día"
          danger
          description={
            <p>
              Después del cierre esta estación no podrá registrar despachos ni
              movimientos con fecha {target.day}. No se puede deshacer.
            </p>
          }
          onConfirm={async () => {
            const result = await api<{ id: string }>("/api/cierres/", {
              method: "POST",
              body: JSON.stringify({
                stationId: target.stationId,
                day: target.day,
                counts: preview.lines.map((l) => ({
                  tankId: l.tankId,
                  counted: Number(counts[l.tankId]),
                })),
                notes: notes.trim() || null,
              }),
            });
            setClosed({
              id: result.id,
              name: `cierre-${station.code}-${target.day}.pdf`,
            });
            setPreview(null);
            history.reload();
          }}
          onClose={() => setConfirming(false)}
        />
      )}
      <section className="data-panel">
        <div className="section-heading">
          <h2>Cierres registrados</h2>
        </div>
        <Messages error={history.error} />
        {!history.data ? (
          history.loading && <Loading />
        ) : history.data.items.length === 0 ? (
          <Empty title="Sin cierres" text="Aquí aparecerán las actas de cierre." />
        ) : (
          <div className="table-scroll">
            <table>
              <thead>
                <tr>
                  <th>Día</th>
                  <th>Estación</th>
                  <th>Despachos</th>
                  <th>Volumen</th>
                  <th>Cerrado</th>
                  <th>Acta</th>
                </tr>
              </thead>
              <tbody>
                {history.data.items.map((c) => (
                  <tr key={c.id}>
                    <td>{c.day}</td>
                    <td>{c.station}</td>
                    <td>{c.dispatchCount}</td>
                    <td>{formatGallons(c.dispatchedVolume)}</td>
                    <td>{formatDateTime(c.closedAt)}</td>
                    <td>
                      <button
                        className="text-button"
                        aria-label={`Descargar acta de ${c.station} del ${c.day}`}
                        onClick={() =>
                          void downloadActa(c.id, `cierre-${c.day}.pdf`).catch(
                            (e) => setError(explain(e)),
                          )
                        }
                      >
                        Descargar PDF
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
        {history.data && (
          <Pager
            page={page}
            total={history.data.total}
            loading={history.loading}
            onPage={setPage}
            unit="cierres"
          />
        )}
      </section>
    </>
  );
}
