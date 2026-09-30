---
tipo: proyecto
estado: activo
actualizado: 2026-09-30
---

# Tareas pendientes

Backlog por fases. **Una fase no empieza hasta que la anterior está verificada**, porque cada
una depende de contratos que fija la anterior.

Cómo se usa: toma la primera tarea sin marcar de la fase activa cuyas dependencias estén
cumplidas. Al terminarla, márcala aquí, anota en [[Bitacora de cambios]] y actualiza
[[Trazabilidad de requisitos del SRS]] si cerró un RF o RS.

Notación: `[ ]` pendiente · `[~]` en curso · `[x]` hecho y verificado · `[!]` bloqueada.

**Entrega vigente — 2026-09-30:** QA-PDF-01 y QA-PDF-02 están cerrados y publicados
en Azure `0.4.0-e43897b`. Backend 66/66, navegador 10/10 y CI `36730599453`
success. Los commits `f10045c`, `20fb182` y `93ce2f3` tienen entradas y evidencia
en [[Bitacora de cambios]]. Siguen pendientes SMS real, Android físico y los
recursos institucionales indicados abajo; los defectos PDF no son tareas abiertas.

---

## Fase 0 — Montaje del entorno (verificada 2026-09-22)

- [x] Crear estructura del repositorio y git
- [x] Crear vault de Obsidian dedicado
- [x] Escribir `AGENTS.md` canónico y punteros `CLAUDE.md` / `CODEX.md`
- [x] Instalar .NET 10 LTS (10.0.401 verificado junto a 9.0.315)
- [x] Matriz de trazabilidad de los 30 requisitos del SRS
- [x] Revisar el sync del bridge Claude↔Codex. **Hallazgo distinto al esperado**: `mcp=0` era
      correcto (no hay MCP portables que espejar), pero faltaban 10 skills de cuenta. Arreglado
      y verificado — ver [[Entorno de agentes]]
- [x] PostgreSQL 17 healthy, `pg_isready` y SQL autenticado por TCP verificados.
      `.env` generado por delegación; puerto 15432 para no interferir con servicio en 5432.
- [x] Esqueleto del backend .NET 10 con 5 proyectos. `dotnet test` da 2/2: las pruebas
      levantan la API en memoria y comprueban `/health` y `/`
- [x] Esqueleto del frontend React+TS+Vite. `npm run build` compila
- [x] CI de Fase 0 ejecutada correctamente en GitHub: run `35759842395`.
- [x] H-01..H-07 resueltos por delegación explícita del usuario en ADR-007
- [x] ADR-006 resuelto por ADR-007: hash + permisos SQL, límites documentados

**Criterio de fin de fase:** `dotnet build`, `dotnet test`, `npm run build` y
`docker compose up` corren los cuatro sin error, PostgreSQL tiene salud y consulta
autenticada verificadas, CI tiene una ejecución remota correcta y H-01..H-07/ADR-006
tienen respuesta explícita registrada en las decisiones. Evidencia en la bitácora.

**Estado al 2026-09-22:** entorno ejecutado, PostgreSQL comprobado, CI remota correcta
y decisiones registradas. Fase 0 cerrada; el desarrollo continúa por delegación.

---

## Fase 1 — Dominio y autenticación (verificada)

Cubre RF-01 a RF-04, RS-01, RS-02, RS-05, RS-06.

- [x] Modelo de Usuario, Rol, Empleado, Vehículo, Departamento y migración EF Core aplicada
- [x] Inicialización idempotente: 5 roles y administrador local; sin inventar datos de INTEC
- [x] JWT y refresh tokens rotativos; revocación por logout, contraseña y cambios de acceso
- [x] MFA TOTP con confirmación y códigos de recuperación de un solo uso (API probada)
- [x] RBAC de 5 roles probado con PostgreSQL real
- [x] Política de contraseñas H-06 implementada con bloqueo 5 fallos/15 minutos
- [x] Catálogos persistentes con validación, bajas lógicas, duplicados y control de versión
- [x] Interfaz de login, catálogos y usuarios; pruebas de navegador escritorio/móvil emulado
- [~] Auditoría transaccional encadenada y permisos SQL; revisar cobertura y recuperación
- [x] Pruebas de concurrencia de refresh/auditoría y ciclo completo de MFA: 24/24 backend,
      4/4 navegador en escritorio/móvil emulado (`08a91ef`, 2026-09-22)
