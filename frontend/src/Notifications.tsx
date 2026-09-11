import { useCallback, useState } from "react";
import { api } from "./api";
import { Empty, Loading, Messages, PageHeading, Pager, Tabs } from "./components";
import { explain, formatDateTime, notificationKindLabels, useLoad } from "./lib";

type Notification = {
  id: number;
  kind: string;
  message: string;
  entityId: string;
  createdAt: string;
  read: boolean;
};
type Inbox = { items: Notification[]; total: number; unread: number };

// RF-23: alertas del sistema (vencimientos, inventario bajo, fallas de integración).
export function Notifications({ onChange }: { onChange: () => void }) {
  const [filter, setFilter] = useState<"unread" | "all">("unread");
  const [page, setPage] = useState(1);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  const load = useCallback(
    () =>
      api<Inbox>(
        `/api/notificaciones/?page=${page}${filter === "unread" ? "&unread=true" : ""}`,
      ),
    [filter, page],
  );
  const inbox = useLoad(load);

  async function mark(path: string) {
    setBusy(true);
    setError("");
    try {
      await api(path, { method: "POST" });
      inbox.reload();
      onChange();
    } catch (e) {
      setError(explain(e));
    } finally {
      setBusy(false);
    }
  }

  return (
    <>
      <PageHeading
        eyebrow="ALERTAS"
        title="Notificaciones"
        description="Tickets por vencer o vencidos, inventario bajo, ajustes y fallas de entrega o de programación."
      >
        <button
          disabled={busy || !inbox.data || inbox.data.unread === 0}
          onClick={() => void mark("/api/notificaciones/leidas")}
        >
          Marcar todas como leídas
        </button>
      </PageHeading>
      <Tabs
        label="Filtro de notificaciones"
        tabs={[
          ["unread", `Sin leer${inbox.data ? ` (${inbox.data.unread})` : ""}`],
          ["all", "Todas"],
        ]}
        value={filter}
        onChange={(value) => {
          setFilter(value);
          setPage(1);
        }}
      />
      <Messages error={error || inbox.error} />
      <section className="data-panel">
        {inbox.loading && !inbox.data ? (
          <Loading />
        ) : !inbox.data || inbox.data.items.length === 0 ? (
          <Empty
            title={filter === "unread" ? "Todo al día" : "Sin notificaciones"}
            text="Las alertas aparecen aquí cuando el sistema las genera."
          />
        ) : (
          <div className="table-scroll">
            <table>
              <thead>
                <tr>
                  <th>Fecha y hora</th>
                  <th>Tipo</th>
                  <th>Mensaje</th>
                  <th>Acción</th>
                </tr>
              </thead>
              <tbody>
                {inbox.data.items.map((n) => (
                  <tr key={n.id} className={n.read ? "" : "unread"}>
                    <td>{formatDateTime(n.createdAt)}</td>
                    <td>{notificationKindLabels[n.kind] ?? n.kind}</td>
                    <td className="wrap">{n.message}</td>
                    <td>
                      {n.read ? (
                        <small>Leída</small>
                      ) : (
                        <button
                          className="text-button"
                          disabled={busy}
                          onClick={() =>
                            void mark(`/api/notificaciones/${n.id}/leida`)
                          }
                        >
                          Marcar leída
                        </button>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
        {inbox.data && (
          <Pager
            page={page}
            total={inbox.data.total}
            loading={inbox.loading}
            onPage={setPage}
            unit="notificaciones"
          />
        )}
      </section>
    </>
  );
}
