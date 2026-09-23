# Handoff para Astra (actualizado 2026-09-22)

Rama: `fase-2-producto` (creada desde `fase-1-dominio` @4f7635a). Nada empujado todavía a remoto.

## Hecho y verificado (backend)
| Commit | Contenido |
|---|---|
| 804ca44 | Dominio/infra: tickets, inventario, despacho, firma QR ECDSA, cifrado de campos, PDF/CSV/XLSX |
| 2db1065 | API: solicitudes, tickets, despacho atómico, cierre diario, reportes, alertas (44/44 tests) |
| bf60ba1 | Scripts compatibles con Windows PowerShell 5.1, Mailpit, claves en CI |
| e9a2750 | GET /api/tickets/{id}/qr.png (Admin/Supervisor, auditado) |
| 2f3013b | Cifrado de empleado (RS-03), TLS 1.3, OAuth 2.0 client credentials (RS-05), ancla de auditoría (RS-06), versión en /acceso — **51/51 tests (Release)** |

Estado final 2026-09-22: backend **52/52**, Playwright 6/6, imagen Docker probada.
Comando de pruebas: `dotnet test backend -c Release` (usar Release: el API en Debug bloquea las DLL).

## Frontend — HECHO (`d0ac9c7`)
Todas las pantallas, menú por rol, PWA, ticket público. `npm run build` + `oxlint` OK; Playwright 6/6.
Stack local reiniciado con el backend nuevo (migraciones aplicadas, empleados cifrados).
Vault al día: Bitácora, ADR-009..013, Estado actual, Tareas pendientes, Trazabilidad.

## Lo que queda (en orden)
1. `git push -u origin fase-2-producto` y verificar CI (`.github/workflows/ci.yml`; e2e de CI usa `scripts/ci-e2e.sh`,
   revisar que `screens.spec.ts` y `mfa.spec.ts` —espera 61 s por el límite de /api/auth— pasen allí).
2. **Decisión del usuario pendiente:** cliente OAuth con rol Supervisor hoy puede aprobar solicitudes, anular tickets y
   escribir inventario (solo despacho/cierre exigen `sid`). Si se restringe: añadir `.RequireClaim("sid")` a las políticas
   `request-approve`, `inventory-write`, `catalog-write` en `Program.cs` + prueba.
3. ~~Despliegue, carga, manual, informe~~ HECHO (`1c05ca7` + docs): ver `docs/despliegue.md`, `docs/informe-final.md`.
4. Con recursos del usuario: B-01..B-04 y CA-6 en Android físico. Repetir carga con volumen real.

## Bloqueos reales (no los resuelve código)
B-01 pasarela SMS, B-02 SMTP institucional, B-03 dominio/certificado, B-04 datos reales de INTEC, CA-6 prueba física en Android.

## Trampas conocidas
- Solo Windows PowerShell 5.1 (no hay pwsh): usar `Invoke-Native` de `scripts/config-local.ps1` para ejecutables nativos.
- EF: proyecciones con constructor no se pueden componer → usar records con `init`.
- Minimal APIs: un tipo con `TryParse` estático se trata como parámetro enlazable.
- No imprimir secretos (.env, artifacts/acceso-local.txt). Solo detener procesos con `scripts/detener.ps1`. No tocar `vault/.obsidian/`.

## Progreso
- 2026-09-22: handoff creado; empezando pantallas faltantes del frontend.
- 2026-09-22: HECHO (sin commit todavía): Reports.tsx, Notifications.tsx, Settings.tsx (Parámetros + Integraciones OAuth), PublicTicket.tsx (ruta `/ticket/<id>.<token>` en main.tsx), App.tsx con menú por rol + banner sin conexión + ancla de auditoría, screens.css (el agente no había escrito CSS para sus pantallas), PWA en vite.config.ts (API NetworkOnly, WASM precacheado), íconos en public/. Arreglado TS2774 en Dispatch.tsx. `npm run build` OK, `oxlint` OK. Siguiente: reiniciar stack y e2e.
