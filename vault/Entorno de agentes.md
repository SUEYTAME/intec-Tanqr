---
tipo: proyecto
estado: activo
actualizado: 2026-09-22
---

# Entorno de agentes

Qué capacidades tienen Claude y Astra en este proyecto, cuáles comparten de verdad y cuáles
no pueden compartir. Verificado en la máquina el 2026-09-22, no citado de memoria.

**Quiénes son.** Claude = Claude Code en la app de escritorio. **Astra = GPT‑6 Astra corriendo
sobre Codex CLI 0.153.4** en esta misma máquina (`gpt-6-astra` aparece en
`~/.codex/config.toml`). Los dos leen el mismo disco, que es lo que hace posible que este vault
funcione como memoria compartida.

---

## Skills — se comparten, verificado

El puente `C:\Dev\claude-codex-bridge` espeja las skills de Claude al lado de Codex. Estado
tras el arreglo de hoy: **1389 fuentes espejadas**.

En Claude se invocan por su nombre. **En Codex/Astra se invocan como `$cb-<nombre>`.**

> Astra tiene desactivada la invocación implícita de las skills espejadas: con más de mil
> descripciones se revienta su presupuesto de contexto. Solo `cb-skill-finder` se activa sola,
> y busca en el índice para cargar la que toque. Si Astra necesita una skill concreta, hay que
> **nombrarla**: `$cb-postgres-patterns`. El catálogo completo está en
> `~/.codex/SKILLS-CATALOG.md` y el índice en `~/.codex/skills-index.csv`.

### Las que este proyecto va a usar

| Para qué | En Claude | En Astra | Fase |
|---|---|---|---|
| Patrones de .NET / C# | `ecc:dotnet-patterns` | `$cb-dotnet-patterns` | 1-3 |
| Patrones de PostgreSQL | `ecc:postgres-patterns` | `$cb-postgres-patterns` | 1-3 |
| Migraciones de base de datos | `ecc:database-migrations` | `$cb-database-migrations` | 1 |
| Diseño de la API REST (RF-24) | `ecc:api-design`, `ecc:contract-first` | `$cb-api-design`, `$cb-contract-first` | 1-2 |
| Patrones de React | `ecc:react-patterns` | `$cb-react-patterns` | 4-5 |
| Pruebas de React | `ecc:react-testing` | `$cb-react-testing` | 4-5 |
| Ciclo TDD | `ecc:tdd-workflow` | `$cb-tdd-workflow` | todas |
| Pruebas de extremo a extremo | `ecc:e2e-testing` | `$cb-e2e-testing` | 4 |
| Contenedores | `ecc:docker-patterns` | `$cb-docker-patterns` | 0 |
| Revisión de seguridad (CA-7) | `ecc:security-review` | `$cb-security-review` | 6 |
| Registrar ADRs | `ecc:architecture-decision-records` | `$cb-architecture-decision-records` | todas |
| No dar por hecho lo no verificado | `ecc:verification-loop` | `$cb-verification-loop` | todas |
| Accesibilidad del frontend | `ecc:frontend-a11y` | `$cb-frontend-a11y` | 4-5 |
| Exportar a Excel (RF-20) | `anthropic-skills:xlsx` | `$cb-xlsx` | 5 |
| Exportar a PDF (RF-06, RF-18, RF-20) | `anthropic-skills:pdf` | `$cb-pdf` | 2, 3, 5 |
| Documentos Word de entrega | `anthropic-skills:docx` | `$cb-docx` | 6 |
| Formato académico de INTEC | `anthropic-skills:intec-formato-academico` | `$cb-intec-formato-academico` | 6 |
| Gráficos del dashboard (RF-22) | `dataviz` | — solo Claude | 5 |

### Agentes revisores — también espejados

Claude los lanza como subagentes; Astra los invoca como skill.

| Revisa | En Claude | En Astra |
|---|---|---|
| Código C# | `ecc:csharp-reviewer` | `$cb-csharp-reviewer` |
| Consultas y esquema de base de datos | `ecc:database-reviewer` | `$cb-database-reviewer` |
| Vulnerabilidades | `ecc:security-reviewer` | `$cb-security-reviewer` |
| React / TypeScript | `ecc:react-reviewer`, `ecc:typescript-reviewer` | `$cb-react-reviewer`, `$cb-typescript-reviewer` |
| Errores tragados en silencio | `ecc:silent-failure-hunter` | `$cb-silent-failure-hunter` |
| Errores de compilación | `ecc:build-error-resolver` | `$cb-build-error-resolver` |

El `silent-failure-hunter` importa especialmente aquí: el ADR-005 prohíbe que un envío fallido
se reporte como exitoso, y este agente busca justamente eso.

---

## MCP — aquí la paridad NO es completa, y conviene saberlo

