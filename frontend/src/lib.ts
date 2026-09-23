import { useCallback, useEffect, useState, useSyncExternalStore } from "react";
import { api } from "./api";

// Utilidades compartidas por las pantallas. Todas las fechas se muestran en la zona
// horaria de la institución; los filtros por día usan días locales AAAA-MM-DD.
export const TIME_ZONE = "America/Santo_Domingo";

export const explain = (error: unknown) =>
  error instanceof TypeError
    ? "No se pudo conectar con el servidor. Revisa la conexión e inténtalo de nuevo."
    : error instanceof Error
      ? error.message
      : "No se pudo completar la operación.";

const dateTime = new Intl.DateTimeFormat("es-DO", {
  timeZone: TIME_ZONE,
  year: "numeric",
  month: "2-digit",
  day: "2-digit",
  hour: "2-digit",
  minute: "2-digit",
  hour12: false,
});
const isoDay = new Intl.DateTimeFormat("en-CA", {
  timeZone: TIME_ZONE,
  year: "numeric",
  month: "2-digit",
  day: "2-digit",
});
// Sin ceros de relleno: "10.000 gal" se lee como diez mil (mismo criterio que el backend).
const gallons = new Intl.NumberFormat("es-DO", { maximumFractionDigits: 3 });
const integer = new Intl.NumberFormat("es-DO");

// Fecha y hora absolutas en la zona de la institución.
export function formatDateTime(value: string | null | undefined) {
  if (!value) return "—";
  const parts = Object.fromEntries(
    dateTime.formatToParts(new Date(value)).map((p) => [p.type, p.value]),
  );
  return `${parts.year}-${parts.month}-${parts.day} ${parts.hour}:${parts.minute}`;
}
export const formatGallons = (value: number | null | undefined) =>
  value === null || value === undefined ? "—" : `${gallons.format(value)} gal`;
export const formatNumber = (value: number) => integer.format(value);

// Día local (AAAA-MM-DD) del instante dado en la zona de la institución.
export const localDay = (date = new Date()) => isoDay.format(date);

// Desplazamiento UTC de la zona para un instante, p. ej. "-04:00".
function offsetAt(instant: Date) {
  const name = new Intl.DateTimeFormat("en-US", {
    timeZone: TIME_ZONE,
    timeZoneName: "longOffset",
  })
    .formatToParts(instant)
    .find((p) => p.type === "timeZoneName")?.value;
  const match = /GMT([+-]\d{2}:\d{2})?/.exec(name ?? "");
  if (!match) throw new Error(`Zona horaria no reconocida: ${name}`);
  return match[1] ?? "+00:00";
}
// Valor de <input type="datetime-local"> interpretado en la zona de la institución → ISO UTC.
export function localInputToIso(value: string) {
  const offset = offsetAt(new Date(`${value}:00Z`));
  return new Date(`${value}:00${offset}`).toISOString();
}
// ISO UTC → valor para <input type="datetime-local"> en la zona de la institución.
export function isoToLocalInput(value: string | Date) {
  return formatDateTime(
    typeof value === "string" ? value : value.toISOString(),
  ).replace(" ", "T");
}

export const ticketStatusLabels: Record<string, string> = {
  Created: "Creado",
  Sent: "Enviado",
  Pending: "Pendiente de entrega",
  NearExpiry: "Próximo a vencer",
  Expired: "Vencido",
  Consumed: "Consumido",
  Voided: "Anulado",
};
export const activeTicketStatuses = ["Created", "Sent", "Pending", "NearExpiry"];
export const requestStatusLabels: Record<string, string> = {
  Pending: "Pendiente",
  Approved: "Aprobada",
  Rejected: "Rechazada",
  Cancelled: "Cancelada",
};
export const originLabels: Record<string, string> = {
  Manual: "Manual",
  Scheduled: "Programada",
  Recurring: "Recurrente",
};
export const movementKindLabels: Record<string, string> = {
  Receipt: "Recepción",
  Purchase: "Compra",
  TransferIn: "Transferencia entrada",
  TransferOut: "Transferencia salida",
  Dispatch: "Despacho",
  Shrinkage: "Merma",
  PositiveAdjustment: "Ajuste positivo",
  NegativeAdjustment: "Ajuste negativo",
};
export const notificationKindLabels: Record<string, string> = {
  TicketNearExpiry: "Ticket próximo a vencer",
  TicketExpired: "Ticket vencido",
  LowInventory: "Inventario bajo",
  IntegrationFailure: "Falla de integración",
  InventoryAdjustment: "Ajuste de inventario",
  ScheduleFailure: "Falla de programación",
};
export const frequencyLabels: Record<string, string> = {
  Once: "Una vez",
  Daily: "Diaria",
  Weekly: "Semanal",
  Monthly: "Mensual",
};
export const ruleLabels: Record<string, string> = {
  Fixed: "Cantidad fija",
  History: "Promedio de despachos anteriores",
};
export const channelLabels: Record<string, string> = {
  Email: "Correo",
  Sms: "SMS",
};
// ADR-005: Outbox = escrito en la bandeja local, NUNCA equivale a enviado.
export const deliveryResultLabels: Record<string, string> = {
  Sent: "Enviado",
  Outbox: "En bandeja local (no enviado)",
  Failed: "Falló",
};

