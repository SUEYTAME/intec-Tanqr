// Prueba de carga básica (Fase 6). Uso:
//   BASE_URL=https://host LOAD_EMAIL=... LOAD_PASSWORD=... node scripts/prueba-carga.mjs [usuarios] [segundos]
// Inicia sesión una vez (el login tiene límite de 60/min/IP) y reparte GET autenticados entre
// usuarios virtuales concurrentes. Informa latencias p50/p95/p99 y errores por ruta.
const base = process.env.BASE_URL ?? "https://localhost";
const email = process.env.LOAD_EMAIL;
const password = process.env.LOAD_PASSWORD;
if (!email || !password) throw new Error("Faltan LOAD_EMAIL y LOAD_PASSWORD.");
const users = Number(process.argv[2] ?? 50);
const seconds = Number(process.argv[3] ?? 30);
const routes = ["/api/dashboard", "/api/tickets/?page=1", "/api/inventario/", "/api/solicitudes/?page=1", "/api/notificaciones/?unread=true"];

const login = await fetch(`${base}/api/auth/login`, {
  method: "POST",
  headers: { "Content-Type": "application/json" },
  body: JSON.stringify({ email, password }),
});
if (!login.ok) throw new Error(`Login falló: ${login.status}`);
const { accessToken } = await login.json();
const headers = { Authorization: `Bearer ${accessToken}` };

const stats = Object.fromEntries(routes.map((r) => [r, { times: [], errors: 0, codes: {} }]));
const end = Date.now() + seconds * 1000;
async function worker(id) {
  for (let i = id; Date.now() < end; i++) {
    const route = routes[i % routes.length];
    const started = performance.now();
    try {
      const response = await fetch(base + route, { headers });
      await response.arrayBuffer();
      const s = stats[route];
      s.times.push(performance.now() - started);
      if (!response.ok) { s.errors++; s.codes[response.status] = (s.codes[response.status] ?? 0) + 1; }
    } catch (error) {
      stats[route].errors++;
      stats[route].codes[error.cause?.code ?? "red"] = (stats[route].codes[error.cause?.code ?? "red"] ?? 0) + 1;
    }
  }
}
const t0 = Date.now();
await Promise.all(Array.from({ length: users }, (_, i) => worker(i)));
const elapsed = (Date.now() - t0) / 1000;
const pct = (a, p) => a.length ? a[Math.min(a.length - 1, Math.floor((p / 100) * a.length))].toFixed(1) : "-";
let total = 0, errors = 0;
console.log(`usuarios=${users} duración=${elapsed.toFixed(1)}s base=${base}`);
for (const [route, s] of Object.entries(stats)) {
  s.times.sort((a, b) => a - b);
  total += s.times.length; errors += s.errors;
  console.log(`${route.padEnd(34)} n=${String(s.times.length).padStart(6)} p50=${pct(s.times, 50)}ms p95=${pct(s.times, 95)}ms p99=${pct(s.times, 99)}ms errores=${s.errors} ${JSON.stringify(s.codes)}`);
}
console.log(`TOTAL n=${total} rps=${(total / elapsed).toFixed(1)} errores=${errors} (${((errors / Math.max(total, 1)) * 100).toFixed(2)}%)`);
if (errors > 0) process.exitCode = 1;
