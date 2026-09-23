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
