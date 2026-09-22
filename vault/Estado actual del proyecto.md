---
tipo: proyecto
estado: activo
actualizado: 2026-09-22
---

# Estado actual del proyecto

**Empieza aquí.** Esta nota dice qué es cierto hoy. Si algo contradice a otra nota, esta gana
en lo que toca al estado; las [[Decisiones de arquitectura]] ganan en lo que toca al porqué.

## Qué es el proyecto

Plataforma web + PWA de despacho para el control del inventario y despacho de combustible de
INTEC mediante tickets digitales con QR firmado criptográficamente y trazabilidad completa.

El documento fuente es `docs/SRS.pdf` (SRS Ticket Digitales v1.0, fechado agosto 2026).
Define 24 requisitos funcionales y 6 de seguridad. La trazabilidad completa de qué está hecho
está en [[Trazabilidad de requisitos del SRS]].

## Fase actual

**Fase 0 — Montaje del entorno.** En curso desde 2026-09-22.

Ninguna línea de código de producto escrita todavía. El backlog vive en [[Tareas pendientes]].

## Qué está montado y verificado

Rama activa: `fase-0-entorno`. Base recibida: `0408d59` (posterior al montaje `907f922`).
Commits de implementación de este bloque: `a6a3e08` (entorno) y `6304270` (CI).
La actualización de continuidad queda en un commit posterior; consultar `git log -1`.

- Estructura del repositorio en `C:\Dev\intec-combustible`, con git inicializado.
- Vault de Obsidian dedicado en `vault/`, que es esta carpeta.
- Contrato de agentes: `AGENTS.md` canónico + punteros `CLAUDE.md` y `CODEX.md`.
- **Backend**: solución de 5 proyectos en .NET 10 con dependencias en una sola dirección
  (Api → Infrastructure → Application → Domain). `dotnet test` da **2/2 pasando**.
- **Frontend**: React + TypeScript + Vite. `npm run build` compila.
- **Reverificación 2026-09-22 por Astra**: `dotnet build` sin avisos ni errores,
  `dotnet test` 2/2; también build y pruebas en Release. Frontend compilado tanto en
  `frontend/` como desde `npm ci` en copia aislada bajo `artifacts/`.
- **Docker operativo**: `docker info` devuelve servidor 29.6.1. B-06 resuelto.
- **CI preparada** en `.github/workflows/ci.yml`; `actionlint` 1.7.12 sin errores.
  Comandos ejecutados localmente en Windows, **no ejecutada todavía en GitHub/Linux**.
- **Puente con Astra**: 1389 skills espejadas, incluidas las 10 de cuenta que antes faltaban.

## Qué NO está montado

- **PostgreSQL no se ha levantado.** No existe `.env`; faltan `POSTGRES_USER`,
  `POSTGRES_PASSWORD` y `POSTGRES_DB` también en el entorno del proceso. Compose aborta
  correctamente al interpolar. `POSTGRES_PORT` es opcional (5432). Ver B-07.
- **CI remota sin verificar**: `git remote -v` no devuelve remotos. Ver B-08.
- Migraciones, persistencia de la API y los 30 requisitos del SRS siguen pendientes.
  `/health` solo prueba la API en memoria; **no comprueba PostgreSQL**.
- H-01..H-07 y ADR-006 siguen sin respuesta aprobada. No iniciar Fase 1 aún.

## Entorno de la máquina — verificado el 2026-09-22

| Herramienta | Estado |
|---|---|
| .NET SDK | 9.0.315 y **10.0.401 LTS**, ambos instalados. `backend/global.json` fija el 10 |
| Node.js | v24.15.0 |
| npm | 11.12.1 |
| Docker | Servidor **29.6.1 activo**, Compose v5.3.0; PostgreSQL pendiente de variables |
| Git | 2.53.0.windows.3 |
| Python | 3.12.2 |
| Flutter | **No instalado.** Decisión: no hace falta, vamos con PWA (ADR-004) |
| psql (cliente) | **No instalado.** PostgreSQL va en contenedor Docker |

## Bloqueos abiertos

Cosas que **no** podemos resolver escribiendo código y que necesitan que una persona actúe.
Algunas bloquean producción; B-07/B-08 y las decisiones pendientes impiden cerrar Fase 0.

