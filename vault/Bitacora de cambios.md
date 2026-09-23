---
tipo: proyecto
estado: activo
actualizado: 2026-09-22
---

# Bitácora de cambios

Changelog del proyecto. Más reciente arriba. Esta nota es la que permite que un agente sepa
qué tocó el otro sin tener que leer el diff entero.

**Formato de cada entrada:**

```
## AAAA-MM-DD — Título corto
**Agente:** Claude | Astra | ambos
**Qué cambió:** en una o dos frases, en lenguaje llano.
**Archivos:** rutas tocadas.
**Verificado con:** el comando exacto que se corrió y su resultado. Si no se corrió nada, se dice.
**Commit:** hash corto, o "sin commit" y por qué.
**Requisitos afectados:** RF-xx / RS-xx, o "ninguno".
```

**"Verificado con" no es opcional.** Si dice "no se corrió nada", al menos es honesto y el
siguiente agente sabe que tiene que comprobarlo. Lo que no vale es omitir la línea.

---

## 2026-09-22 — Configuración delegada y primera administración funcional

**Agente:** Astra. **Rama:** `fase-1-dominio`.
**Autorización:** el usuario delegó crear configuración/recursos y resolver pendientes,
priorizando acceso sencillo y continuación autónoma. ADR-007/008 registran decisiones;
no se inventaron instalaciones ni personas reales de INTEC.

**Qué cambió / archivos:** modelos y migración en `backend/src/Combustible.Domain/` e
`Infrastructure/Data/`; contratos en `Application/Requests.cs`; API en `Program.cs`,
`Endpoints/`, `Security/`, `DatabaseBootstrap.cs`; pruebas en `backend/tests/`.
Web en `frontend/src/`, `frontend/e2e/` y `playwright.config.ts`. Arranque en `scripts/`
y `Iniciar.cmd`. `.env.example`, README y CI actualizados. Las cuatro notas de continuidad
reflejan que existe producto parcial, no solo un esqueleto.

**Infraestructura real:** `.env` creado con secreto aleatorio. Primer `docker compose up -d`
falló por puerto 5432 ocupado (PID 7424, confirmado con `Get-NetTCPConnection`). Se comprobó
15432 libre con `TcpListener`, se configuró solo `.env` y luego el ejemplo. No se detuvo
el proceso ajeno ni se borró volumen. PostgreSQL 17.11 quedó healthy. Repo privado creado
con `gh repo create SUEYTAME/intec-combustible --private --source . --remote origin --push`.
CI de Fase 0 pasó: https://github.com/SUEYTAME/intec-combustible/actions/runs/35759842395.

**Comandos ejecutados y resultados:**

| Directorio | Comando | Resultado |
|---|---|---|
| raíz | `docker info --format '{{json .ServerVersion}}'` | 29.6.1 activo |
| raíz | `docker compose config --quiet` | 0, variables presentes |
| raíz | `docker compose up -d` | Fallo 5432; después éxito con 15432 |
| raíz | `docker compose ps` | `combustible-db` healthy, 127.0.0.1:15432 |
| raíz | `docker compose exec -T db sh -c 'pg_isready -U "$POSTGRES_USER" -d "$POSTGRES_DB"'` | accepting connections |
| raíz | `docker compose exec -T db sh -c 'PGPASSWORD="$POSTGRES_PASSWORD" psql -h 127.0.0.1 -U "$POSTGRES_USER" -d "$POSTGRES_DB" -v ON_ERROR_STOP=1 -c "SELECT current_database(), version();"'` | combustible / PostgreSQL 17.11 |
| backend | `dotnet ef migrations add InitialIdentityAndCatalogs --project src/Combustible.Infrastructure --startup-project src/Combustible.Api --output-dir Data/Migrations` | Migración generada; configuración local cargada desde script, sin imprimir secretos |
| raíz | `./scripts/preparar.ps1` | Migración aplicada; 5 roles y administrador local creados. Repetición idempotente correcta |
| backend | `dotnet build --nologo -v quiet` | 0 warnings, 0 errores tras corregir fallo del analizador de handlers genéricos |
| backend | `dotnet test --nologo --logger 'console;verbosity=normal'` | Suites intermedias 15/15 y 21/21 después de corregir los fallos descritos abajo |
| backend | `dotnet build --configuration Release --nologo -v quiet` | 0 warnings, 0 errores |
| backend | `dotnet test --configuration Release --no-build --logger 'console;verbosity=normal'` | **22/22**, sin omitidas; PostgreSQL efímero real, rol SQL limitado |
| frontend | `npm run build` / `npm run lint` | Ambos pasan; lint sin avisos |
| frontend | `npx playwright install chromium` | Chromium instalado para pruebas |
| raíz | `./scripts/iniciar.ps1` | API readiness y web responden 200; credenciales solo en archivo ignorado |
| raíz | `./scripts/probar-interfaz.ps1` | **2/2**: escritorio y Pixel 7 emulado; login, crear/editar/desactivar departamento, navegación y logout |
| raíz | `./scripts/detener.ps1` | Solo procesos propios detenidos; PostgreSQL conservado |
| backend | `dotnet list package --vulnerable --include-transitive --format json` | 0 paquetes vulnerables reportados |
| raíz | `& ./artifacts/tools/actionlint-1.7.12/actionlint.exe -color .github/workflows/ci.yml` | Sin errores |
| raíz | `git diff --check` / `git diff --cached --check` | Sin errores |

