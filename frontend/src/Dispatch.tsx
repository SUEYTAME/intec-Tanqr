import { useCallback, useEffect, useRef, useState } from "react";
import type { ChangeEvent, FormEvent } from "react";
import { api } from "./api";
import { Facts, Messages, PageHeading, TicketStatus } from "./components";
import {
  explain,
  formatDateTime,
  formatGallons,
  numberValue,
  textValue,
  useLoad,
  useOnline,
} from "./lib";
import { decodeImage, getDetector } from "./scanner";

type ValidatedTicket = {
  id: string;
  number: string;
  status: string;
  authorizedQuantity: number;
  issuedAt: string;
  expiresAt: string;
  employeeName: string;
  employeeCode: string;
  nationalIdLast4: string;
  vehiclePlate: string;
  vehicleCode: string;
  department: string;
  fuelType: string;
  fuelTypeId: string;
};
type Validation = {
  valid: boolean;
  error: string | null;
  ticket: ValidatedTicket;
};
type StockTank = {
  id: string;
  code: string;
  active: boolean;
  balance: number;
  station: string;
  stationId: string;
  fuelTypeId: string;
  fuelType: string;
};
type DispatchResult = {
  id: string;
  ticketNumber: string;
  quantity: number;
  difference: number;
  tankBalance: number;
};

// En contextos no seguros (HTTP fuera de localhost) mediaDevices no existe aunque el tipo diga lo contrario.
const cameraAvailable = () =>
  typeof (navigator.mediaDevices as MediaDevices | undefined)?.getUserMedia ===
  "function";

// Cámara trasera + detector; se detiene al primer QR leído o al cerrar.
function CameraScanner({
  onResult,
  onClose,
}: {
  onResult: (text: string) => void;
  onClose: () => void;
}) {
  const video = useRef<HTMLVideoElement>(null);
  const [error, setError] = useState(
    cameraAvailable()
      ? ""
      : "Este navegador no permite usar la cámara aquí. La cámara requiere HTTPS (o localhost). Usa el lector externo o carga una foto del QR.",
  );
  useEffect(() => {
    if (!cameraAvailable()) return;
    let stream: MediaStream | null = null;
    let timer = 0;
    let stopped = false;
    async function start() {
      const detector = await getDetector();
      stream = await navigator.mediaDevices.getUserMedia({
        video: { facingMode: "environment" },
        audio: false,
      });
      if (stopped || !video.current) return;
      video.current.srcObject = stream;
      await video.current.play();
      const tick = async () => {
        if (stopped || !video.current) return;
        const found = await detector.detect(video.current);
        if (stopped) return;
        if (found[0]) {
          stopped = true;
          onResult(found[0].rawValue);
          return;
        }
        timer = window.setTimeout(() => void tick().catch(fail), 250);
      };
      await tick();
    }
    function fail(e: unknown) {
      if (stopped) return;
      stopped = true;
      setError(
        e instanceof DOMException && e.name === "NotAllowedError"
          ? "Permiso de cámara denegado. Actívalo en el navegador o usa otra opción."
          : explain(e),
      );
    }
    void start().catch(fail);
    return () => {
      stopped = true;
      window.clearTimeout(timer);
      stream?.getTracks().forEach((track) => track.stop());
    };
  }, [onResult]);
  return (
    <div className="camera">
      {error ? (
        <p className="error" role="alert">
          {error}
        </p>
      ) : (
        <>
          <video ref={video} muted playsInline aria-label="Vista de la cámara" />
          <p className="hint" role="status">
            Apunta al QR del ticket. Se lee solo.
          </p>
        </>
      )}
      <button type="button" onClick={onClose}>
        Cerrar cámara
      </button>
    </div>
  );
}

