---
tipo: proyecto
estado: activo
actualizado: 2026-09-30
---

# Estado actual del proyecto

## Mensajes al usuario — 2026-09-30

El detalle de entrega de un ticket se muestra en español, sin la respuesta SMTP ni el error HTTP de Twilio. La validación y los avisos de cuenta duplicada también. La auditoría muestra la acción y la entidad en español. Backend **113/113**; frontend build/lint correctos.

Fuente de continuidad. Leer después AGENTS.md y [[Tareas pendientes]]; decisiones en
[[Decisiones de arquitectura]], evidencia exacta en [[Bitacora de cambios]].

## Fase actual

**Producto en rama `fase-2-producto`, subida a GitHub; corrección vigente y desplegada `4db7a30`, Azure IaC `345e623`.**
Fases 1-5 con código y pruebas; Fase 6 con contenedor local, carga, manual e informe.
**Desplegada y verificada en Azure INTEC (actualizada 2026-09-30):**
https://intec-fuel-dev-b805.northcentralus.cloudapp.azure.com — demo con datos ficticios.
Acceso, operación y evidencia en `docs/azure.md`; decisiones ADR-014/015.
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
- Repositorio **privado**: https://github.com/SUEYTAME/intec-Tanqr.
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
- Navegación por rol corregida el 2026-09-28 (`be50048`): Despachador solo ve Tickets,
  Despacho, Cierre diario y Mi seguridad; Supervisor, Auditor y Consulta tienen menús ajustados
  a los actores del SRS. Playwright valida los cinco roles en escritorio y móvil. La revisión
  `e43897b` pasó CI `36730599453` (cuatro jobs) y está desplegada en Azure como
  `intec-combustible:0.4.0-e43897b`; `/health/ready` respondió HTTP 200 el 2026-09-30.
- Build Release backend sin avisos/errores; tests PostgreSQL real y navegador registrados
  en la bitácora. Build/lint frontend correctos. Auditoría NuGet sin vulnerabilidades reportadas.
- CI ampliada de Fase 1 correcta para `70972c7`: run `35764488194`, backend, frontend y
  navegador con base efímera. Commit posterior `08a91ef`: 24/24 pruebas backend y 4/4 de
  navegador locales; build Release y frontend/lint correctos. Consultar CI de cada commit.

## Estado verificado de Fase 2-6 (2026-09-22)

- Backend **52/52**, repetido por Astra en PostgreSQL real (Release, 2026-09-22).
  Frontend build/lint repetidos correctamente. CI independiente `35814915846`: backend52/52,
  navegador6/6, frontend y publicación del contenedor correctos.
- Tickets numerados sin huecos, QR firmado ECDSA, entrega por correo (Mailpit local), despacho
  atómico, inventario, cierre diario, reportes CSV/XLSX/PDF, dashboard, alertas, programaciones.
- RS-03 cifrado de datos personales + TLS 1.3; RS-05 OAuth client credentials; RS-06 ancla firmada.
- PWA instalable; la API nunca se guarda en caché. Base local migrada y empleados cifrados.
- GitHub usa `fase-2-producto` como rama predeterminada; conserva las ramas de Fase 0 y Fase 1.

## Verificación de presentación — 2026-09-30

| Entrega | Estado vigente | Evidencia |
|---|---|---|
| QA-PDF-01: tablas recortadas | CERRADO; publicado en Azure | `f10045c` y `93ce2f3`; regresiones de página/celda y render de PDF descargado |
| QA-PDF-02: QR ausente en PDF | CERRADO; publicado en Azure | `f10045c`; imagen incrustada y QR de COM-2026-000008 decodificado idéntico al PNG |
| Flujo integral y estados entre sesiones | VERIFICADO | `20fb182` y `e43897b`; navegador 10/10, backend 66/66 |
| Versión publicada | `0.4.0-4db7a30` | CI `36745051245` success, plantilla Trial temporal, salud HTTP 200 |

