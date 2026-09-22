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