export type Role =
  | "Administrador"
  | "Supervisor"
  | "Despachador"
  | "Auditor"
  | "Consulta";
export const hasRole = (roles: string[], ...allowed: Role[]) =>
  roles.some((role) => (allowed as string[]).includes(role));

export type Named = {
  id: string;
  active: boolean;
  version: string;
  code?: string;
  name?: string;
};
export type Employee = Named & {
  code: string;
  fullName: string;
  departmentId: string;
};
export type Vehicle = Named & {
  plate: string;
  internalCode: string;
  departmentId: string;
  tankCapacity: number;
};
export type CodeName = Named & { code: string; name: string };
export type Tank = CodeName & {
  stationId: string;
  fuelTypeId: string;
  capacity: number;
  criticalLevel: number;
  balance: number;
};

// Lee todas las páginas de un catálogo (100 por página).
export async function loadAll<T>(route: string): Promise<T[]> {
  const all: T[] = [];
  for (let page = 1; ; page++) {
    const data = await api<{ items: T[]; total: number }>(
      `/api/${route}/?page=${page}&pageSize=100`,
    );
    all.push(...data.items);
    if (data.items.length === 0 || all.length >= data.total) return all;
  }
}

// Carga asíncrona con estado de carga/error y recarga explícita. `load` debe ser
// estable (useCallback): cada identidad nueva dispara una carga.
type Loaded<T> = {
  load: (() => Promise<T>) | null;
  revision: number;
  data: T | null;
  error: string;
};
export function useLoad<T>(load: () => Promise<T>) {
  const [revision, setRevision] = useState(0);
  const [state, setState] = useState<Loaded<T>>({
    load: null,
    revision: 0,
    data: null,
    error: "",
  });
  useEffect(() => {
    let alive = true;
    load().then(
      (data) => {
        if (alive) setState({ load, revision, data, error: "" });
      },
      (e: unknown) => {
        if (alive)
          setState((s) => ({ ...s, load, revision, error: explain(e) }));
      },
    );
    return () => {
      alive = false;
    };
  }, [load, revision]);
  const reload = useCallback(() => setRevision((x) => x + 1), []);
  return {
    data: state.data,
    error: state.error,
    loading: state.load !== load || state.revision !== revision,
    reload,
  };
}

function subscribeOnline(listener: () => void) {
  window.addEventListener("online", listener);
  window.addEventListener("offline", listener);
  return () => {
    window.removeEventListener("online", listener);
    window.removeEventListener("offline", listener);
  };
}
export const useOnline = () =>
  useSyncExternalStore(subscribeOnline, () => navigator.onLine);

// Descarga un blob con su nombre de archivo.
export function saveBlob(blob: Blob, filename: string) {
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = filename;
  document.body.append(link);
  link.click();
  link.remove();
  setTimeout(() => URL.revokeObjectURL(url), 10000);
}

export const numberValue = (form: FormData, key: string) => {
  const raw = String(form.get(key) ?? "").trim();
  return raw === "" ? null : Number(raw);
};
export const textValue = (form: FormData, key: string) =>
  String(form.get(key) ?? "").trim();

export type Catalogs = {
  employees: Employee[];
  vehicles: Vehicle[];
  departments: CodeName[];
  fuels: CodeName[];
};
// Catálogos para formularios de asignación (solicitudes, programaciones, reportes).
export function useCatalogs() {
  const load = useCallback(async (): Promise<Catalogs> => {
    const [employees, vehicles, departments, fuels] = await Promise.all([
      loadAll<Employee>("empleados"),
      loadAll<Vehicle>("vehiculos"),
      loadAll<CodeName>("departamentos"),
      loadAll<CodeName>("combustibles"),
    ]);
    return { employees, vehicles, departments, fuels };
  }, []);
  return useLoad(load);
}

export type Assignment = {
  employeeId: string;
  vehicleId: string;
  departmentId: string;
  fuelTypeId: string;
};
export const emptyAssignment: Assignment = {
  employeeId: "",
  vehicleId: "",
  departmentId: "",
  fuelTypeId: "",
};
