import { useEffect, useRef, useState } from "react";
import type { FormEvent, ReactNode } from "react";
import {
  channelLabels,
  deliveryResultLabels,
  explain,
  presentDeliveryDetail,
  formatDateTime,
  formatGallons,
  ticketStatusLabels,
} from "./lib";

export function PageHeading({
  eyebrow,
  title,
  description,
  children,
}: {
  eyebrow: string;
  title: string;
  description: string;
  children?: ReactNode;
}) {
  return (
    <div className="page-heading">
      <div>
        <p className="eyebrow">{eyebrow}</p>
        <h1>{title}</h1>
        <p>{description}</p>
      </div>
      {children}
    </div>
  );
}

export function Messages({
  error,
  notice,
}: {
  error?: string;
  notice?: string;
}) {
  return (
    <>
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
    </>
  );
}

export function Loading({ text = "Cargando…" }: { text?: string }) {
  return (
    <p className="empty" role="status">
      {text}
    </p>
  );
}

export function Empty({ title, text }: { title: string; text: string }) {
  return (
    <div className="empty">
      <strong>{title}</strong>
      <p>{text}</p>
    </div>
  );
}

// Diálogo modal nativo: foco atrapado, Escape cierra. Para acciones destructivas o
// que necesitan motivo. `onConfirm` recibe los datos del formulario del diálogo.
export function ConfirmDialog({
  title,
  description,
  confirmLabel,
  danger,
  children,
  onConfirm,
  onClose,
}: {
  title: string;
  description?: ReactNode;
  confirmLabel: string;
  danger?: boolean;
  children?: ReactNode;
  onConfirm: (form: FormData) => Promise<void>;
  onClose: () => void;
}) {
  const ref = useRef<HTMLDialogElement>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  useEffect(() => {
    const dialog = ref.current;
    if (dialog && !dialog.open) dialog.showModal();
  }, []);
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setBusy(true);
    setError("");
    try {
      await onConfirm(new FormData(event.currentTarget));
      onClose();
    } catch (e) {
      setError(explain(e));
      setBusy(false);
    }
  }
  return (
    <dialog
      ref={ref}
      className="dialog"
      aria-labelledby="dialog-title"
      onCancel={(event) => {
        event.preventDefault();
        if (!busy) onClose();
      }}
    >
      <form onSubmit={submit}>
        <h2 id="dialog-title">{title}</h2>
        {description && <div className="dialog-text">{description}</div>}
        {children}
        {error && (
          <p className="error" role="alert">
            {error}
          </p>
        )}
        <div className="dialog-actions">
          <button type="button" onClick={onClose} disabled={busy}>
            Volver
          </button>
          <button className={danger ? "primary danger" : "primary"} disabled={busy}>
            {busy ? "Procesando…" : confirmLabel}
          </button>
        </div>
      </form>
    </dialog>
  );
}

export function Tabs<T extends string>({
  label,
  tabs,
  value,
  onChange,
}: {
  label: string;
  tabs: [T, string][];
  value: T;
  onChange: (value: T) => void;
}) {
  return (
    <div className="tabs" role="group" aria-label={label}>
      {tabs.map(([key, text]) => (
        <button
          key={key}
          type="button"
          aria-pressed={value === key}
          onClick={() => onChange(key)}
        >
          {text}
        </button>
      ))}
    </div>
  );
}

const statusTone: Record<string, string> = {
  Created: "info",
  Sent: "info",
  Pending: "warn",
  NearExpiry: "warn",
  Expired: "danger",
  Consumed: "",
  Voided: "inactive",
};
export function TicketStatus({ status }: { status: string }) {
  return (
    <span className={`badge ${statusTone[status] ?? ""}`}>
      {ticketStatusLabels[status] ?? status}
    </span>
  );
}

export function Badge({
  tone = "",
  children,
}: {
  tone?: "" | "info" | "warn" | "danger" | "inactive";
  children: ReactNode;
}) {
  return <span className={`badge ${tone}`}>{children}</span>;
}

// Barra de nivel de un tanque con marca del nivel crítico.
export function LevelBar({
  balance,
  capacity,
  critical,
  criticalLevel,
  label,
}: {
  balance: number;
  capacity: number;
  critical: boolean;
  criticalLevel: number;
  label: string;
}) {
  const percent = capacity > 0 ? Math.min(100, (balance / capacity) * 100) : 0;
  const mark =
    capacity > 0 ? Math.min(100, (criticalLevel / capacity) * 100) : 0;
  return (
    <div
      className={`level ${critical ? "critical" : ""}`}
      role="meter"
      aria-label={label}
      aria-valuemin={0}
      aria-valuemax={capacity}
      aria-valuenow={balance}
      aria-valuetext={`${formatGallons(balance)} de ${formatGallons(capacity)}${critical ? ", nivel crítico" : ""}`}
    >
      <span className="level-fill" style={{ width: `${percent}%` }} />
      <span className="level-mark" style={{ left: `${mark}%` }} />
    </div>
  );
}

export type Delivery = {
  channel: string;
  destination: string;
  result: string;
  detail: string;
  attemptedAt: string;
};
export function Deliveries({ deliveries }: { deliveries: Delivery[] }) {
  if (deliveries.length === 0)
    return <p className="hint">No hay intentos de entrega registrados.</p>;
  return (
    <ul className="deliveries">
      {deliveries.map((d, index) => (
        <li key={index} className={`delivery ${d.result.toLowerCase()}`}>
          <div>
            <strong>{channelLabels[d.channel] ?? d.channel}</strong>
            <span className="mono">{d.destination}</span>
          </div>
          <div>
            <Badge
              tone={
                d.result === "Sent"
                  ? ""
                  : d.result === "Outbox"
                    ? "warn"
                    : "danger"
              }
            >
              {deliveryResultLabels[d.result] ?? d.result}
            </Badge>
            <small>{formatDateTime(d.attemptedAt)}</small>
          </div>
          {d.detail && (
            <p>{presentDeliveryDetail(d.channel, d.result, d.detail)}</p>
          )}
        </li>
      ))}
    </ul>
  );
}

export function Pager({
  page,
  total,
  pageSize = 50,
  loading,
  onPage,
  unit = "registros",
}: {
  page: number;
  total: number;
  pageSize?: number;
  loading?: boolean;
  onPage: (page: number) => void;
  unit?: string;
}) {
  return (
    <div className="pagination">
      <span>
        Página {page} · {total} {unit}
      </span>
      <div>
        <button
          disabled={page === 1 || loading}
          onClick={() => onPage(page - 1)}
        >
          Anterior
        </button>
        <button
          disabled={page * pageSize >= total || loading}
          onClick={() => onPage(page + 1)}
        >
          Siguiente
        </button>
      </div>
    </div>
  );
}

// Datos clave/valor en rejilla (detalle de ticket, tarjeta de despacho).
export function Facts({ items }: { items: [string, ReactNode][] }) {
  return (
    <dl className="facts">
      {items.map(([term, value]) => (
        <div key={term}>
          <dt>{term}</dt>
          <dd>{value}</dd>
        </div>
      ))}
    </dl>
  );
}