Los diagnósticos anteriores describen el estado de sus fechas; esta sección y
[[Tareas pendientes]] indican qué sigue abierto. Evidencia detallada por commit
en [[Bitacora de cambios]] y por requisito en [[Trazabilidad de requisitos del SRS]].

- **QA previa a presentación (2026-09-30): dos defectos PDF corregidos en código `f10045c`.**
  Las tablas usan dimensiones Letter reales y caben en la página. El QR del ticket
  se incrusta como BMP portable; PDFsharp no aceptaba el PNG monocromo de 1 bit.
  Backend ampliado **66/66**, build/lint correctos, ocho regresiones PDF y render
  visual de reportes de varias páginas. QR firmado sintético decodificado desde
  el PDF renderizado con payload idéntico. Flujo integral de navegador agregado;
  suite completa **10/10** y publicación Azure final `e43897b` verificada. Palabras
  largas ajustadas al ancho interior de celdas. Lista/detalle refrescan al recuperar
  foco/visibilidad y cada 15 s visibles; prueba con dos sesiones. Evidencia y
  resultados: `docs/qa-presentacion-2026-09-30.md`.
  Instalador en GitHub e imagen activa Azure coinciden en e43897b;
  PDF recién descargados sin recorte y QR leído. 18a1e17 es una versión histórica.

- **COM-2026-000009:** no tenía despacho; correo fallaba SMTP 5.1.4 por `.test`.
  Usuario proporcionó dirección real; se corrigió Email por API y se reenvió una vez:
  Email=Sent, ticket=Sent sin consumo. Recepción en bandeja no confirmada.
  Cinco consumidos coinciden con cinco despachos, sin inconsistencias de estado.
  SMS=Outbox explícito, sin gateway; el usuario pidió prompt para resolverlo en
  otro chat: `docs/handoff-sms.md`. Adaptador Twilio implementado el 2026-09-30; ver docs/sms-twilio.md.
- **Portada:** el QR anterior apuntaba al login, no era ticket. Copia en
  `C:\Users\proje\Downloads\portada corregida.pptx` contiene QR firmado real de
  COM-2026-000008, Enviado, uso único, vence 2026-10-03; render/decodificación validados.

## Lo que falta / límites

- Rama `fase-2-producto` **subida**. CI `35818197956` correcta para `345e623`:
  backend 52/52, navegador 6/6, frontend build/lint y publicación. API de GitHub confirmó
  `visibility: private` del contenedor desde el token de Actions.
- El job de GHCR publica la imagen privada tras backend, frontend y navegador correctos.
  La imagen publicada no equivale a una aplicación alojada.
- Despliegue probado con imagen Docker (`docs/despliegue.md`); carga, manual e informe hechos.
- Permisos OAuth, separación de funciones y PII del empleado: resueltos por el usuario (ADR-016, `465eaa1`).
- B-01 SMS, B-02 SMTP institucional, B-03 dominio/certificado, B-04 datos reales: pendientes
  para operación real. Una demo puede usar datos ficticios y correo de prueba, identificados
  como tales; no necesita esperar recursos institucionales.
  CA-6 requiere un Android físico.
- Usuario autorizó **todo lo necesario en su cuenta Azure INTEC**. Cuenta
  `1128305@est.intec.edu.do`, suscripción `44f41884-c42a-4162-898f-d83d8d987ff3` (Azure for Students).
  No usar `LegatTech-Bot`. Plan en `docs/azure.md`: B2als_v2,4GiB,northcentralus,~USD36.29/mes.
- **Azure desplegado 2026-09-23 y actualizado 2026-09-30 a `e43897b`** en `rg-intec-fuel-dev-b805`: VM B2als_v2, Key Vault, Blob,
  ACS Email. Verificado desde internet: TLS 1.3 aceptado con Let's Encrypt, TLS 1.2 rechazado,
  salud 200, login real, PostgreSQL cerrado, respaldo a Blob y restauración con filas idénticas.
- Administración sin SSH (`az vm run-command`): la clave `artifacts/azure/id_ed25519` tiene frase
  de paso desconocida (ADR-015). Usuario inicial `admin@combustible-demo.test`; su contraseña solo
  en Key Vault (`bootstrap-password`).
