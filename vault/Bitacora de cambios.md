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