// RF-12/RF-13, H-05, H-07: despacho con QR firmado, confirmación de identidad y
// conexión obligatoria. Nunca se guardan despachos para enviarlos después.
export function Dispatch() {
  const online = useOnline();
  const [camera, setCamera] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [qr, setQr] = useState("");
  const [validation, setValidation] = useState<Validation | null>(null);
  const [result, setResult] = useState<DispatchResult | null>(null);
  const [tankId, setTankId] = useState("");
  const [quantity, setQuantity] = useState("");
  const [identity, setIdentity] = useState(false);
  const loadTanks = useCallback(
    () => api<{ tanks: StockTank[] }>("/api/inventario/"),
    [],
  );
  const stock = useLoad(loadTanks);
  const typed = useRef<HTMLInputElement>(null);

  const reset = useCallback(() => {
    setQr("");
    setValidation(null);
    setResult(null);
    setTankId("");
    setQuantity("");
    setIdentity(false);
    setError("");
    if (typed.current) typed.current.value = "";
  }, []);

  const validate = useCallback(
    async (text: string) => {
      const code = text.trim();
      if (!code) return;
      reset();
      setBusy(true);
      try {
        const response = await api<Validation>("/api/despachos/validar", {
          method: "POST",
          body: JSON.stringify({ qr: code }),
        });
        setQr(code);
        setValidation(response);
      } catch (e) {
        setError(`QR rechazado: ${explain(e)}`);
      } finally {
        setBusy(false);
      }
    },
    [reset],
  );
  const onCamera = useCallback(
    (text: string) => {
      setCamera(false);
      void validate(text);
    },
    [validate],
  );

  async function onPhoto(event: ChangeEvent<HTMLInputElement>) {
    const input = event.currentTarget;
    const file = input.files?.[0];
    if (!file) return;
    reset();
    setBusy(true);
    try {
      const text = await decodeImage(file);
      if (!text) {
        setError(
          "No se encontró un QR legible en la foto. Toma otra más cerca y con buena luz.",
        );
        setBusy(false);
        return;
      }
      await validate(text);
    } catch (e) {
      setError(explain(e));
      setBusy(false);
    } finally {
      input.value = "";
    }
  }

  function onTyped(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    void validate(textValue(new FormData(event.currentTarget), "qr"));
  }

  async function confirm(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!validation || !navigator.onLine) return;
    const form = new FormData(event.currentTarget);
    setBusy(true);
    setError("");
    try {
      const response = await api<DispatchResult>("/api/despachos/", {
        method: "POST",
        body: JSON.stringify({
          qr,
          tankId,
          quantity: numberValue(form, "quantity"),
          identityConfirmed: identity,
          odometer: numberValue(form, "odometer"),
          differenceReason: textValue(form, "differenceReason") || null,
          observations: textValue(form, "observations") || null,
        }),
      });
      setResult(response);
      setValidation(null);
      stock.reload();
    } catch (e) {
      setError(
        e instanceof TypeError
          ? "Sin conexión con el servidor: el despacho NO se registró. Vuelve a intentarlo con conexión."
          : explain(e),
      );
    } finally {
      setBusy(false);
    }
  }

  const ticket = validation?.ticket;
  const tanks = (stock.data?.tanks ?? []).filter(
    (t) => t.active && t.fuelTypeId === ticket?.fuelTypeId,
  );
  const tank = tanks.find((t) => t.id === tankId);
  const amount = Number(quantity);
  const short =
    ticket !== undefined &&
    quantity !== "" &&
    amount > 0 &&
    amount < ticket.authorizedQuantity;

  return (
    <div className="dispatch">
      <PageHeading
        eyebrow="DESPACHO"
        title="Despacho"
        description="Lee el QR del ticket, verifica al portador y registra los galones servidos."
      />
      {!online && (
        <p className="offline-banner" role="alert">
          <strong>Sin conexión.</strong> El despacho requiere internet: no se
          puede validar ni confirmar, y no se guarda nada para enviarlo
          después.
        </p>
      )}
      <Messages error={error || stock.error} />

      {result ? (
        <section className="result-card success" aria-live="polite">
          <p className="eyebrow">DESPACHO REGISTRADO</p>
          <h2>Ticket {result.ticketNumber}</h2>
          <Facts
            items={[
              ["Galones despachados", formatGallons(result.quantity)],
              ["Diferencia con lo autorizado", formatGallons(result.difference)],
              ["Nuevo saldo del tanque", formatGallons(result.tankBalance)],
            ]}
          />
          <button className="primary" onClick={reset}>
            Despachar otro ticket
          </button>
        </section>
      ) : ticket ? (
        <>
          <section
            className={`ticket-card ${validation.valid ? "valid" : "invalid"}`}
            aria-labelledby="ticket-card-title"
          >
            <div className="ticket-card-head">
              <div>
                <p className="eyebrow">
                  {validation.valid ? "TICKET VÁLIDO" : "TICKET NO DESPACHABLE"}
                </p>
                <h2 id="ticket-card-title">{ticket.number}</h2>
              </div>
              <TicketStatus status={ticket.status} />
            </div>
            {!validation.valid && (
              <p className="error" role="alert">
                {validation.error}
              </p>
            )}
            <p className="ticket-person">{ticket.employeeName}</p>
            <Facts
              items={[
                ["Código de empleado", ticket.employeeCode],
                ["Cédula (últimos 4)", <strong key="cedula" className="big">{ticket.nationalIdLast4}</strong>],
                ["Placa", <strong key="placa" className="big">{ticket.vehiclePlate}</strong>],
                ["Ficha", ticket.vehicleCode],
                ["Combustible", ticket.fuelType],
                ["Autorizado", <strong key="autorizado" className="big">{formatGallons(ticket.authorizedQuantity)}</strong>],
                ["Departamento", ticket.department],
                ["Vence", formatDateTime(ticket.expiresAt)],
              ]}
            />
          </section>
          {validation.valid ? (
            <form className="editor dispatch-form" onSubmit={confirm}>
              <label className="check identity">
                <input
                  type="checkbox"
                  checked={identity}
                  onChange={(e) => setIdentity(e.target.checked)}
                  required
                />
                <span>
                  Verifiqué la cédula del portador
                  <small>
                    Debe terminar en {ticket.nationalIdLast4} y la persona debe
                    coincidir con {ticket.employeeName}.
                  </small>
                </span>
              </label>
              <label>
                Tanque
                <select
                  required
                  value={tankId}
                  onChange={(e) => setTankId(e.target.value)}
                >
                  <option value="">
                    {tanks.length === 0
                      ? `No hay tanques activos de ${ticket.fuelType}`
                      : "Selecciona el tanque"}
                  </option>
                  {tanks.map((t) => (
                    <option key={t.id} value={t.id}>
                      {t.code} · {t.station} · {formatGallons(t.balance)}
                    </option>
                  ))}
                </select>
              </label>
              {tank && (
                <p className="hint">
                  Existencia actual del tanque {tank.code}:{" "}
                  <strong>{formatGallons(tank.balance)}</strong>
                </p>
              )}
              <label>
                Galones despachados
                <input
                  name="quantity"
                  type="number"
                  inputMode="decimal"
                  required
                  min={0.001}
                  step={0.001}
                  max={ticket.authorizedQuantity}
                  value={quantity}
                  onChange={(e) => setQuantity(e.target.value)}
                  aria-describedby="quantity-help"
                />
                <small id="quantity-help">
                  Máximo {formatGallons(ticket.authorizedQuantity)}.
                </small>
              </label>
              {short && (
                <label>
                  Motivo de la diferencia
                  <textarea
                    name="differenceReason"
                    required
                    maxLength={500}
                    rows={2}
                    aria-describedby="difference-help"
                  />
                  <small id="difference-help">
                    Faltan{" "}
                    {formatGallons(ticket.authorizedQuantity - amount)} respecto
                    a lo autorizado.
                  </small>
                </label>
              )}
              <label>
                Odómetro (km, opcional)
                <input
                  name="odometer"
                  type="number"
                  inputMode="numeric"
                  min={0}
                  step={1}
                />
              </label>
              <label>
                Observaciones (opcional)
                <textarea name="observations" maxLength={500} rows={2} />
              </label>
              <div className="button-column">
                <button
                  className="primary big-button"
                  disabled={busy || !online || !identity}
                >
                  {busy ? "Registrando…" : "Confirmar despacho"}
                </button>
                <button type="button" onClick={reset} disabled={busy}>
                  Cancelar y leer otro QR
                </button>
              </div>
            </form>
          ) : (
            <button className="primary" onClick={reset}>
              Leer otro QR
            </button>
          )}
        </>
      ) : (
        <section className="editor scan-panel" aria-label="Lectura del QR">
          {camera ? (
            <CameraScanner onResult={onCamera} onClose={() => setCamera(false)} />
          ) : (
            <button
              className="primary big-button"
              onClick={() => setCamera(true)}
              disabled={busy || !online}
            >
              Escanear con la cámara
            </button>
          )}
          <form onSubmit={onTyped} className="scan-typed">
            <label>
              Código QR (lector externo o texto)
              <input
                ref={typed}
                name="qr"
                autoComplete="off"
                autoCapitalize="off"
                spellCheck={false}
                maxLength={400}
                aria-describedby="typed-help"
              />
              <small id="typed-help">
                Los lectores USB o Bluetooth escriben el código y pulsan Enter.
              </small>
            </label>
            <button type="submit" disabled={busy || !online}>
              Validar código
            </button>
          </form>
          <label className="file-pick">
            Cargar foto del QR
            <input
              type="file"
              accept="image/*"
              onChange={(e) => void onPhoto(e)}
              disabled={busy || !online}
            />
          </label>
          {busy && (
            <p className="hint" role="status">
              Validando…
            </p>
          )}
        </section>
      )}
    </div>
  );
}