- [x] OAuth 2.0 client credentials para integraciones (RS-05, ADR-011): `OAuth2_*` 2 pruebas (`2f3013b`)
- [x] Interfaz para desactivar MFA y gestionar recuperación después del alta
- [x] Cambios de acceso exigen versión (`Cambio_de_acceso_de_usuario_exige_la_version_vista`); cadena reconstruida detectada por ancla (ADR-012)
- [x] Revisión de configuración y secretos de producción: Key Vault + env 0600 en la VM (2026-09-23; dominio institucional sigue B-03)
- [x] CI ampliada de Fase 1: run `35764488194` correcto para `70972c7`. Rama `fase-2-producto` subida el 2026-09-22; CI de entrega en Fase 6.

---

## Fases 2 a 5 — Tickets, inventario, despacho, PWA, reportes (código completo 2026-09-22, rama `fase-2-producto`)

Evidencia repetida por Astra: backend52/52, build/lint frontend correctos; CI `35814915846` backend52/52 y Playwright6/6 correctos. Detalle en [[Bitacora de cambios]].

- [x] Solicitud y Ticket con los 7 estados (RF-10, ADR-009)
- [x] Numeración sin huecos bajo concurrencia (CA-1): `Emision_concurrente_no_duplica_ni_salta_numeros`
- [x] Firma ECDSA del QR; alterado, reusado y vencido se rechazan (RS-04)
- [x] PDF del ticket; correo real a Mailpit por SMTP; SMS solo a bandeja local — **[!] B-01/B-02 para producción**
- [x] Solicitudes programadas y recurrentes con aprobación automática opcional (RF-05, RF-11)
- [x] Vencimiento y aviso automáticos (RF-10)
- [x] Tanques, movimientos, recepciones con RNC, transferencias, ajustes con motivo (RF-14..RF-17)
- [x] Despacho atómico y sin sobregiro concurrente (CA-3); única ruta de despacho (CA-2)
- [x] Cierre diario con acta PDF y bloqueo de movimientos del día (RF-18)
- [x] Reportes con filtros y exportación Excel/CSV/PDF (RF-19, RF-20, CA-5); dashboard (RF-22); alertas (RF-23)
- [x] PWA: escáner (cámara, lector externo, foto), confirmación de identidad, instalable, API NetworkOnly (H-07)
- [x] Pantallas web de todos los módulos, menú por rol, ticket público (RF-09)
- [ ] Prueba de extremo a extremo en un Android físico (CA-6) — requiere dispositivo y HTTPS (B-03)
- [x] Cliente OAuth con rol Supervisor ya no aprueba/anula/escribe (ADR-016, 2026-09-23)

---

## Fase 6 — Endurecimiento y entrega

