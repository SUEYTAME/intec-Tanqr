---
tipo: proyecto
estado: activo
actualizado: 2026-09-22
---

# Estado actual del proyecto

Fuente de continuidad. Leer después AGENTS.md y [[Tareas pendientes]]; decisiones en
[[Decisiones de arquitectura]], evidencia exacta en [[Bitacora de cambios]].

## Fase actual

**Producto funcionalmente completo en rama `fase-2-producto` (último commit `d0ac9c7`, 2026-09-22).**
Fases 1-5 con código y pruebas; Fase 6 a medias (despliegue, carga, manual, informe).
Continuidad rápida: [[Handoff para Astra]]. No inventar datos reales de INTEC.

## Acceso sencillo

- Doble clic en `Iniciar.cmd`, o `powershell -File scripts/iniciar.ps1` (Windows PowerShell 5.1; no hay pwsh).
- Web: http://localhost:5173. API: http://127.0.0.1:5080.
- Usuario y contraseña inicial: `artifacts/acceso-local.txt`, ignorado por Git.
- Secretos generados en `.env`, ignorado; nunca copiar sus valores al vault ni al chat.
- `./scripts/iniciar.ps1 -Reiniciar` actualiza; `./scripts/detener.ps1` detiene solo sus servidores.
- PostgreSQL: `127.0.0.1:15432`. El 5432 estaba ocupado por PID 7424; no se tocó.

## Estado verificado

- Docker servidor 29.6.1 operativo. PostgreSQL **17.11 healthy**, `pg_isready` y consulta
  autenticada por TCP correctos. B-06 y B-07 resueltos. Volumen local persistente.
- Repositorio **privado**: https://github.com/SUEYTAME/intec-combustible.
  Fase 0 CI correcta: https://github.com/SUEYTAME/intec-combustible/actions/runs/35759842395.
- Backend .NET 10.0.401 / runtime 10.0.12, EF Core + Identity + Npgsql 10.
  Migración inicial aplicada a PostgreSQL. Usuario `combustible_app` separado del propietario;
  no puede UPDATE/DELETE/TRUNCATE auditoría ni acceder al historial de migraciones.
- Login JWT, refresh rotativo, sesiones revocables, MFA TOTP y recuperación de un solo uso.
  Usuarios/roles, departamentos, empleados y vehículos con bajas lógicas y concurrencia.
  Auditoría SHA-256 encadenada en la misma transacción que la escritura.
- React: login, catálogos editables, administración de usuarios, MFA y consulta de auditoría.
  Tokens solo en memoria. No hay datos reales precargados. Las pruebas UI dejan registros
  `QA-` / `Prueba UI` inactivos, expresamente ficticios.
- Build Release backend sin avisos/errores; tests PostgreSQL real y navegador registrados
  en la bitácora. Build/lint frontend correctos. Auditoría NuGet sin vulnerabilidades reportadas.
- CI ampliada de Fase 1 correcta para `70972c7`: run `35764488194`, backend, frontend y
  navegador con base efímera. Commit posterior `08a91ef`: 24/24 pruebas backend y 4/4 de
  navegador locales; build Release y frontend/lint correctos. Consultar CI de cada commit.

## Estado verificado de Fase 2-6 (2026-09-22)

- Backend 51/51 en PostgreSQL real (Release). Playwright 6/6 escritorio + Pixel 7 emulado.
- Tickets numerados sin huecos, QR firmado ECDSA, entrega por correo (Mailpit local), despacho
  atómico, inventario, cierre diario, reportes CSV/XLSX/PDF, dashboard, alertas, programaciones.
- RS-03 cifrado de datos personales + TLS 1.3; RS-05 OAuth client credentials; RS-06 ancla firmada.
- PWA instalable; la API nunca se guarda en caché. Base local migrada y empleados cifrados.

## Lo que falta / límites

- Rama `fase-2-producto` **sin push ni CI remota** todavía.
- Despliegue probado con imagen Docker (`docs/despliegue.md`); carga, manual e informe hechos.
- Decisión abierta: permisos de escritura de clientes OAuth con rol Supervisor.
- B-01 SMS, B-02 SMTP institucional, B-03 dominio/certificado, B-04 datos reales: bloquean
  producción, no el desarrollo. CA-6 requiere un Android físico.
- No se tocó `vault/.obsidian/` preexistente sin rastrear.

## Qué abrir según la tarea

| Tarea | Archivos |
|---|---|
| Continuar | [[Tareas pendientes]] + [[Bitacora de cambios]] |
| Requisito | [[Trazabilidad de requisitos del SRS]] + `docs/SRS.pdf` |
| Arquitectura | [[Decisiones de arquitectura]], incluidos DESCARTADO y ADR-007/008 |
| Backend | `backend/src/Combustible.Api/Program.cs`, `Endpoints/`, `Infrastructure/Data/`, `tests/` |
| Arranque | `scripts/iniciar.ps1`, `scripts/preparar.ps1`, `scripts/config-local.ps1` |
| Frontend | `frontend/src/App.tsx` (menú/rutas), pantallas `*.tsx`, `lib.ts`, `components.tsx`, `screens.css`, `frontend/e2e/` |
| Colaboración | [[Como trabajamos]]; comprobar archivos estables antes de escribir |