**Fallos encontrados y resueltos:** el analizador ASP.NET lanzó AD0001 para lambdas de tipo
genérico: se usaron delegates tipados, sin desactivar analizadores. La configuración de
WebApplicationFactory llegaba tarde: corregida con UseSetting. El alta MFA cambiaba
SecurityStamp y revocaba la sesión antes de confirmar el código: ahora devuelve sesión
renovada; probado TOTP real y recuperación de un solo uso. El guard de PID interpretaba
la fecha JSON como string: ahora compara ticks UTC. Hubo un build fallido por DLL bloqueada
por nuestra API; se detuvo únicamente el proceso registrado y la compilación pasó.

**Evidencia local ignorada:** `artifacts/tests-release.log`, `tests-fase1.log`,
`nuget-audit.json`, `startup.log`, capturas `catalogo-desktop.png`, `catalogo-mobile.png`,
`login-desktop.png`, `login-mobile.png`. Capturas inspeccionadas visualmente: sin solapamientos;
la tabla móvil permite desplazamiento horizontal. No equivalen a Android físico/CA-6.

**Commits:** `4c44b4f` (persistencia/ADR), `e4a66b0` (API y pruebas), `a9856f3` (web/arranque/CI).
La nota de continuidad se comitea después para citar estos hashes. CI ampliada de Fase 1
pendiente de ejecutar al escribir este checkpoint; no se hereda el verde de Fase 0.

**Requisitos afectados:** RF-01..RF-04, RF-21, RS-01, RS-02, RS-05 y RS-06 en curso con
implementación y pruebas concretas en trazabilidad. Ningún CA completo cerrado. OAuth 2.0,
cifrado en reposo, producción, tickets, QR, inventario y PWA no se presentan como realizados.

---
## 2026-09-22 — Relevo: Docker activo, configuración pendiente y CI preparada

**Agente:** Astra (Codex). **Rama:** `fase-0-entorno`.

**Qué cambió:** se verificó el estado recibido y se conservó íntegro el cambio local
de Compose: variables obligatorias sin fallback, puertos solo en localhost y Adminer
opcional mediante perfil. `.env.example` deja la contraseña vacía y distingue contratos
previstos de funcionalidades aún inexistentes. Se añadió CI para build/test del backend
y build del frontend, con acciones fijadas a SHA, permiso `contents: read`, credenciales
de checkout no persistentes y límites de tiempo. README y vault reflejan los bloqueos reales.

**Archivos:** `docker-compose.yml` (cambio previo preservado), `.env.example`,
`.github/workflows/ci.yml`, `README.md`, `vault/Estado actual del proyecto.md`,
`vault/Tareas pendientes.md`, `vault/Trazabilidad de requisitos del SRS.md` y esta bitácora.
No se editó código de dominio ni se alteró ningún ADR. No se creó `.env` ni ningún secreto.

**Inspección de Git y concurrencia:** `git status --short --branch`, `git log -5 --oneline`,
`git diff -- docker-compose.yml`, `git diff --cached --stat`, `git remote -v` y
`git status --porcelain=v1 --untracked-files=all`. Base `0408d59`, anterior `907f922`;
sin staging ni remotos. Solo Compose modificado y `vault/.obsidian/.obsidian/` sin rastrear.
Se detectaron procesos Claude con `Get-Process claude,codex -ErrorAction SilentlyContinue`;
se compararon tamaño y `LastWriteTimeUtc` de los archivos antes de editar, sin cambios
concurrentes observados. No se tocaron los archivos de Obsidian ni se detuvo otro proceso.

**Verificado con** (salida 0 salvo donde se indica; rutas relativas a la raíz):

| Directorio | Comando exacto | Resultado real |
|---|---|---|
| raíz | `docker info --format '{{json .ServerVersion}}'` | Servidor `29.6.1` responde; B-06 resuelto |
| raíz | `docker compose version` | v5.3.0 |
| raíz | `Test-Path -LiteralPath '.env'` | False |
| raíz | `[Environment]::GetEnvironmentVariable($name, 'Process')` para cada nombre PostgreSQL | USER, PASSWORD, DB y PORT ausentes; solo se imprimió presencia/ausencia, no valores |
| raíz | `docker compose config --quiet` | **Salida 1**: falta POSTGRES_USER; PASSWORD y DB también están ausentes. No hay configuración interpolada válida |
| raíz | `docker compose config --no-interpolate --quiet` | Salida 0: estructura Compose válida; no verifica credenciales ni arranque |
| raíz | `docker ps -a --filter name=combustible --format '{{.Names}} {{.Status}} {{.Ports}}'` | Sin contenedores coincidentes |
| backend | `dotnet build` | Correcto, 0 warnings y 0 errores |
| backend | `dotnet test` | 2 pasadas, 0 fallidas, 0 omitidas; net10.0 |
| frontend | `npm run build` | Correcto, Vite 8.3.0; bundle JS 222.52 kB |
| backend | `dotnet restore` | Dependencias restauradas/actualizadas correctamente |
| backend | `dotnet build --configuration Release --no-restore` | Correcto, 0 warnings y 0 errores |
| backend | `dotnet test --configuration Release --no-build --no-restore` | 2 pasadas, 0 fallidas, 0 omitidas |
| artifacts/ci-frontend-20260922 | `npm ci` | 27 paquetes instalados; auditoría de npm: 0 vulnerabilidades reportadas |
| artifacts/ci-frontend-20260922 | `npm run build` | Correcto desde instalación limpia; Vite 8.3.0 |
| raíz | `& (Join-Path $toolDir 'actionlint.exe') -version` | 1.7.12; `$toolDir` = `C:\Dev\intec-combustible\artifacts\tools\actionlint-1.7.12` |
| raíz | `& (Join-Path $toolDir 'actionlint.exe') -color .github/workflows/ci.yml` | Sin errores, salida 0 |
| raíz | `git diff --check` y `git diff --cached --check` | Sin errores; Git avisa conversión LF/CRLF según configuración local |
| raíz | `git check-ignore .env` | `.env` ignorado |
| backend | `node --version`, `npm --version`, `dotnet --version` | v24.15.0, 11.12.1 y 10.0.401 |

