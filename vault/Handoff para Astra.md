# Handoff para Astra (actualizado 2026-09-23, relevo de Claude)

## RELEVO PARA CODEX — LEER PRIMERO (Claude, 2026-09-23)

**Estado: producto desplegado y verificado en Azure.** URL https://intec-fuel-dev-b805.northcentralus.cloudapp.azure.com
Rama `fase-2-producto`; código desplegado = `465eaa1` (CI `35862079238` verde: backend 58/58, navegador 6/6).
Todo lo operativo está en `docs/azure.md`; decisiones en ADR-014/015/016; evidencia en la bitácora.

**Acceso:** usuario `admin@combustible-demo.test`; contraseña SOLO en Key Vault
(`az keyvault secret show --subscription 44f41884-c42a-4162-898f-d83d8d987ff3 --vault-name kv-intec-fuel-dev-b805 -n bootstrap-password --query value -o tsv`).
Nunca imprimir secretos en el chat ni subirlos.

**Reglas que no cambian:** solo suscripción `44f41884-c42a-4162-898f-d83d8d987ff3` (pasar siempre `--subscription`;
NO tocar LegatTech-Bot). Si ARM pide MFA con token `amr=pwd`, login con `--claims-challenge` (receta en `docs/azure.md`).
SSH NO sirve (clave con frase de paso desconocida; no regenerar): administrar con `az vm run-command`.
Actualizar la VM: `git archive` del commit con CI verde → Blob `deployments/src-<7>.tar.gz` → cambiar `REVISION` y
`SRC_SHA256` en `deploy/azure/instalar.sh` → `az vm run-command invoke ... --scripts @deploy/azure/instalar.sh`.
Pruebas locales: `dotnet test backend -c Release` (necesita Docker Desktop encendido).

**Decisiones del usuario ya tomadas (2026-09-23):** máximo poder al Administrador; despacho/cierre solo Despachador;
OAuth sin escritura; PII (cédula/correo/móvil) solo Administrador y Supervisor; purge protection activada (irreversible);
correo real de prueba autorizado y entregado (ticket DEMO COM-2026-000001).

**Necesidad del usuario: el proyecto solo debe estar arriba ~1 mes (hasta ~2026-10-23).**
Crédito: Cost Management (2026-09-23) muestra USD 7.30 gastados en 12 meses (SQL rg-intec-db 7.29, OpenAI 0.004);
este proyecto aún no aparecía por retraso de facturación. Quedan ≈ USD 92.7. Gasto previsto ≈ USD 41.7/mes
(proyecto ~36.3 + SQL Basic `db-intec-demo` 5.38) → dura ≈ 2.2 meses. **No hace falta AWS ni cambiar de VM.**
Saldo oficial: https://www.microsoftazuresponsorships.com/balance (solo el usuario). Si el saldo real fuera menor
a ~USD 45, pasar a VM gratuita `Standard_B2ats_v2` + Premium SSD P6 64 GiB (app 253 MiB + PG 35 MiB medidos;
añadir swap 2 GiB y construir la imagen fuera de la VM).
Al terminar el mes: el usuario decide borrar `rg-intec-fuel-dev-b805` (el agente no borra sin orden explícita;
Key Vault queda en borrado suave 7 días por purge protection). Correo ACS: secreto de app Entra vence 2027-09-23.

**Vigilancia del mes (ADR-017, 2026-09-23):** reinicio de la VM verificado (vuelve sola). Prueba de
Application Insights a `/health` cada 15 min (+ certificado) → alerta `alerta-demo-caida` → correo del usuario.
Presupuesto de suscripción `presupuesto-credito-estudiante` USD 45/mes con avisos. Ver disponibilidad:
`az monitor metrics list --resource <id de appi-intec-fuel-dev-b805> --metric availabilityResults/availabilityPercentage --offset 24h`.
Falta que el usuario confirme que le llegó el correo de Azure del grupo de acciones.

### Prompt vigente para Codex (lo mantiene al día el agente que trabaja)
El usuario pega esto en Codex si Claude deja de responder:

> Continúa el proyecto en C:\Dev\intec-combustible (rama fase-2-producto). No reconstruyas el chat.
> Lee: AGENTS.md → vault/Handoff para Astra.md (sección "RELEVO PARA CODEX", completa) →
> vault/Estado actual del proyecto.md → vault/Tareas pendientes.md → docs/azure.md.
> La demo está en Azure y debe seguir arriba hasta ~2026-10-23. Solo la suscripción
> 44f41884-c42a-4162-898f-d83d8d987ff3 con --subscription explícito; nunca LegatTech-Bot.
> Si Azure pide MFA, dame el código de dispositivo (receta --claims-challenge en docs/azure.md).
> No imprimas secretos, no regeneres la clave SSH (usa az vm run-command con --scripts @archivo),
> no borres recursos sin que yo lo pida, no toques db-intec-demo, el OpenAI ni vault/.obsidian/.
> Primero verifica el estado real (/health, disponibilidad en Application Insights, último respaldo
> en Blob) y dime qué encontraste; luego sigue con la primera tarea desbloqueada de Tareas pendientes.
> Al terminar: bitácora, estado, tareas, este relevo (incluido este prompt), dotnet test backend -c Release,
> npm run build, commit, push y CI.

**Pendiente (no simular):** SMS real (B-01), datos reales de INTEC (B-04), prueba en Android físico (CA-6) contra la URL.
Datos DEMO en producción: catálogos `DEMO*`, ticket consumido, cierre `DEMO-EST` 2026-09-23, usuarios DEMO desactivados.
No tocar `vault/.obsidian/`. `db-intec-demo` y el OpenAI son del usuario: no tocarlos.

Rama: `fase-2-producto` (creada desde `fase-1-dominio` @4f7635a), **subida a remoto el 2026-09-22**.

## Continuación de Astra — publicación

El usuario autorizó subir todo a GitHub y continuar la publicación. Código de Claude `09d740d`
subido; `4c105d7` añade GHCR condicionado a los tres jobs de CI. Ejecución
`35814915846` **correcta**: backend52/52, navegador6/6, frontend y contenedor.
`345e623` prepara IaC Azure y verifica privacidad del paquete. CI `35818197956` **correcta**:
backend 52/52, navegador 6/6, frontend y contenedor; API confirmó `visibility: private`.
Imagen: `ghcr.io/sueytame/intec-combustible:sha-345e6231e2fc1d69ec2c99a0fa5300471793da62`.
Digest: `sha256:5e9e7f1d61b1b73407a52234cd9761491161ade095fdb265be748f31f4b6af64`.
**Nueva instrucción:** crear todo lo necesario en Azure, exclusivamente en cuenta INTEC.
Cuenta1128305@est.intec.edu.do; suscripción44f41884-c42a-4162-898f-d83d8d987ff3;
tenant6856181f-daf8-4725-ac51-dd9f7dfe2f2b. Pasar siempre --subscription: la CLI también
tiene otra cuenta. Cuotas verificadas B2als_v2/4GiB northcentralus;~USD36.29/mes.
Leer `docs/azure.md`, `infra/`, `scripts/azure-infra.ps1`. Bicep sin avisos y conformance correctos.
**Bloqueo exacto:** Azure rechazó what-if por falta de MFA. No se creó el grupo/VM; group exists=false.
Se inició `az login --tenant ... --use-device-code`; el usuario debe completar Microsoft/MFA.
Si la sesión expiró, iniciar otra; no intentar esquivar MFA. La autorización de despliegue ya existe.
OAuth sigue pendiente de decisión: no se cambiaron permisos ni se creó cliente externo.
INTEC es el contexto del repositorio/SRS; sus recursos reales no son un requisito para una demo.

Continuidad de Azure (ignorada por Git): `.copilot-azure/sessions/b805f7aa-7186-4692-98e7-df81256974cc/`.
Clave SSH en `artifacts/azure/id_ed25519`; no regenerarla ni subirla. Subagente IaC agotó cuota;
main generó/revisó IaC localmente. No se atribuye una revisión independiente inexistente.
Runtime pendiente: certificados ACME/renovación, secretos KeyVault, Docker app/PG, correo,
backupBlob+restauración y smoke login/TLS. ACS SMTP investigado, sin recursos Entra/correo creados.

## Azure — DESPLEGADO Y VERIFICADO (Claude, 2026-09-23)
URL https://intec-fuel-dev-b805.northcentralus.cloudapp.azure.com. Usuario `admin@combustible-demo.test`,
contraseña solo en Key Vault `kv-intec-fuel-dev-b805` → `bootstrap-password`. Todo en `docs/azure.md`
y ADR-015. SSH NO sirve (clave con frase de paso desconocida): administrar con `az vm run-command`.
Si ARM rechaza por MFA con token `amr=pwd`: login con `--claims-challenge` (receta en `docs/azure.md`).
Pendiente: envío real de correo (con permiso), SSH, Data Protection persistente, OAuth Supervisor, B-01/B-04/CA-6.

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
1. Completar MFA y desplegar Azure según `docs/azure.md`; verificar CI del último commit.
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