Esto contradice una suposición cómoda. Los MCP **no** se comparten como las skills.

### Lo que se comprobó el 2026-09-22

`~/.claude.json` tiene **cero** servidores MCP definidos: ni globales (`mcpServers`) ni en
ninguno de los 32 proyectos. El puente reporta `mcp=0` en cada corrida, y ese número es
**correcto**, no un síntoma de fallo: no hay nada portable que espejar.

Los MCP que Claude tiene en sesión vienen de sitios que el puente no puede tocar:

| Origen | Ejemplos | ¿Cruza a Astra? |
|---|---|---|
| Runtime de la app de escritorio | navegador integrado, computer-use, paneles de sesión, terminal | **No.** Los provee el propio host. Codex trae sus equivalentes |
| Conectores de la cuenta Claude | Figma, Lucid, Microsoft 365, Zoho, Claude Docs | **No.** Van atados a la cuenta Claude, no a un archivo de configuración |
| Plugins instalados | `azure`, `chrome-devtools` | **No** como MCP. Las *skills* dentro del plugin sí cruzan; el servidor MCP no |

### Qué significa en la práctica

**No se puede prometer que Astra tenga los mismos MCP que Claude.** Lo honesto es repartir el
trabajo según quién tiene la herramienta:

| Tarea del proyecto | La hace | Con qué |
|---|---|---|
| Auditoría Lighthouse y depuración de la PWA (CA-6) | Claude | MCP `chrome-devtools` |
| Probar la app web contra el servidor de desarrollo | Claude | navegador integrado |
| Desplegar o consultar recursos en Azure | Claude | MCP `azure` |
| Diagramas ER y de secuencia para la documentación | Claude | MCP `lucid` |
| Todo el trabajo de código, pruebas y ficheros | **Ambos** | sistema de archivos + skills espejadas |

Astra no queda cojo para construir: lo que le falta son herramientas de *inspección de
navegador y de nube*, no de desarrollo. El reparto está en [[Como trabajamos]].

### Bug latente encontrado, sin impacto hoy

`~/.claude.json` tiene 4 pares de claves de proyecto que solo difieren en mayúsculas:

```
'c:/Users/proje/AI-Skills-Vault'   y  'C:/Users/proje/AI-Skills-Vault'
'C:/WINDOWS/system32'              y  'C:/Windows/System32'
'c:/Dev/open source trading plat/daily_stock_analysis'  y su variante con 'C:'
'C:/Users/proje/Downloads/JobSearchgit/career-ops'      y su variante con 'c:'
```

`ConvertFrom-Json` de PowerShell falla con claves duplicadas ignorando mayúsculas, así que el
paso de MCP del puente aborta y registra un `WARN` en cada corrida. **Hoy no cambia nada**
porque no hay MCP portables que espejar. Pero el día que se añada uno, no cruzará y el `WARN`
se seguirá viendo como ruido de fondo. Queda anotado; no se arregló porque tocar `.claude.json`
a máquina es arriesgado y no bloquea este proyecto.

---

## Lo que se arregló hoy

**Las skills sincronizadas desde la cuenta Claude no llegaban a Astra.** `Enumerate-Sources`
en `sync.ps1` escaneaba `~/.claude/skills` solo al primer nivel, exigiendo un `SKILL.md`
dentro de cada carpeta. Las skills de cuenta viven en
`~/.claude/skills/synced/<orgId>/<userId>/<skill>/SKILL.md`, tres niveles más abajo, así que
la carpeta `synced` se descartaba entera por no tener `SKILL.md` propio. Resultado: 10 skills
invisibles para Astra, entre ellas `xlsx`, `pdf`, `pptx` y `docx` — las que este proyecto
necesita para RF-20.

**Arreglo:** bloque aditivo en `sync.ps1` que también recorre `skills\synced` con
`-Recurse -Depth 2`. Copia de seguridad previa en
`sync.ps1.bak-pre-synced-20260922-123317`.

**Verificado:** `-DryRun` dio `enumerated 1389` (antes 1379) con `created=10`, `forward=0`,
`backward=0`, `conflict=0`, `skipped=1379` — es decir, nada de lo que ya funcionaba se tocó.
La corrida real creó las 10 y se comprobó carpeta por carpeta en `~/.codex/skills/`.

---

## Cómo comprobar que el entorno sigue sano

```
powershell -File C:\Dev\claude-codex-bridge\sync.ps1 -DryRun
```

Qué esperar: `enumerated 1389` o más, `conflict=0`. Si aparece `conflict>0`, los dos agentes
editaron la misma skill y hay una copia de seguridad en `backups\<timestamp>\`: míralo antes
de seguir.

**Las eliminaciones nunca se aplican solas**, en ninguno de los dos lados. Si borras una skill
en Claude, el puente lo reporta pero no la borra en Codex. Es deliberado.
