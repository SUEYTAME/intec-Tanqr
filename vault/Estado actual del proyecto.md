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

Rama activa: `fase-0-entorno`. Último commit: `907f922`.

- Estructura del repositorio en `C:\Dev\intec-combustible`, con git inicializado.
- Vault de Obsidian dedicado en `vault/`, que es esta carpeta.
- Contrato de agentes: `AGENTS.md` canónico + punteros `CLAUDE.md` y `CODEX.md`.
- **Backend**: solución de 5 proyectos en .NET 10 con dependencias en una sola dirección
  (Api → Infrastructure → Application → Domain). `dotnet test` da **2/2 pasando**.
- **Frontend**: React + TypeScript + Vite. `npm run build` compila.
- **Puente con Astra**: 1389 skills espejadas, incluidas las 10 de cuenta que antes faltaban.

## Qué NO está montado

- **La base de datos nunca se ha levantado.** `docker-compose.yml` está validado
  sintácticamente pero no ejecutado — ver bloqueo B-06.
- CI, migraciones, y los 30 requisitos del SRS. Detalle en [[Tareas pendientes]].

## Entorno de la máquina — verificado el 2026-09-22

| Herramienta | Estado |
|---|---|
| .NET SDK | 9.0.315 y **10.0.401 LTS**, ambos instalados. `backend/global.json` fija el 10 |
| Node.js | v24.15.0 |
| npm | 11.12.1 |
| Docker | CLI 29.6.1 instalado, **daemon caído** — bloqueo B-06 |
| Git | 2.53.0.windows.3 |
| Python | 3.12.2 |
| Flutter | **No instalado.** Decisión: no hace falta, vamos con PWA (ADR-004) |
| psql (cliente) | **No instalado.** PostgreSQL va en contenedor Docker |

## Bloqueos abiertos

Cosas que **no** podemos resolver escribiendo código y que necesitan que una persona actúe.
Ninguna bloquea el desarrollo hoy; bloquean la puesta en producción de su requisito.

| # | Bloqueo | Requisito afectado | Quién lo desbloquea |
|---|---|---|---|
| B-01 | No hay cuenta de pasarela SMS contratada | RF-09 (envío de tickets por SMS) | El usuario. Un agente no puede crear cuentas ni meter datos de pago |
| B-02 | No hay credenciales SMTP para correo saliente | RF-06, RF-09, RF-23 | El usuario, o TI de INTEC |
| B-03 | No hay dominio ni certificado TLS para producción | RS-03 (TLS 1.3) | El usuario / TI de INTEC |
| B-04 | No hay datos reales de INTEC: departamentos, vehículos, empleados, tanques | RF-02, RF-03, RF-04, RF-14 | El usuario. Mientras tanto se trabaja con datos sembrados ficticios |
| B-05 | Sin definir: ¿cuántas estaciones y tanques físicos hay? El SRS asume "tanque" pero no dice cuántos | RF-14, RF-16 | El usuario debe confirmarlo antes de cerrar el modelo de inventario |
| B-06 | **El daemon de Docker no arranca.** Se lanzó `Docker Desktop.exe` y tras 180 s no quedó ningún proceso vivo; probablemente pide aceptar términos o elevación de forma interactiva | Toda la Fase 1 en adelante: sin base de datos no hay migraciones | El usuario, abriendo Docker Desktop a mano una vez |

**Regla:** mientras un bloqueo esté abierto, el código del requisito se escribe contra una
interfaz con implementación de desarrollo (por ejemplo, un `IEmailSender` que escribe a disco
en vez de enviar). Así el bloqueo no detiene el desarrollo y el día que llegue la credencial
solo se cambia el registro de dependencias. Nunca se simula el éxito de un envío real.

## Qué abrir según la tarea

| Si vas a... | Abre |
|---|---|
| Retomar el trabajo sin más contexto | [[Tareas pendientes]] |
| Implementar un requisito | [[Trazabilidad de requisitos del SRS]] + `docs/SRS.pdf` |
| Proponer o cambiar arquitectura | [[Decisiones de arquitectura]] — **incluidas las secciones DESCARTADO** |
| Saber qué hizo el otro agente | [[Bitacora de cambios]] |
| Necesitar una capacidad (MCP, skill) que no tienes | [[Entorno de agentes]] |
| Entender las reglas de colaboración | [[Como trabajamos]] |