La copia de frontend se creó con `git ls-files frontend`, `New-Item` y `Copy-Item`,
preservando subdirectorios, en un destino nuevo bajo `artifacts/`. No se sustituyó el
`node_modules` de trabajo. Los artefactos de verificación están ignorados por Git.
`actionlint` se descargó de la publicación oficial v1.7.12 y su ZIP se contrastó con
`actionlint_1.7.12_checksums.txt` mediante `Get-FileHash -Algorithm SHA256` antes de ejecutarlo:
`6E7241B51E6817EA6A047693D8E6FED13B31819C9A0DD6C5A726E1592D22F6E9`.

**Fuentes técnicas comprobadas:** documentación oficial de
[checkout](https://github.com/actions/checkout),
[setup-dotnet](https://github.com/actions/setup-dotnet),
[setup-node](https://github.com/actions/setup-node) y
[actionlint v1.7.12](https://github.com/rhysd/actionlint/releases/tag/v1.7.12).
Se verificaron los SHA utilizados con los comandos:

```powershell
git ls-remote https://github.com/actions/checkout.git refs/tags/v6
git ls-remote https://github.com/actions/setup-dotnet.git refs/tags/v5
git ls-remote https://github.com/actions/setup-node.git refs/tags/v6
Get-FileHash -Algorithm SHA256 -LiteralPath 'docs/SRS.pdf','C:\Users\proje\Downloads\SRS Plataforma Web y Aplicación Móvil para Gestión de Tickets Digitales e Inventario de Combustible Intec.pdf'
```

Los SHA de las acciones coinciden con los fijados en el workflow. Los dos PDF tienen
SHA-256 `45391564A81B75863329C76A7529CCF37E49155AB6D717DC6B4BC9A8673CC79E`.

**Lo que no se verificó:** PostgreSQL, autenticación SQL y salud del contenedor.
`docker compose up -d`, `docker compose ps` y la consulta SQL se omitieron por faltar
las variables requeridas, conforme al límite solicitado. No se ejecutó GitHub Actions
porque no hay remoto; la validación local fue en Windows, no en el runner Ubuntu del workflow.
Los tests existentes no prueban persistencia, negocio ni seguridad.

**Decisiones pendientes:** se pidió al usuario configurar `.env` sin exponer secretos,
definir H-06 y resolver ADR-006. No llegó respuesta durante este bloque. H-01..H-07 y
ADR-006 permanecen abiertos. Se registraron B-07 (variables locales) y B-08 (remoto/CI);
Fase 0 sigue abierta. Próximo bloque: revalidar configuración, arrancar y comprobar
PostgreSQL si hay variables, obtener destino GitHub y respuestas del SRS.

**Commits:** `a6a3e08` (Compose recibido y ejemplo sin contraseña), `6304270`
(workflow y README). La documentación de continuidad se registra después de estos
commits para poder citar sus hashes; su hash se consulta con `git log -1 -- vault`.
No se hizo push ni commit a `main`.

**Requisitos afectados:** ninguno implementado ni cerrado. Los 30 RF/RS y los 7 CA
conservan su estado. El trabajo corresponde exclusivamente a Fase 0.

---

## 2026-09-22 — Fase 0: montaje del entorno

**Agente:** Claude
**Rama:** `fase-0-entorno`

**Qué cambió.** Proyecto creado desde cero: estructura del repositorio, vault de Obsidian
dedicado dentro del repositorio, contrato de agentes, y esqueletos de backend y frontend que
compilan y pasan pruebas. Se instaló .NET 10 LTS y se fijó con `global.json`. Se arregló un
fallo del puente Claude↔Codex que dejaba 10 skills fuera del alcance de Astra.

**Archivos.** 49 archivos. Los que importan: `AGENTS.md`, `CLAUDE.md`, `CODEX.md`,
`README.md`, `docker-compose.yml`, `.env.example`, `backend/` (solución de 5 proyectos),
`frontend/` (Vite React-TS), y las 7 notas de `vault/`.
Fuera del repositorio: `C:\Dev\claude-codex-bridge\sync.ps1` parcheado.

**Verificado con.**

| Comando | Resultado |
|---|---|
| `dotnet test` | **Passed! Failed: 0, Passed: 2** sobre `net10.0`. Las 2 pruebas levantan la API en memoria y comprueban `/health` y `/` |
| `npm run build` | compila, `built in 293ms`, bundle 222.52 kB |
| `dotnet --version` en `backend/` | `10.0.401` — el `global.json` fija el SDK y no se cuela el 9.0.315 |
| `docker compose config --quiet` | sintaxis válida |
| `docker compose up -d` | **FALLÓ.** El daemon de Docker no está corriendo. Ver bloqueo B-06 |
| `sync.ps1 -DryRun` | `enumerated 1389` (antes 1379), `created=10`, `forward=0 backward=0 conflict=0 skipped=1379` |
| `sync.ps1` (real) | las 10 skills creadas; comprobadas una a una en `~/.codex/skills/` |
| Parser de PowerShell sobre `sync.ps1` | sin errores de sintaxis tras el parche |
| `releases-index.json` de Microsoft | `10.0` en `lts/active` hasta `2028-11-14`; `8.0` y `9.0` en `maintenance` hasta `2026-11-10`. Es la base del ADR-001 |

**Lo que NO se verificó.** No se levantó la base de datos ni se probó ninguna conexión a
PostgreSQL, porque el daemon de Docker no arrancó. `docker-compose.yml` está validado
sintácticamente pero **nunca se ha ejecutado**. No se asume que funcione.

**Commit:** `907f922` — 49 archivos, 3329 inserciones.

**Requisitos afectados:** ninguno implementado. Los 30 quedan mapeados en
[[Trazabilidad de requisitos del SRS]], todos en `PENDIENTE` o `BLOQUEADO`.

**Hallazgos.**

1. **El puente dejaba 10 skills fuera.** `Enumerate-Sources` en `sync.ps1` escaneaba
   `~/.claude/skills` solo al primer nivel, así que las skills sincronizadas desde la cuenta
   —que viven tres niveles más abajo, en `skills/synced/<orgId>/<userId>/<skill>/`— nunca
   llegaban a Astra. Entre ellas `xlsx`, `pdf`, `pptx` y `docx`, justo las que hacen falta
   para RF-20. Arreglado y verificado. Detalle en [[Entorno de agentes]].
2. **El `mcp=0` del puente NO es un fallo.** Se comprobó: `~/.claude.json` no define ningún
   servidor MCP, ni global ni en ninguno de sus 32 proyectos. No hay nada portable que
   espejar. Existe además un bug latente de claves duplicadas que rompe el paso de MCP, pero
   hoy no tiene consecuencia. Ambas cosas documentadas en [[Entorno de agentes]].
3. **Astra es GPT‑6 Astra sobre Codex CLI**, confirmado por la entrada `gpt-6-astra` en
   `~/.codex/config.toml`. Por eso el vault en disco funciona como memoria compartida.

## 2026-09-22 — MFA completo y concurrencia verificada

**Commit de código:** `08a91ef`. **Requisitos:** RS-01/RS-05/RS-06 parciales; no se cierra OAuth ni CA-6.
Archivos: `AuthEndpoints.cs`, `Requests.cs`, `ApiFixture.cs`, `ProductTests.cs`,
`SecurityTests.cs`, `SaludDeLaApiTests.cs`, `frontend/src/Administration.tsx`, `frontend/e2e/mfa.spec.ts`.

- Regeneración y baja de MFA requieren contraseña y TOTP o recuperación de un solo uso;
  revocan sesiones anteriores. Interfaz muestra nuevos códigos una vez y exige nuevo login.
- Refresh simultáneo: solo uno emite respuesta correcta; el replay revoca también la sesión
  ganadora. Ocho escrituras concurrentes conservan la cadena de auditoría.
- Primera ejecución: 20/24 backend; cuatro 429 por compartir IP de TestServer entre casos.
  Se asignó IP independiente por cliente mediante TestServer.CreateHandler; producción
  conserva 60 solicitudes/minuto/IP. Prueba específica del límite sigue activa.
- Primera prueba MFA de navegador falló por selector `Mi cuenta`; UI real dice `Mi seguridad`.
  Corregido el test; capturas deshabilitadas para este flujo que muestra secretos efímeros.
- CI previa verificada: `gh run view 35764488194 --json status,conclusion,jobs` → success
  para `70972c7`. No se atribuye ese resultado a commits posteriores.

Comandos exactos (raíz salvo indicación):

```powershell
dotnet test backend --nologo --logger 'console;verbosity=normal' > artifacts/tests-security.log 2>&1
./scripts/iniciar.ps1
./scripts/probar-interfaz.ps1 > artifacts/browser-security.log 2>&1
dotnet build backend --configuration Release --nologo
git diff --check
# En frontend:
npx --yes prettier --write e2e/mfa.spec.ts
npm run build
npm run lint
```

Resultado final: backend **24/24**, navegador **4/4**, build Release **0 avisos/0 errores**,
frontend build/lint correctos. Arranque local PostgreSQL healthy y API/web accesibles.
Credenciales conservadas en archivos ignorados; cuentas `qa-mfa-...@example.test` usadas
por el navegador quedan inactivas. No se cambió MFA de la cuenta administradora local.
Pendiente: revisar rutas de acceso, auditoría alterada, OAuth/OIDC y seguridad de producción.

## 2026-09-22 — Fase 2-6: tickets, inventario, despacho, seguridad e interfaz completa (Claude)

Rama `fase-2-producto` (desde `fase-1-dominio` @4f7635a). Continúa el trabajo de Astra.

| Commit | Qué |
|---|---|
| `804ca44` | Dominio/infra: combustibles, estaciones, tanques, tickets, solicitudes, programaciones, despachos, movimientos, recepciones, cierres, notificaciones. Firma ECDSA P-256 del QR, cifrado AES-256-GCM de campos, PDF/CSV/XLSX |
| `2db1065` | API: solicitudes→aprobación→ticket numerado sin huecos, entrega correo/SMS tras commit, despacho atómico, cierre diario con bloqueo, reportes, dashboard, alertas, ciclo de vida (vencer/avisar/programaciones/reintentos). 44/44 |
| `bf60ba1` | Scripts compatibles con Windows PowerShell 5.1 (no hay pwsh), Mailpit local, claves en CI |
| `e9a2750` | `GET /api/tickets/{id}/qr.png` para Admin/Supervisor, auditado |
| `2f3013b` | RS-03 cifrado de cédula/correo/móvil con índice ciego + backfill; TLS 1.3 exclusivo; RS-05 OAuth 2.0 client credentials (OpenIddict 7.7.1); RS-06 ancla firmada; versión obligatoria en `/acceso`. **51/51** |
| `d0ac9c7` | Interfaz completa: panel, solicitudes, tickets, programaciones, despacho con escáner QR, cierre, inventario, reportes, notificaciones, parámetros, integraciones, ticket público; menú por rol; PWA (API NetworkOnly); ícono |

Verificación (2026-09-22/23):

```powershell
dotnet test backend -c Release                 # 51/51 (antes de d0ac9c7; backend sin cambios después)
cd frontend; npm run build; npx oxlint          # OK, 0 avisos
powershell -File scripts/iniciar.ps1 -Reiniciar # migraciones EncryptEmployeePersonalData y OAuthClientCredentials aplicadas; 1/1 empleados cifrados
powershell -File scripts/probar-interfaz.ps1    # Playwright 6/6 (escritorio + Pixel 7)
npm audit                                       # 0 vulnerabilidades
dotnet list package --vulnerable --include-transitive  # ninguno
```

Hallazgos y correcciones durante la verificación:
- El agente de frontend (falló por límite de gasto) dejó pantallas sin CSS: añadido `screens.css`.
- TS2774 en `Dispatch.tsx` (mediaDevices es `undefined` en HTTP) → comprobación con `typeof`.
- Con todos los módulos el menú lateral no cabía en 720 px: ahora se desplaza dentro de la barra (lo detectó `screens.spec.ts`).
- `mfa.spec.ts` no enviaba la versión a `/acceso` (400 tras 2f3013b). Corregido.
- `mfa.spec.ts` chocaba con el límite de 60/min/IP de `/api/auth` al correr 6 pruebas seguidas; espera una ventana nueva en lugar de relajar el límite.

Revisión de seguridad CA-7 (manual, 2026-09-22): enlace público con token de 128 bits, comparación en tiempo constante y límite de tasa; sin `innerHTML`/`eval`; tokens solo en memoria; `no-store` y `nosniff` en la API; dependencias sin vulnerabilidades. **Pendiente de decisión del usuario:** un cliente OAuth con rol Supervisor puede aprobar solicitudes, anular tickets y escribir inventario (solo despacho y cierre exigen `sid`).

## 2026-09-22 — Fase 6: despliegue, carga, manual e informe (Claude)

Commit `1c05ca7` + documentación en el commit siguiente.

- Despliegue de un solo origen: la API sirve `frontend/dist` si `WEB_ROOT` está definido (sin proxy:
  el límite de tasa y la auditoría verían la IP del proxy). CSP, `X-Frame-Options`, `Referrer-Policy`.
  Prueba nueva `Un_solo_origen_sirve_la_interfaz_con_CSP_y_la_API_sigue_intacta`.
- `Dockerfile`, `deploy/docker-compose.prod.yml` (paso `init` con propietario, `app` con
  `combustible_app`), `deploy/produccion.env.example` (real `deploy/produccion.env` ignorado).
- `docs/despliegue.md`, `docs/manual-usuario.md`, `docs/informe-final.md`, `scripts/prueba-carga.mjs`.

Verificación:
- `dotnet test backend -c Release` → **52/52**.
- `docker build` → OK tras copiar `.editorconfig` de la raíz (sin él, CA1861 en migraciones rompía el publish).
- Imagen ejecutada con proyecto compose aislado `combustible-smoke` y claves efímeras (borradas):
  init exit 0; `/health/ready` por TLS 1.3; TLS 1.2 rechazado; CSP presente; `/ticket/<clave>` → SPA;
  `sw.js`, manifiesto y `.wasm` con tipo correcto; `/api/no-existe` → 404; login admin OK.
  Se añadió `libgssapi-krb5-2` porque Npgsql lo sondeaba y dejaba "Error:" en el log.
- Carga 50 usuarios × 30 s: 7552 solicitudes, 250,6 rps, 0 errores (base casi vacía).

## 2026-09-22 — Continuación de Astra: GitHub y distribución del contenedor

**Commit de implementación:** `4c105d7`, sobre el relevo de Claude `09d740d`.
El usuario autorizó subir todo a GitHub y continuar con la publicación.

- Subida `fase-2-producto` al repositorio privado existente. No se incluyeron `.env`,
  credenciales, certificados, datos locales ni `vault/.obsidian/` preexistente.
- CI incorpora un job de distribución que depende de backend, frontend y navegador.
  Construye el Dockerfile y publica en GHCR con `GITHUB_TOKEN`, permiso `packages: write`
  limitado al job, etiqueta por SHA completo y etiqueta de rama. No se publica desde PR.
- `docs/despliegue.md` explica cómo descargar una revisión para el compose existente.
  README/informe/vault distinguen la demo de la operación institucional: no hace falta
  esperar datos reales ni recursos de INTEC para una demo; sí acordar hosting y finalidad.
- Se consultó Azure en lectura: suscripción activa con recursos de otros proyectos, sin
  alojamiento de combustible identificado. No se crearon recursos ni se cambiaron servicios.
- OAuth conserva los permisos anteriores mientras se resuelve la pregunta al usuario.
  No se cerró ningún RF/RS ni se atribuyó una nueva revisión de seguridad a esta sesión.

Verificación local repetida:

| Comando | Resultado |
|---|---|
| `dotnet test backend -c Release --nologo` | 52/52, PostgreSQL y SMTP de pruebas reales en contenedores |
| `npm run build` en `frontend` | correcto, incluida generación de PWA |
| `npm run lint` en `frontend` | correcto |
| `git diff --check` | correcto |

La primera CI de `09d740d` (`35814838109`) fue cancelada automáticamente al subir
`4c105d7`; no se cuenta como éxito. CI de publicación: `35814915846`, **success**.
Backend52/52, frontendbuild/lint, navegador6/6 y Docker publicados. Digest de GHCR:
`sha256:daa6cbab29efa116317bbff111ca9afa849e08580948c414ad07d499f86aa50b`.

## 2026-09-23 — Azure INTEC autorizado, infraestructura preparada, MFA pendiente

**Commit:** `345e623`. Usuario autorizó crear/desplegar todo lo necesario en cuenta INTEC;
pidió conservar objetivo/contexto al compactar. Se registró continuidad en este vault y
sesión local ignorada `.copilot-azure/sessions/b805f7aa-7186-4692-98e7-df81256974cc/`.

- Identificada Azure for Students44f41884-c42a-4162-898f-d83d8d987ff3,
  cuenta1128305@est.intec.edu.do, tenant6856181f-daf8-4725-ac51-dd9f7dfe2f2b.
- Registrados proveedores Compute/Network/KeyVault/Storage; solicitada inscripción Communication.
  Sin recursos de aplicación creados. La suscripción LegatTech-Bot no se modificó.
- Cuotas REST y restricciones SKU verificadas por subagente: B2als_v2 en northcentralus,
  2CPU4GiB, totalregional6/0usados, familia10/0, IPStandard3/0. B1ms/B2s restringidos.
- Precio consultado por subagente en Retail API:36.29USD/mes730h, excluye excesos/impuestos/email.
- IaC en `infra/`, arranque administrativo `scripts/azure-infra.ps1`, ADR-014, `docs/azure.md`.
  SSH solo IPadministrador,80ACME/443TLS, disco64GiB, MI, KeyVaultRBAC y Blob privado.
- `az bicep build --file infra/main.bicep --stdout`: correcto sin avisos tras usar versiones
  GA disponibles con esquema local (las GA2026 nuevas aún no tenían tipos para varios recursos).
  `scaffold-conformance.ps1`: `passed:true`, sin fallos. Se ejecutó en PowerShell7.6.5
  incluido en runtimeCodex; WindowsPowerShell5.1 no interpretó UTF8 del script externo.
- `scripts/azure-infra.ps1` what-if: **rechazado por Azure por sesión sin MFA**.
  `az group exists --name rg-intec-fuel-dev-b805 --subscription ...` → false.
  Se abrió reautenticación device-code para el usuario. No se eludió la exigencia.
- Subagente IaC agotó cuota antes de escribir; IaC generado/revisado por main. No hay revisión
  independiente completa ni validación ARM concluida. Aplicación/certificados/backups aún sin desplegar.
- CLI GitHub sin `read:packages`: consulta de privacidad devolvió403. CI ahora verifica
  `visibility=private` con `GITHUB_TOKEN`; el push anterior sí terminó correctamente.
- Código de aplicación sin cambios: se conserva evidencia local52/52, frontendbuild/lint y
  CI52/52+navegador6/6. `git diff --check` correcto. No se cerraron RF/RS nuevos.

**Cierre de publicación:** CI `35818197956` de `345e623` terminó **success**:
backend **52/52**, navegador **6/6**, frontend build/lint y Docker. La API de GitHub,
consultada con el token de Actions, confirmó **`visibility: private`**. Paquete:
https://github.com/users/SUEYTAME/packages/container/package/intec-combustible.
Digest final `sha256:5e9e7f1d61b1b73407a52234cd9761491161ade095fdb265be748f31f4b6af64`.
Las notas de continuidad se subieron en `d8a653f`; este añadido solo registra el resultado
final y usa `[skip ci]` porque no modifica código ni infraestructura. Azure sigue pendiente de MFA.

## 2026-09-23 — Aplicación desplegada y verificada en Azure INTEC (Claude)

**Agente:** Claude. **Rama:** `fase-2-producto`. Autorización del usuario: crear/desplegar
todo lo necesario solo en la suscripción INTEC `44f41884-…`. LegatTech-Bot no se tocó.

**Qué cambió:** la aplicación funciona en https://intec-fuel-dev-b805.northcentralus.cloudapp.azure.com
con certificado Let's Encrypt, PostgreSQL privado, secretos en Key Vault, correo ACS y respaldo
diario a Blob. Detalle operativo en `docs/azure.md`; decisiones en ADR-015.

- **MFA:** el primer device-code emitió token `amr=pwd` y ARM volvió a rechazar. Segundo login con
  `--claims-challenge` que exige `amr=mfa` → token `amr=pwd,mfa`. No se eludió nada.
- **Infra:** `scripts/azure-infra.ps1` what-if `Succeeded` (15 Create, 0 Modify/Delete) y
  `-Deploy` correcto. IP `130.131.46.104`. cloud-init terminó 08:22 UTC.
- **Secretos:** `scripts/azure-secretos.ps1` creó 8 secretos en `kv-intec-fuel-dev-b805` sin imprimirlos.
- **Correo:** `scripts/azure-correo.ps1` + `infra/correo.bicep`: app Entra
  `intec-combustible-smtp-b805` (tenant permite `allowedToCreateApps`), ACS + dominio administrado
  por Azure, SMTP username `combustible-smtp` (el primer intento `combustible-app` falló: no puede
  igualar el nombre del recurso). Secreto de cliente en Key Vault, vence 2027-09-23.
  Remitente `DoNotReply@d6fbd5cf-2eba-4d89-b532-3ff863a8420e.azurecomm.net`.
  **No se ha enviado ningún correo real**: entrega pendiente de prueba con permiso del usuario.
- **SSH inutilizable:** `artifacts/azure/id_ed25519` está cifrada con frase de paso no registrada
  (`ssh-keygen -y -P ""` → incorrect passphrase). No se regeneró. Se instaló con
  `az vm run-command` (`deploy/azure/instalar.sh`); el código de `345e623` viajó por Blob privado
  `deployments/src-345e623.tar.gz` (SHA-256 `9f68c8cd…c3efa2`, comprobado en la VM).
- **Imagen:** construida en la VM desde `345e623` (`intec-combustible:0.4.0-345e623`), no descargada
  de GHCR (el `gh` local no tiene `read:packages`). Mismo código que pasó CI `35818197956`.
- `.gitattributes` fuerza LF en `*.sh` (`core.autocrlf=true` rompería el script en la VM).

**Verificado con (desde internet, 2026-09-23):**
- `openssl s_client -tls1_3` → `TLSv1.3`, `TLS_AES_256_GCM_SHA384`, `Verify return code: 0 (ok)`,
  emisor Let's Encrypt YE1, vence 2026-12-22. `-tls1_2` → alerta 70 `protocol version`, sin cifrado.
- `GET /health/ready` → 200 `{"status":"ready"}`; `/` → 200 `text/html`. Puertos 5432/15432/80 cerrados.
- `POST /api/auth/login` con el administrador inicial (clave leída de Key Vault a variable) → tokens;
  `GET /api/auth/me` → rol Administrador; clave incorrecta → 401.
- Navegador: contexto seguro, manifest 200, service worker registrado, sin errores de consola.
- `systemctl start combustible-backup` → `combustible-20260923T085017Z.dump` (100653 bytes) en Blob.
  `combustible-restore-test` → 33 tablas restauradas en PostgreSQL aislado, filas idénticas.
- `certbot renew --dry-run` → correcto. Timers: backup 07:30 UTC diario, `certbot.timer` activo.

**No verificado / pendiente:** entrega real de correo; SMS (B-01); datos reales (B-04);
Android físico (CA-6); decisión OAuth Supervisor. El hook de renovación convirtió el PFX en la
instalación inicial, pero una renovación real aún no ha ocurrido.
**Commit:** `79d3233`. CI `35840116261` **success**: backend 52/52, navegador 6/6, frontend y
contenedor GHCR. **Requisitos afectados:** ninguno nuevo en código;
cierra la tarea de despliegue de Fase 6 (B-03 resuelto para la demo con dominio `cloudapp.azure.com`).

## 2026-09-23 — Correo real verificado y revisión completa contra el SRS (Claude)

**Agente:** Claude. **Autorización:** el usuario pidió "correo" (envío de prueba a su buzón) y
"revisa bien todo el programa para ver si falta algo".

- **Correo real:** datos DEMO en producción (departamento, empleado con el correo del usuario,
  cédula `00000000001` y móvil ficticios, vehículo, combustible) → solicitud aprobada → ticket
  `COM-2026-000001`. ACS respondió `2.6.0 Queued mail for delivery`; el conector Outlook confirmó
  el mensaje en la **bandeja de entrada** (11:29 UTC) con QR en línea, PDF adjunto, código corto y
  enlace seguro al FQDN de Azure. SMS quedó en bandeja local (B-01), como corresponde.
- **E2E en Azure** (script local en scratchpad, datos DEMO): QR del ticket decodificado con el
  zxing-wasm de la PWA; validar 200, QR alterado 422, despacho 201, reuso 422; existencia 500→490;
  ticket `Consumed`; reportes XLSX/CSV/PDF 200; dashboard 200; cierre diario 201 y acta PDF 200;
  ajuste posterior 422 "El día operativo ya fue cerrado"; `/api/auditoria/verificar` `valid: true`
  (28 eventos). Enlace público sin sesión muestra el ticket y oculta el QR consumido.
  Usuario `despacho@combustible-demo.test` creado para la prueba y **desactivado** al final.
  Un primer intento falló antes de enviar nada: en PowerShell `$h` y `$H` son la misma variable.
- **Hueco cerrado:** no había prueba de edición de empleados/vehículos (RF-02/RF-03). Nueva
  `CatalogTests.Edicion_de_empleado_y_vehiculo_recifra_y_conserva_unicidad`: la cédula editada
  mueve el índice ciego (la nueva choca con 409, la anterior queda libre), correo descifrado,
  vehículo editado y versión obsoleta 412.
- **Matriz:** RF-01..RF-04, RS-01, RS-02 pasan a HECHO con pruebas nombradas; nota RS-06
  actualizada; sección "Revisión completa" con hallazgos abiertos (lectura de PII por todos los
  roles, 24/7 con una VM, Key Vault sin purge protection, OAuth Supervisor).
- Producción: HSTS, CSP de un solo origen, `X-Frame-Options: DENY`, `no-store`; logins auditados.

**Verificado con:** `dotnet test backend -c Release --nologo` → **53/53** (PostgreSQL local, Docker
Desktop arrancado para ello). E2E y correo contra https://intec-fuel-dev-b805.northcentralus.cloudapp.azure.com.
**Commit:** `2511fb3`; CI `35855538217` **success** (backend 53/53, navegador 6/6, frontend, contenedor). **Requisitos afectados:** RF-01..RF-04, RS-01, RS-02 (cierre documental); RF-06/RF-09 correo verificado en producción.
## 2026-09-23 — ADR-016 desplegado: permisos, privacidad y purge protection (Claude)

**Agente:** Claude. **Decisiones del usuario:** "el máximo poder debería ser del administrador";
datos personales "solo adm y supervisor"; purge protection "sí, actívala".

- `Program.cs`: `catalog-write`, `request-approve`, `inventory-write` exigen `sid` (persona); nueva
  política `employee-pii`; `dispatch`/`close` solo Despachador. `CatalogEndpoints`: lectura de
  empleados sin cédula/correo/móvil para otros roles. Frontend: menú Despacho solo Despachador,
  columna "Restringido". Pruebas: despacho con Despachador, matriz RBAC con Administrador,
  OAuth Supervisor sin escritura, nueva `Datos_personales_del_empleado_solo_para_administrador_y_supervisor`.
- Key Vault: `az keyvault update --enable-purge-protection true` → `purgeProtection: true`; Bicep igual.
- Desplegado `465eaa1` en Azure (paquete SHA-256 `abfb7d62…af38`), salud ready. Verificado en producción:
  Administrador ve la cédula del empleado DEMO; usuario Consulta DEMO no (usuario desactivado después).
- Inventario de la suscripción: además de este proyecto hay `rg-intec-db` (SQL `db-intec-demo` Basic,
  USD 0.177/día ≈ 5.38/mes, no se puede pausar; `db-intec-schedule` en oferta gratuita, pausada) y
  `rg-hermes-ai` (Azure OpenAI gpt-5-mini GlobalStandard, cobra por uso). No se tocó ninguno.
- Memoria real en la VM: app 253 MiB, PostgreSQL 35 MiB (base para evaluar la VM gratuita de 1 GiB).
- Cost Management devolvió 429 para esta suscripción; el saldo real se ve en microsoftazuresponsorships.com.

**Verificado con:** `dotnet test backend -c Release` **58/58**; `npm run build` y `npm run lint` correctos;
CI `35862079238` success (58/58, navegador 6/6); instalador en Azure `{"status":"ready"}`.
**Commit:** `465eaa1` (código) + este registro. **Requisitos afectados:** RS-02, RS-03, RF-24.

## 2026-09-23 — Mes de demo: reinicio verificado, vigilancia y presupuesto (Claude, ADR-017)

**Agente:** Claude. **Pedido:** demo arriba ~1 mes (hasta ~2026-10-23), 24/7 "MUY importante";
"continúa con lo que creas que falte".

- Crédito: Cost Management mostró USD 7.30 gastados en 12 meses (SQL 7.29, OpenAI 0.004); quedan
  ≈ USD 92.7 → ≈ 2 meses. Se mantiene la VM actual; no hace falta AWS.
- Revisión en la VM: `docker`/`containerd` habilitados en el arranque, timers de respaldo y certbot
  activos, disco 12 %, 2.9 GiB de RAM disponibles, `unattended-upgrades` sin reinicio automático.
- `az vm restart`: arranque 13:05:51 UTC; `/health` 200 y login 200 sin intervención, `RestartCount=0`.
- Nuevo `infra/monitoreo.bicep` (Log Analytics con tope 0.1 GB/día, Application Insights, prueba
  estándar `wt-intec-fuel-health` a `/health` cada 15 min con certificado ≥ 14 días, grupo de acciones
  `ag-intec-fuel-dev-b805`, alerta `alerta-demo-caida`), `infra/presupuesto.bicep` (suscripción,
  USD 45/mes, avisos 80 %/100 % real y 100 % previsto) y `scripts/azure-monitoreo.ps1` (what-if / `-Deploy`).
  Precios de la API oficial: USD 0.00056 por ejecución, USD 0.10/mes la alerta → ≈ USD 1.7/mes.
- `docs/azure.md`: recursos, paso 6, reinicio, vigilancia, crédito; corregida la revisión de la imagen (`465eaa1`).

**Verificado con:** `az bicep build` de ambos archivos sin errores; what-if: 5 `Create` en el grupo, el resto
`Ignore`, presupuesto `Create`; despliegue correcto; `az rest` del presupuesto (45, Monthly); alerta
`enabled`, `PT15M`; prueba `Enabled`, 900 s; métrica `availabilityResults` 13:10 UTC = 1 ejecución, **100 %**.
**No verificado:** la entrega del correo de alerta (exigiría tumbar la demo); el usuario debe confirmar el
aviso de Azure "agregado al grupo de acciones". El presupuesto mostró USD 0 del mes por retraso de facturación.
**Commit:** ver el siguiente push. **Requisitos afectados:** ninguno del SRS (operación de la demo).