- [x] Cifrado AES-256-GCM de cédula/correo/móvil con índice ciego (RS-03, ADR-010)
- [x] Kestrel solo TLS 1.3 (`Kestrel_rechaza_TLS_1_2_y_negocia_TLS_1_3`) — certificado real **[!] B-03**
- [x] Revisión de seguridad CA-7 (manual, 2026-09-22): sin vulnerabilidades en dependencias; un punto de decisión abierto
- [x] Empujar `fase-2-producto` y verificar CI remota — `35814915846` correcta para `4c105d7`
- [x] Publicar contenedor en GHCR tras CI — digest `daa6cbab29efa116317bbff111ca9afa849e08580948c414ad07d499f86aa50b`
- [x] Verificar privacidad del paquete: CI `35818197956` de `345e623` correcta; API devuelve `visibility: private`
- [x] Identificar cuenta Azure INTEC, verificar cuotas y precio, preparar IaC (`345e623`; Bicep y conformance correctos)
- [x] Reautenticar sesión Azure INTEC con MFA (2026-09-23, `--claims-challenge` amr=mfa)
- [x] Desplegar en Azure INTEC: app/PostgreSQL/secretos/certificado/backups; HTTPS, TLS 1.3/1.2, login y restauración verificados (2026-09-23, `docs/azure.md`)
- [x] Correo ACS entregado en el buzón del usuario (autorizado 2026-09-23, ticket DEMO COM-2026-000001)
- [x] Prueba de extremo a extremo en Azure: QR→validar→despachar→inventario→reportes→cierre→auditoría (2026-09-23)
- [x] Prueba de edición de empleados/vehículos (RF-02/RF-03): 53/53 backend
- [x] PII del empleado solo para Administrador/Supervisor (ADR-016, verificado en Azure 2026-09-23)
- [x] *Purge protection* activada en Key Vault (irreversible, 2026-09-23)
- [x] OAuth Supervisor sin poder de escritura; despacho/cierre solo Despachador (ADR-016)
- [x] **Disponibilidad 24/7 durante el mes de demo (hasta ~2026-10-23):** el usuario solo necesita ~1 mes; crédito ≈ USD 92.7 alcanza ≈ 2 meses, se mantiene la VM actual (2026-09-23). Plan B si el saldo real < USD 45: VM gratuita B2ats_v2 + Premium SSD P6
- [x] Reinicio de la VM verificado: vuelve sola, `/health` y login 200, `RestartCount=0` (2026-09-23)
- [x] Vigilancia (ADR-017): prueba estándar `/health` cada 15 min + certificado, alerta por correo, presupuesto USD 45/mes; primera ejecución 100 % (2026-09-23)
- [x] Alertas verificadas de punta a punta por **push** (app Microsoft Azure del usuario): prueba sin caída apuntando la prueba a `/health/prueba-alerta`, "Fired" 15:43 UTC y "Resolved" 16:08 UTC recibidos (2026-09-23). El correo no está verificado (OTP vencido, Azure no reenvía) y no llega
- [ ] SMS real (RF-09 lo pide; la función está programada y prueba en bandeja): contratar proveedor NO es necesario para presentar. Solo si INTEC adopta el sistema
- [ ] Correo/dominio institucional y datos reales: NO los pide el SRS; solo si INTEC adopta el sistema
- [ ] Carga masiva (CSV/Excel) de empleados, vehículos y tanques para B-04: hoy solo existe alta uno a uno en Catálogos
- [x] Adaptador SMS Twilio (`ISmsSender`), 82/82 backend, build/lint frontend; secretos Key Vault (2026-09-30). Recepción real bloqueada por Trial 572006.
- [x] Datos DEMO ficticios para que la demo no se vea vacía (2026-09-23, ver bitácora)
- [ ] **Rediseño de la UI** (el usuario la ve genérica): decisión pendiente del usuario; no empezar sin ella
- [x] Galones legibles ("10" y no "10.000") en correo/SMS/PDF/acta/mensajes; desplegado `9fed40e` (2026-09-23)
- [x] QR: se mantiene payload firmado + texto junto al código y enlace para el teléfono (ADR-018, decisión delegada, 2026-09-23)
- [x] Saldo real informado por el usuario 2026-09-23: **USD 92.68** restantes (7.32 usados de 100); pronóstico del mes USD 5.31 aún sin la VM facturada
- [ ] ~2026-10-23: el usuario decide si borra `rg-intec-fuel-dev-b805` y el presupuesto `presupuesto-credito-estudiante` (el agente no borra sin orden)
- [ ] Recuperar acceso SSH: el usuario da la frase de paso de `artifacts/azure/id_ed25519` o autoriza una clave nueva
- [ ] (Baja) Persistir claves de Data Protection: hoy solo las usa el reset de contraseña en una misma petición
- [ ] Actualizaciones futuras: usar la imagen de GHCR (credencial de lectura en la VM) en vez de construir en la VM
- [x] Despliegue de un solo origen (Kestrel TLS 1.3 + CSP), Dockerfile y compose de producción probados (`1c05ca7`)
- [x] Prueba de carga básica: 250 rps, 0 errores (base casi vacía; repetir con volumen real)
- [x] Manual de usuario con ejercicios de capacitación (`docs/manual-usuario.md`)
- [x] Informe final (`docs/informe-final.md`)

---

## Repositorio

- [x] Poner `fase-2-producto` como rama predeterminada de GitHub; conservar las ramas de fases anteriores.

## Correcciones posteriores a la entrega

- [x] Revalidar aviso de Azure/instalador antiguos: GitHub e imagen activa e43897b
      coinciden; PDF recién descargados con QR decodificado y tabla completa.
      Aviso de 18a1e17 obsoleto; evidencia en QA de presentación, 2026-09-30.

- [x] Confirmar los dos PDF reportados y ejecutar la regresión disponible (2026-09-30):
      reproducción aislada y evidencia en `docs/qa-presentacion-2026-09-30.md`.