| # | Bloqueo | Requisito afectado | Quién lo desbloquea |
|---|---|---|---|
| B-01 | No hay cuenta de pasarela SMS contratada | RF-09 (envío de tickets por SMS) | El usuario. Un agente no puede crear cuentas ni meter datos de pago |
| B-02 | No hay credenciales SMTP para correo saliente | RF-06, RF-09, RF-23 | El usuario, o TI de INTEC |
| B-03 | No hay dominio ni certificado TLS para producción | RS-03 (TLS 1.3) | El usuario / TI de INTEC |
| B-04 | No hay datos reales de INTEC: departamentos, vehículos, empleados, tanques | RF-02, RF-03, RF-04, RF-14 | El usuario. Mientras tanto se trabaja con datos sembrados ficticios |
| B-05 | Sin definir: ¿cuántas estaciones y tanques físicos hay? El SRS asume "tanque" pero no dice cuántos | RF-14, RF-16 | El usuario debe confirmarlo antes de cerrar el modelo de inventario |
| B-06 | **RESUELTO 2026-09-22**: `docker info --format '{{json .ServerVersion}}'` devuelve `29.6.1` con salida 0 | Entorno | Docker Desktop abierto por el usuario; verificación de Astra |
| B-07 | No existe `.env` ni variables de PostgreSQL en el proceso. No se ejecutó `docker compose up -d` | Cierre Fase 0 y persistencia | Usuario: definir `POSTGRES_USER`, `POSTGRES_PASSWORD`, `POSTGRES_DB` localmente; no publicar la contraseña |
| B-08 | No hay remoto Git configurado: workflow escrito y validado localmente, sin ejecución en GitHub | CI de Fase 0 | Usuario: indicar el repositorio destino y autorizar su publicación |

**Regla:** mientras un bloqueo esté abierto, el código del requisito se escribe contra una
interfaz con implementación de desarrollo (por ejemplo, un `IEmailSender` que escribe a disco
en vez de enviar). Así el bloqueo no detiene el desarrollo y el día que llegue la credencial
solo se cambia el registro de dependencias. Nunca se simula el éxito de un envío real.
Esta regla no permite inventar credenciales, sustituir PostgreSQL ni decidir H-01..H-07.

## Relevo y siguiente bloque — 2026-09-22

- El cambio local recibido en `docker-compose.yml` se inspeccionó y conservó sin editar:
  puertos en localhost, variables obligatorias y Adminer en perfil `herramientas`.
  Está recogido en `a6a3e08` junto a la eliminación de la contraseña de ejemplo.
- `vault/.obsidian/.obsidian/` ya existía sin rastrear: no se editó ni se comiteó.
  Había procesos Claude abiertos; los archivos de trabajo permanecieron estables entre
  comprobaciones. Eso no prueba que otros procesos estén inactivos: comprobar de nuevo al retomar.
- El PDF `docs/SRS.pdf` coincide por SHA-256 con el original de Descargas.
- Se preguntó al usuario por `.env`, H-06 (política concreta de contraseñas) y ADR-006
  (hash + permisos o además destino externo). **Sin respuesta registrada en este bloque**.
  H-01..H-05/H-07 permanecen pendientes; preguntas exactas en la matriz de trazabilidad.
- Al continuar: comprobar Git y `.env` sin mostrar valores; si están las tres variables,
  ejecutar `docker compose config --quiet`, `docker compose up -d` y `docker compose ps`.
  Solo dar PostgreSQL por verificado tras salud y consulta autenticada `SELECT 1` por TCP.
  No borrar volúmenes para resolver un fallo de autenticación.
- Después: primera ejecución de CI en el remoto que indique el usuario y cierre explícito
  de H-01..H-07/ADR-006. No fijar dominio, reglas de despacho ni auditoría por suposición.

## Qué abrir según la tarea

| Si vas a... | Abre |
|---|---|
| Retomar el trabajo sin más contexto | [[Tareas pendientes]] |
| Implementar un requisito | [[Trazabilidad de requisitos del SRS]] + `docs/SRS.pdf` |
| Proponer o cambiar arquitectura | [[Decisiones de arquitectura]] — **incluidas las secciones DESCARTADO** |
| Saber qué hizo el otro agente | [[Bitacora de cambios]] |
| Necesitar una capacidad (MCP, skill) que no tienes | [[Entorno de agentes]] |
| Entender las reglas de colaboración | [[Como trabajamos]] |
