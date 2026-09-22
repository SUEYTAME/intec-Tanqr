---
tipo: proyecto
estado: activo
actualizado: 2026-09-22
---

# Estado actual del proyecto

Fuente de continuidad. Leer después AGENTS.md y [[Tareas pendientes]]; decisiones en
[[Decisiones de arquitectura]], evidencia exacta en [[Bitacora de cambios]].

## Fase actual

**Fase 1 — Dominio y autenticación, en curso.** Rama `fase-1-dominio`.
El usuario delegó el 2026-09-22 generar configuración, resolver pendientes y continuar.
H-01..H-07 tienen decisión explícita en ADR-007; ADR-008 define la primera entrega.
No volver a pedir esas mismas autorizaciones. No inventar datos reales de INTEC.

## Acceso sencillo

- Doble clic en `Iniciar.cmd`, o `./scripts/iniciar.ps1` (PowerShell 7).
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

## Lo que falta / límites

- **No producción**: OAuth 2.0/OIDC todavía pendiente; JWT local no lo sustituye (RS-05 parcial).
- MFA probado vía API e interfaz: alta, recuperación de un solo uso, regeneración y baja.
  Navegador Chromium escritorio y Pixel 7 emulado; no equivale a Android físico (CA-6).
- Auditoría no resiste a un superusuario que reconstruya toda la cadena. Revisión de
  integridad `/api/auditoria/verificar`; destino externo/anclaje aún no implementado.
- RS-03: AES-256 de datos sensibles y TLS de producción pendientes.
- Tickets, QR, inventario, despacho, cierres, reportes, notificaciones y PWA aún no implementados.
- B-01 SMS, B-02 SMTP, B-03 dominio/TLS y B-04 datos reales siguen abiertos para producción.
  B-05 cantidad física de tanques sigue sin datos reales, pero el modelo admite N tanques.
- No se tocó `vault/.obsidian/.obsidian/` preexistente sin rastrear.

## Siguiente bloque

1. Revisar Git y bitácora; hay checkpoints de código y commits de continuidad separados.
2. Resolver cualquier fallo real de la CI de Fase 1 antes de seguir.
3. Completar seguridad y pruebas pendientes de Fase 1 según [[Tareas pendientes]].
4. No marcar OAuth, cifrado, QR ni inventario como hechos por tener pantallas o interfaces.

## Qué abrir según la tarea

| Tarea | Archivos |
|---|---|
| Continuar | [[Tareas pendientes]] + [[Bitacora de cambios]] |
| Requisito | [[Trazabilidad de requisitos del SRS]] + `docs/SRS.pdf` |
| Arquitectura | [[Decisiones de arquitectura]], incluidos DESCARTADO y ADR-007/008 |
| Backend | `backend/src/Combustible.Api/Program.cs`, `Endpoints/`, `Infrastructure/Data/`, `tests/` |
| Arranque | `scripts/iniciar.ps1`, `scripts/preparar.ps1`, `scripts/config-local.ps1` |
| Frontend | `frontend/src/App.tsx`, `Administration.tsx`, `api.ts`, `frontend/e2e/` |
| Colaboración | [[Como trabajamos]]; comprobar archivos estables antes de escribir |
