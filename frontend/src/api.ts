export type Session = {
  accessToken: string;
  refreshToken: string;
  expiresAt: string;
  userId: string;
  displayName: string;
  roles: string[];
};
let current: Session | null = null;
const listeners = new Set<() => void>();
export const sessionStore = {
  get: () => current,
  subscribe: (listener: () => void) => {
    listeners.add(listener);
    return () => {
      listeners.delete(listener);
    };
  },
  set: (session: Session | null) => {
    current = session;
    listeners.forEach((listener) => listener());
  },
};
let renewal: Promise<boolean> | null = null;
async function renew() {
  const snapshot = current;
  if (!snapshot) return false;
  const response = await fetch("/api/auth/refresh", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ refreshToken: snapshot.refreshToken }),
  });
  if (current !== snapshot) return current !== null;
  if (!response.ok) {
    sessionStore.set(null);
    return false;
  }
  sessionStore.set((await response.json()) as Session);
  return true;
}
async function request(
  path: string,
  init: RequestInit,
  retry: boolean,
): Promise<Response> {
  const headers = new Headers(init.headers);
  if (init.body) headers.set("Content-Type", "application/json");
  if (current) headers.set("Authorization", `Bearer ${current.accessToken}`);
  const response = await fetch(path, { ...init, headers });
  if (
    response.status === 401 &&
    current &&
    retry &&
    path !== "/api/auth/login"
  ) {
    renewal ??= renew().finally(() => {
      renewal = null;
    });
    if (await renewal) return request(path, init, false);
  }
  if (!response.ok) {
    const data = (await response.json().catch(() => null)) as {
      title?: string;
      error?: string;
      errors?: Record<string, string[]>;
    } | null;
    const messages: Record<number, string> = {
      401: "Credenciales o código incorrectos, cuenta bloqueada o sesión vencida.",
      403: "Tu rol no permite esta operación.",
      409: "Ya existe un registro con esos datos.",
      412: "Otra persona modificó el registro. Recarga antes de guardar.",
      429: "Demasiados intentos. Espera un minuto.",
    };
    const detail = data?.errors
      ? Object.values(data.errors).flat().join(" ")
      : data?.error;
    throw new Error(
      detail ??
        messages[response.status] ??
        data?.title ??
        `No se pudo completar la operación (${response.status}).`,
    );
  }
  return response;
}
export async function api<T>(
  path: string,
  init: RequestInit = {},
  retry = true,
): Promise<T> {
  const response = await request(path, init, retry);
  return response.status === 204
    ? (undefined as T)
    : ((await response.json()) as T);
}
// Archivos protegidos (PDF, QR, reportes): el token vive solo en memoria, así que
// no sirven enlaces directos; se descargan con Authorization y se usan como blob.
export async function apiBlob(path: string, init: RequestInit = {}) {
  const response = await request(path, init, true);
  const disposition = response.headers.get("Content-Disposition") ?? "";
  const encoded = /filename\*=UTF-8''([^;]+)/i.exec(disposition)?.[1];
  const plain = /filename="?([^";]+)"?/i.exec(disposition)?.[1];
  return {
    blob: await response.blob(),
    filename: encoded ? decodeURIComponent(encoded) : (plain ?? null),
  };
}
export async function login(
  email: string,
  password: string,
  code: string,
  recoveryCode: string,
) {
  const session = await api<Session>(
    "/api/auth/login",
    {
      method: "POST",
      body: JSON.stringify({
        email,
        password,
        code: code || null,
        recoveryCode: recoveryCode || null,
      }),
    },
    false,
  );
  sessionStore.set(session);
}
export async function logout() {
  await api<void>("/api/auth/logout", { method: "POST" });
  sessionStore.set(null);
}
