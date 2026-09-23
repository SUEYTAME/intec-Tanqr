import { useEffect, useState } from "react";
import type { FormEvent } from "react";
import { api } from "./api";
import { explain, formatGallons, loadAll } from "./lib";
import type { Named } from "./lib";

type Row = Record<string, string | number | boolean> & {
  id: string;
  version: string;
  active: boolean;
};
type Field = {
  key: string;
  label: string;
  type?: "text" | "email" | "tel" | "number" | "ref";
  max?: number;
  min?: number;
  maxValue?: number;
  step?: number;
  // Catálogo referenciado (select) cuando type === "ref".
  ref?: { route: string; placeholder: string };
};
type Schema = {
  title: string;
  description: string;
  fields: Field[];
  // Columna adicional de solo lectura en la tabla.
  extra?: { label: string; value: (row: Row) => string };
};
const department = {
  route: "departamentos",
  placeholder: "Selecciona un departamento",
};
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
      { key: "departmentId", label: "Departamento", type: "ref", ref: department },
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
      { key: "year", label: "Año", type: "number", min: 1900, maxValue: 2200, step: 1 },
      { key: "kind", label: "Tipo", max: 80 },
      { key: "departmentId", label: "Departamento", type: "ref", ref: department },
      { key: "tankCapacity", label: "Capacidad (galones)", type: "number", min: 0.001, step: 0.001 },
      { key: "odometer", label: "Odómetro (km)", type: "number", min: 0, step: 1 },
    ],
  },
  combustibles: {
    title: "Combustibles",
    description: "Tipos de combustible que se almacenan y despachan.",
    fields: [
      { key: "code", label: "Código", max: 30 },
      { key: "name", label: "Nombre", max: 150 },
    ],
  },
  estaciones: {
    title: "Estaciones",
    description: "Puntos de despacho donde están instalados los tanques.",
    fields: [
      { key: "code", label: "Código", max: 30 },
      { key: "name", label: "Nombre", max: 150 },
    ],
  },
  tanques: {
    title: "Tanques",
    description:
      "Capacidad y nivel crítico de cada tanque. La existencia solo cambia con movimientos de inventario.",
    fields: [
      { key: "code", label: "Código", max: 30 },
      {
        key: "stationId",
        label: "Estación",
        type: "ref",
        ref: { route: "estaciones", placeholder: "Selecciona una estación" },
      },
      {
        key: "fuelTypeId",
        label: "Combustible",
        type: "ref",
        ref: { route: "combustibles", placeholder: "Selecciona un combustible" },
      },
      { key: "capacity", label: "Capacidad (galones)", type: "number", min: 0.001, step: 0.001 },
      { key: "criticalLevel", label: "Nivel crítico (galones)", type: "number", min: 0, step: 0.001 },
    ],
    extra: {
      label: "Existencia",
      value: (row: Row) => formatGallons(Number(row.balance)),
    },
  },
} satisfies Record<string, Schema>;
export type Section = keyof typeof sections;

const refName = (item: Named & Record<string, unknown>) =>
  String(item.name ?? item.code ?? item.id);

export function Catalog({
  section,
  canWrite,
  embedded,
}: {
  section: Section;
  canWrite: boolean;
  embedded?: boolean;
}) {
  const schema: Schema = sections[section];
  const [rows, setRows] = useState<Row[]>([]);
  const [refs, setRefs] = useState<Record<string, (Named & Row)[]>>({});
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
        if (alive) setError(explain(e));
      })
      .finally(() => {
        if (alive) setLoading(false);
      });
    return () => {
      alive = false;
    };
  }, [section, page, revision]);
  useEffect(() => {
    const routes = [
      ...new Set(
        sections[section].fields.flatMap((f: Field) =>
          f.ref ? [f.ref.route] : [],
        ),
      ),
    ];
    if (routes.length === 0) return;
    let alive = true;
    Promise.all(routes.map((route) => loadAll<Named & Row>(route)))
      .then((lists) => {
        if (alive)
          setRefs(Object.fromEntries(routes.map((r, i) => [r, lists[i]!])));
      })
      .catch((e) => {
        if (alive) setError(explain(e));
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
    for (const field of schema.fields)
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
      setError(explain(e));
    } finally {
      setSaving(false);
    }
  }
  const display = (field: Field, row: Row) => {
    // La API omite cédula/correo/móvil a quien no es Administrador ni Supervisor (ADR-016).
    if (!field.ref) return field.key in row ? String(row[field.key]) : "Restringido";
    const item = refs[field.ref.route]?.find((x) => x.id === row[field.key]);
    return item ? refName(item) : "…";
  };
  const Heading = embedded ? "h2" : "h1";
  return (
    <>
      <div className={`page-heading ${embedded ? "embedded" : ""}`}>
        <div>
          {!embedded && (
            <p className="eyebrow">CATÁLOGOS / {schema.title.toUpperCase()}</p>
          )}
          <Heading>{schema.title}</Heading>
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
              {schema.fields.map((field) => (
                <label key={field.key}>
                  {field.label}
                  {field.ref ? (
                    <select
                      name={field.key}
                      required
                      defaultValue={String(editing?.[field.key] ?? "")}
                    >
                      <option value="">{field.ref.placeholder}</option>
                      {(refs[field.ref.route] ?? [])
                        .filter(
                          (d) => d.active || d.id === editing?.[field.key],
                        )
                        .map((d) => (
                          <option key={d.id} value={d.id}>
                            {refName(d)}
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
                      min={field.min}
                      max={field.maxValue}
                      step={field.step}
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
                  {schema.extra && <th>{schema.extra.label}</th>}
                  <th>Estado</th>
                  {canWrite && <th>Acciones</th>}
                </tr>
              </thead>
              <tbody>
                {rows.map((row) => (
                  <tr key={row.id}>
                    {schema.fields.slice(0, 3).map((f) => (
                      <td key={f.key}>{display(f, row)}</td>
                    ))}
                    {schema.extra && <td>{schema.extra.value(row)}</td>}
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