- Correo ACS **entregado** en el buzón del usuario (autorizado; ticket DEMO `COM-2026-000001`).
- Revisión completa contra el SRS 2026-09-23: flujo extremo a extremo verificado en Azure; hallazgos abiertos en [[Trazabilidad de requisitos del SRS]].
- Login Azure: si ARM pide MFA con token `amr=pwd`, usar `--claims-challenge` (ver `docs/azure.md`).
- **Mes de demo (hasta ~2026-10-23), ADR-017:** reinicio de la VM verificado (vuelve sola); prueba de
  disponibilidad de `/health` cada 15 min con alerta por correo; presupuesto de la suscripción USD 45/mes
  con avisos. Crédito ≈ USD 92.7 → ≈ 2 meses. Al terminar, el usuario decide borrar el grupo.
- No se tocó `vault/.obsidian/` preexistente sin rastrear.

## Qué abrir según la tarea

- **Colaboración Azure (2026-09-30):** compañero INTEC con object ID
  `e333eaea-7226-40a8-a583-a26d4d940caa` tiene Owner del grupo del proyecto,
  Key Vault Secrets Officer, Storage Blob Data Contributor y propiedad de app/SP
  SMTP. Azure/Graph confirmaron las cinco asignaciones. Alcance limitado al proyecto;
  GitHub, login TanQR y presupuesto de suscripción separados. Detalle en `docs/azure.md`.

| Tarea | Archivos |
|---|---|
| Continuar | [[Tareas pendientes]] + [[Bitacora de cambios]] |
| Requisito | [[Trazabilidad de requisitos del SRS]] + `docs/SRS.pdf` |
| Arquitectura | [[Decisiones de arquitectura]], incluidos DESCARTADO y ADR-007/008 |
| Backend | `backend/src/Combustible.Api/Program.cs`, `Endpoints/`, `Infrastructure/Data/`, `tests/` |
| Arranque | `scripts/iniciar.ps1`, `scripts/preparar.ps1`, `scripts/config-local.ps1` |
| Frontend | `frontend/src/App.tsx` (menú/rutas), pantallas `*.tsx`, `lib.ts`, `components.tsx`, `screens.css`, `frontend/e2e/` |
| Colaboración | [[Como trabajamos]]; comprobar archivos estables antes de escribir |

## SMS Twilio — 2026-09-30

Adaptador real implementado; backend 82/82 y frontend build/lint correctos.
Credenciales guardadas en .env ignorado y cinco secretos en Key Vault INTEC.
Prueba directa rechazada 572006: cuenta Trial solo admite plantillas predefinidas.
RF-09 recepción real sigue abierto, requiere Upgrade del usuario y prueba del ticket.
Azure 0.4.0-2b34698 desplegada; CI 36742182779 success, 82/82 backend y 10/10 navegador. Salud 200 y RestartCount=0. VM confirma Twilio configurado y rechazo HTTP 400/572006. PDF público con QR idéntico al PNG, render inspeccionado. Login bootstrap 401: falta acceso actual para ticket/reporte privado; solicitado al usuario.

## SMS genérico para presentación — 2026-09-30

Usuario autoriza plantilla genérica solo hoy. Twilio confirmó delivered y el usuario
confirmó recepción; plantilla sms_order_confirmation, datos de ejemplo, sin datos del
ticket. Implementación 4db7a30, pruebas backend 88/88 y frontend build/lint correctos.
TWILIO_TRIAL_UNTIL=2026-09-30, vencimiento al terminar el día de República Dominicana.
Secretos demo guardados en Key Vault. CI 36745051245 success; publicación Azure 0.4.0-4db7a30 completada, salud 200.
RF-09 completo continúa pendiente de SMS personalizado; ADR-020, docs/sms-twilio.md.

Verificación final de VM: plantilla y vencimiento presentes, RestartCount=0 y SMS
delivered sin error desde Azure. Aprobación local comprobada; recepción inicial
confirmada por usuario. No nueva aprobación autenticada Azure (bootstrap 401).