- [x] **QA-PDF-01:** corregir dimensiones/orientación y ancho de tablas PDF. Despachos,
      tickets, movimientos y detalle del acta reproducen recorte. Criterio: todas las
      columnas legibles dentro de la página, con render visual y prueba de límites,
      incluyendo varias páginas y encabezados repetidos. Cerrado en `f10045c`:
      geometría real, encabezados/última fila y revisión visual; backend **65/65**.
- [x] **QA-PDF-02:** corregir incrustación del QR en ticket PDF (`f10045c`).
      Imagen visible y decodificable desde la página PDF renderizada, payload firmado
      de 146 caracteres idéntico al QR de origen y sin recuadros de error.
- [x] Corregir texto DEMO mal codificado en el reporte (`EstaciÃ³n`): un nombre de
      estación estaba guardado así en Azure. Corrección puntual a `Estación` por API
      de catálogo con If-Match, estado/código intactos y copia local de rollback.
      No era un error global del renderer; inventario sin modificaciones.
- [x] Ajustar palabras largas al ancho interior de celdas PDF (`93ce2f3`):
      medición de la fuente real y ajuste sin añadir guiones visibles. Regresión de
      límites de texto y CSV intacto; backend **66/66**, incluida prueba con DejaVu.
- [x] Corregir QR de portada: el original era la URL de la app, no un ticket.
      Copia `Downloads/portada corregida.pptx` usa COM-2026-000008 firmado, decodificado
      desde render, 28 diapositivas y paquete validado. Uso único; vence 2026-10-03.
- [x] Auditar COM-2026-000009 en Azure: Pendiente, sin despacho, correo fallido
      por destinatario reservado `.test` (SMTP 5.1.4). Cinco consumidos y cinco
      despachos concordantes. No alterar estados por comentarios sin despacho registrado.
- [x] Corregir el correo autorizado del empleado de COM-2026-000009 y reenviar:
      dato real proporcionado por el usuario; PUT con If-Match conservando otros
      campos. Reenvío único Email=Sent, SMS=Outbox, ticket Enviado sin despacho.
      PDF nuevo y QR decodificado idéntico al origen. SMTP aceptó el mensaje;
      recepción en bandeja depende de confirmación del destinatario.
- [x] Refrescar lista/detalle de tickets entre sesiones (`e43897b`): foco/visibilidad,
      15 s mientras visible y botón Actualizar. CI cuatro jobs success y suite local
      final **10/10** con prueba de ambas vistas tras confirmar el despacho.
- [ ] Implementar proveedor SMS y comprobar recepción real en teléfono:
      los nueve intentos actuales son Outbox. Usuario pidió continuar alta/integración
      en otro chat; prompt y pasos en `docs/handoff-sms.md`. RF-09 real sigue abierto.
- [x] Completar recorrido de presentación mediante UI (`20fb182`): solicitud → aprobación →
      descarga/decodificación → despacho → inventario → reporte → cierre. Playwright
      **10/10** juntos en una base aislada, Desktop Chrome y Pixel 7 emulado, con
      rechazos de reuso, exceso de autorización, stock insuficiente y cierre duplicado.
      Android físico/cámara real sigue pendiente (CA-6).

- [x] Restringir los paneles visibles por rol según los actores del SRS y ADR-016; matriz de los
      cinco roles verificada por Playwright en escritorio y móvil (`be50048`, 2026-09-28),
      subida en `18a1e17`, CI `36443297257` correcta y desplegada en Azure.

## Reglas del backlog

- [x] Igualar acceso Azure del compañero dentro de INTEC-combustible (2026-09-30):
      Owner del grupo, gestión de secretos, Blob Data Contributor y propietario app/SP
      SMTP; cinco asignaciones verificadas mediante Azure CLI/Graph. GitHub y login
      TanQR separados. Falta que el compañero confirme entrada desde su propia sesión.

- Una tarea marcada `[x]` significa que **se corrió la prueba y pasó**, no que se escribió el código.
- Si descubres trabajo que falta, añádelo aquí en el momento. Un hallazgo que no se escribe se pierde.
- Si una tarea está `[!]` bloqueada, no la rodees inventando un sustituto: sigue a la siguiente
  y deja constancia del bloqueo en [[Estado actual del proyecto]].

- [ ] Twilio: Upgrade para mensajes personalizados; comprobar remitente/permisos RD y recepción de un ticket real. Prueba 2026-09-30 rechazada 572006; ver docs/sms-twilio.md.
