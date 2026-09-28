---
tipo: proyecto
estado: activo
actualizado: 2026-09-28
---

# Trazabilidad de requisitos del SRS

Matriz viva de los 24 requisitos funcionales y 6 de seguridad de `docs/SRS.pdf`
(SRS Ticket Digitales v1.0, agosto 2026) contra su implementación real.

**Regla de oro:** un requisito solo pasa a `HECHO` cuando existe la prueba que lo demuestra
**y esa prueba se corrió y pasó**. No se marca `HECHO` por haber escrito el código.
Estados: `PENDIENTE` · `EN CURSO` · `HECHO` · `BLOQUEADO` (con el bloqueo de
[[Estado actual del proyecto]]).

**Verificación de relevo 2026-09-22:** los commits `a6a3e08` y `6304270` cubren entorno
y CI, no implementan ningún RF/RS ni cierran criterios de aceptación. Las dos pruebas
existentes verifican `/` y `/health` en memoria; no prueban base de datos, seguridad ni
inventario. SHA-256 de `docs/SRS.pdf` y el PDF original de Descargas coinciden:
`45391564A81B75863329C76A7529CCF37E49155AB6D717DC6B4BC9A8673CC79E`.
Comandos y resultados en [[Bitacora de cambios]]. En ese primer bloque H-01..H-07 y ADR-006 seguían abiertos;
preguntar no constituye aprobación.

---

## Requisitos funcionales

### Dominio y administración

| ID | Requisito | Qué exige el SRS | Estado | Dónde vive | Prueba |
|---|---|---|---|---|---|
| RF-01 | Gestión de Usuarios | Crear, modificar, desactivar, perfiles y roles, reset de contraseña, políticas de acceso. 5 roles mínimos: Administrador, Supervisor, Despachador, Auditor, Consulta | HECHO | API Endpoints/UserEndpoints.cs; frontend/Administration.tsx; frontend/App.tsx | SecurityTests: Edicion_de_usuario_es_atomica_y_reset_revoca_sesiones, Desactivar_cuenta_revoca_JWT_y_refresh; ProductTests: RBAC_limita_escritura_y_auditoria; Playwright roles.spec.ts (5 roles, escritorio/móvil, 2026-09-28) |
| RF-02 | Gestión de Empleados | Código, nombre, cédula, departamento, cargo, correo, teléfono móvil, estado | HECHO | Domain/Catalogs.cs; Endpoints/CatalogEndpoints.cs | CatalogTests: Empleados_y_vehiculos_requieren_departamento...; Edicion_de_empleado_y_vehiculo_recifra_y_conserva_unicidad (2026-09-23) |
| RF-03 | Gestión de Vehículos | Placa, ficha, marca, modelo, año, tipo, departamento, capacidad tanque, odómetro, estado | HECHO | Domain/Catalogs.cs; Endpoints/CatalogEndpoints.cs | CatalogTests: precisión, odómetro, relaciones; Edicion_de_empleado_y_vehiculo_recifra_y_conserva_unicidad (2026-09-23) |
| RF-04 | Gestión de Departamentos | Crear, modificar, asociar empleados, asociar vehículos | HECHO | Endpoints/CatalogEndpoints.cs; frontend/src/App.tsx | ProductTests: Persistencia_versiones_y_auditoria_transaccional; Playwright catalogs.spec.ts; empleados/vehículos asociados por departmentId |

### Ciclo de vida del ticket

| ID | Requisito | Qué exige el SRS | Estado | Dónde vive | Prueba |
|---|---|---|---|---|---|
| RF-05 | Creación de Solicitudes | Manuales, automáticas programadas y recurrentes. Campos: empleado, vehículo, departamento, cantidad autorizada, tipo combustible, fecha solicitud, fecha vencimiento | HECHO | Endpoints/TicketEndpoints.cs; Tickets/LifecycleService.cs; frontend Requests.tsx, Schedules.tsx | FuelTests: Programaciones_generan_solicitudes_y_asignan_automaticamente |
| RF-06 | Emisión de Tickets Digitales | UUID, secuencia correlativa, fechas creación/vencimiento, vehículo, empleado, departamento, cantidad, tipo. Formatos: PDF, correo, QR | HECHO | Tickets/TicketService.cs; Documents/Documents.cs (PDF) | FuelTests: Emision_numera_firma_y_envia_por_SMTP_real |
| RF-07 | Generación de QR Seguro | Datos protegidos por hash: ticket ID, número secuencial, empleado, vehículo, cantidad, fecha emisión, fecha expiración. Criterios: no reutilizable, no editable, único, verificación criptográfica | HECHO | Security/Crypto.cs (TicketSigner ECDSA P-256) | FuelTests: QR_alterado_o_ticket_modificado_en_la_base_se_rechaza; reuso y vencido rechazados |
| RF-08 | Numeración de Tickets | Secuencia consecutiva, prefijo configurable, reinicio anual opcional, sin duplicados. Formato ejemplo: `COM-2026-000001` | HECHO | TicketService.IssueAsync; TicketSettings (prefijo, reinicio anual) | FuelTests: Emision_concurrente_no_duplica_ni_salta_numeros; Parametros_exigen_version_y_rol_administrador |
| RF-09 | Envío de Tickets | Correo con QR y datos. SMS con código corto, URL segura y QR descargable | HECHO en código; producción BLOQUEADA (B-01 SMS, B-02 SMTP) | Messaging/Senders.cs; enlace público /ticket/<id>.<token>; PublicTicket.tsx | FuelTests: Enlace_publico_muestra_el_ticket_y_su_QR_descargable; Sin_SMTP_la_entrega_queda_pendiente... |
| RF-10 | Consulta de Estado | 7 estados: Creado, Enviado, Pendiente, Próximo a vencer, Vencido, Consumido, Anulado | HECHO | Domain/Fuel.cs TicketStatus; LifecycleService (vencer/avisar) | FuelTests: Proceso_periodico_marca_proximo_a_vencer_y_vencido; Anulacion_rechazo_y_cancelacion... |
| RF-11 | Asignaciones | Manual por usuario autorizado. Automática por programación, reglas de negocio y consumo histórico | HECHO | FuelSchedule (cantidad fija o promedio histórico, aprobación automática) | FuelTests: Programaciones_generan_solicitudes_y_asignan_automaticamente |

### Despacho

| ID | Requisito | Qué exige el SRS | Estado | Dónde vive | Prueba |
|---|---|---|---|---|---|
| RF-12 | Despacho de Combustible | Escanear QR, validar ticket, confirmar identidad, registrar despacho. Datos: fecha, hora, galones servidos, operador, estación, observaciones | HECHO | Endpoints/InventoryEndpoints.cs /api/despachos; frontend Dispatch.tsx | FuelTests: Despacho_atomico_descuenta_inventario_y_consume_el_ticket |
| RF-13 | App Móvil de Despacho | Login seguro, escaneo QR, confirmación visual, validación en línea, registro de despacho, consulta de tickets, sincronización inmediata | HECHO (PWA); CA-6 pendiente en Android físico | frontend Dispatch.tsx, scanner.ts, vite.config.ts (PWA) | Playwright screens.spec.ts (Pixel 7 emulado) |

### Inventario

| ID | Requisito | Qué exige el SRS | Estado | Dónde vive | Prueba |
|---|---|---|---|---|---|
| RF-14 | Control de Inventario | Entradas (recepción, compra, transferencias), salidas (despachos, mermas), ajustes (positivos, negativos) | HECHO | Endpoints/InventoryEndpoints.cs recepciones/transferencias/ajustes | FuelTests: Recepcion_transferencia_ajustes_y_existencia_en_tiempo_real |
| RF-15 | Inventario en Tiempo Real | Existencia actual, disponibilidad, consumo diario, consumo mensual, nivel crítico | HECHO | Endpoints/InventoryEndpoints.cs GET /api/inventario; Dashboard.tsx | FuelTests: Recepcion_transferencia_ajustes_y_existencia_en_tiempo_real |
| RF-16 | Recepción de Combustible | RNC, nombre suplidor, factura, volumen recibido, fecha, tanque. Impacta inventario automáticamente | HECHO | ReceiptRequest (RNC 9/11 dígitos) | FuelTests: Recepcion_transferencia_ajustes... |
| RF-17 | Movimientos de Inventario | Historial completo de entradas, salidas, ajustes y transferencias | HECHO | Endpoints/InventoryEndpoints.cs /api/inventario/movimientos (historial solo inserción) | FuelTests: Recepcion_transferencia_ajustes... |
| RF-18 | Cierre Diario | Confirmar despachos, volumen despachado, inventario final, diferencias detectadas. Genera acta digital de cierre y reporte PDF | HECHO | Endpoints/InventoryEndpoints.cs /api/cierres + PDF; DailyClose.tsx | FuelTests: Cierre_diario_genera_acta_y_bloquea_movimientos_del_dia |

### Análisis y plataforma

| ID | Requisito | Qué exige el SRS | Estado | Dónde vive | Prueba |
|---|---|---|---|---|---|
| RF-19 | Reportes | Filtrables por fecha, empleado, vehículo, departamento, tipo combustible, estado ticket | HECHO | Endpoints/ReportEndpoints.cs; Reports.tsx | FuelTests: Reportes_filtran_y_exportan_excel_csv_y_pdf |
| RF-20 | Exportación | Excel, CSV, PDF | HECHO | Documents.cs (ClosedXML, CSV con escape de fórmulas, PDF) | FuelTests: Reportes_filtran_y_exportan_excel_csv_y_pdf |
| RF-21 | Trazabilidad | Auditoría de creaciones, modificaciones, despachos, ajustes, anulaciones y accesos. Incluye usuario, fecha, hora, dirección IP | HECHO | AuditWriter.cs; auditoría en cada escritura, exportación y acceso | SecurityTests + FuelTests |
| RF-22 | Dashboard Ejecutivo | Inventario actual, combustible despachado, tickets activos, tickets vencidos, consumo por departamento, consumo por vehículo | HECHO | Endpoints/ReportEndpoints.cs /api/dashboard; Dashboard.tsx | FuelTests: Dashboard_y_bandeja_de_notificaciones |
| RF-23 | Notificaciones | Alertas automáticas: ticket próximo a vencer, ticket vencido, inventario bajo, fallo de integración, ajustes de inventario | HECHO | Notifier; LifecycleService; Notifications.tsx | FuelTests: Dashboard_y_bandeja_de_notificaciones |
| RF-24 | API REST | Servicios de generación de tickets, consulta, estado de inventario, despachos, reportes | HECHO | API REST documentada en /openapi; acceso de terceros por OAuth (ADR-011) | OAuth2_client_credentials_emite_JWT_de_acceso_sometido_al_RBAC |

---

## Requisitos de seguridad

| ID | Requisito | Qué exige el SRS | Estado | Dónde vive | Prueba |
|---|---|---|---|---|---|
| RS-01 | Autenticación | Usuario/contraseña, MFA opcional, gestión de sesiones | HECHO | Endpoints/AuthEndpoints.cs; Security/SessionService.cs | SecurityTests: MFA_necesita_codigo_valido..., Cinco_fallos_bloquean..., JWT_alterado...; e2e mfa.spec.ts (alta/baja); login real verificado en Azure 2026-09-23 |
| RS-02 | Autorización | Control RBAC basado en roles | HECHO | Program.cs; políticas por rol; frontend/App.tsx (visibilidad por rol) | ProductTests: RBAC_limita_escritura_y_auditoria, Sin_token_no_hay_acceso; Playwright roles.spec.ts (matriz completa de navegación, 2026-09-28); CI `36443297257` correcta y revisión `18a1e17` desplegada en Azure. Las lecturas de API necesarias para OAuth/RF-24 se conservan separadas de la visibilidad de paneles humanos. |
| RS-03 | Cifrado | Tránsito: TLS 1.3. Reposo: AES-256 | HECHO en código (AES-256-GCM, TLS 1.3); **verificado en Azure 2026-09-23** con certificado Let's Encrypt del FQDN de demo (TLS 1.3 ok, TLS 1.2 rechazado, `docs/azure.md`); dominio institucional sigue B-03 | Security/Crypto.cs FieldProtector; Security/TransportSecurity.cs | HardeningTests: cifrado, backfill, Kestrel_rechaza_TLS_1_2... |
| RS-04 | Seguridad de QR | Firma digital, hash SHA-256, token de validación | HECHO | Security/Crypto.cs TicketSigner | FuelTests: QR_alterado...; Solo_existe_una_ruta_de_despacho... |
| RS-05 | Seguridad de APIs | OAuth 2.0, JWT | HECHO | OAuthEndpoints.cs (OpenIddict, client credentials, ADR-011) | HardeningTests: OAuth2_* (2) |
| RS-06 | Auditoría | Registro **inalterable** de accesos, cambios, despachos y ajustes | HECHO (hash encadenado + permisos SQL + ancla externa) | AuditWriter.cs; UserEndpoints.cs /api/auditoria/ancla | HardeningTests: Ancla_externa_detecta_cadena_reconstruida_por_un_superusuario |

> **Nota sobre RS-06.** El SRS dice "inalterable". Una tabla normal de base de datos no lo es:
> quien tenga permiso de escritura puede modificarla. Cómo se consigue realmente esa
> inalterabilidad se resolvió con hash encadenado, permisos SQL y ancla externa firmada
> (ADR-006 → ADR-007/ADR-012 en [[Decisiones de arquitectura]]). Límite conocido: el ancla
> solo protege si un Auditor la descarga y guarda fuera del sistema cada semana.

---

## Criterios de aceptación del SRS

Los 7 criterios de la sección 7. El proyecto no está terminado mientras uno siga en rojo.

| # | Criterio | Cómo se demuestra | Estado |
|---|---|---|---|
| CA-1 | Se emiten tickets QR únicos sin duplicidad | Prueba de concurrencia: N emisiones en paralelo, cero colisiones de secuencia | HECHO — Emision_concurrente_no_duplica_ni_salta_numeros |
| CA-2 | El despacho se valida **exclusivamente** mediante QR válido | Prueba de que no existe ninguna ruta alterna de despacho sin QR verificado | HECHO — Solo_existe_una_ruta_de_despacho_y_la_base_impide_atajos |
| CA-3 | El inventario se actualiza en tiempo real | Prueba de integración: despacho → el saldo del tanque refleja el cambio en la misma transacción | HECHO — Despacho_atomico...; Despachos_concurrentes_no_sobregiran_el_tanque |
| CA-4 | Existe trazabilidad completa de las operaciones | Prueba de que toda operación de escritura deja registro de auditoría | HECHO — auditoría en la misma transacción de cada escritura |
| CA-5 | Los reportes son exportables | Prueba de generación de Excel, CSV y PDF con datos reales | HECHO — Reportes_filtran_y_exportan_excel_csv_y_pdf |
| CA-6 | La app móvil opera correctamente en producción | Prueba de extremo a extremo de la PWA en Android real, incluido el escaneo | PENDIENTE — requiere Android físico; HTTPS válido ya disponible en Azure (2026-09-23) |
| CA-7 | Se cumplen los requisitos de seguridad establecidos | RS-01 a RS-06 todos en `HECHO` + revisión de seguridad | HECHO (revisión manual 2026-09-22; ver bitácora; una decisión abierta) |

---

## Huecos del SRS — ambigüedades reales que hay que cerrar con el usuario

El SRS es claro en el qué y silencioso en varios cómo. Estos huecos son reales, no descuidos
de lectura. Hay que cerrarlos antes de que el código los fije por accidente.

| # | Hueco | Por qué importa |
|---|---|---|
| H-01 | No dice cuántas estaciones ni cuántos tanques hay, pero RF-16 pide registrar "tanque" | Decide si el modelo de inventario es un saldo único o N tanques con transferencias entre ellos |
| H-02 | No define qué pasa si el despacho real difiere de la cantidad autorizada del ticket | RF-12 registra "galones servidos", RF-06 fija "cantidad autorizada". ¿Se permite despachar de menos? ¿Queda saldo? ¿Se puede despachar de más? |
| H-03 | No define el plazo de vencimiento por defecto de un ticket ni qué umbral es "próximo a vencer" | RF-10 lista ambos estados pero no da el número |
| H-04 | No dice si un ticket puede consumirse en varios despachos parciales | Cambia el modelo entero: ticket como evento único o como saldo consumible |
| H-05 | "Confirmar identidad" en RF-12 no está especificado | ¿Cédula? ¿Foto? ¿Firma en pantalla? ¿Basta con que el despachador lo afirme? |
| H-06 | No define la política de contraseñas concreta que pide RF-01 | Largo mínimo, complejidad, caducidad, historial |
| H-07 | No dice qué hacer si el punto de despacho se queda sin conexión | RF-13 pide "validación en línea", pero una estación puede perder señal. ¿Se bloquea el despacho o se encola? |

**Ninguno de estos se resuelve adivinando.** Se preguntan al usuario y la respuesta se
registra como ADR en [[Decisiones de arquitectura]].


## Resolución delegada — 2026-09-22

El usuario delegó las decisiones y la configuración. H-01..H-07 y ADR-006 quedan
resueltos por ADR-007, con autenticación/persistencia en ADR-008. Las preguntas anteriores
se conservan como origen del contrato, no como bloqueos sin respuesta. Las pruebas de
Fase 1 y los commits están en la bitácora. Ningún CA de producto completo se cierra aún.

## 2026-09-22 — Evidencia incremental de MFA y concurrencia

Commit `08a91ef`: RS-01 y RS-05 parciales en `AuthEndpoints.cs`/`Administration.tsx`:
regeneración y desactivación MFA, contraseña + segundo factor, revocación de sesiones.
`SecurityTests.MFA_necesita_codigo_valido_y_revoca_sesiones_anteriores` y
`frontend/e2e/mfa.spec.ts` verifican el ciclo. `ProductTests` cubre refresh simultáneo
(RS-05) y ocho escrituras/auditoría concurrentes (RS-06). Comandos/resultados:
`dotnet test backend --nologo` 24/24; `./scripts/probar-interfaz.ps1` 4/4;
`dotnet build backend --configuration Release --nologo`, `npm run build` y
`npm run lint` correctos. OAuth2 permanece pendiente; móvil emulado no cierra CA-6.

## Revisión completa contra el SRS — 2026-09-23 (Claude)

Relectura íntegra de `docs/SRS.pdf` (12 páginas) contra código, pruebas y la demo en Azure.

**Verificado en producción (Azure, datos DEMO):** correo real entregado en el buzón del
usuario (ticket `COM-2026-000001`, QR en línea, PDF adjunto, código corto y enlace seguro
al dominio de Azure); enlace público sin sesión oculta el QR de un ticket consumido;
QR decodificado con el mismo zxing-wasm de la PWA → validar 200, QR alterado 422,
despacho 201, reuso 422; existencia 500→490 en la misma transacción; ticket `Consumed`;
reportes XLSX/CSV/PDF 200; dashboard 200; cierre diario 201 + acta PDF; ajuste posterior
rechazado ("El día operativo ya fue cerrado"); `/api/auditoria/verificar` → `valid: true`.

**Cubierto aunque la matriz no lo nombraba:** actor Solicitante (3.4) = rol Consulta con
`request-create`; mermas y compras (RF-14) en `MovementKind`; disponibilidad y consumo
mensual (RF-15) en `GET /api/inventario`.

**Hallazgos abiertos (requieren decisión del usuario):**
1. `GET /api/empleados` solo exige autenticación: todos los roles y clientes OAuth ven cédula,
   correo y móvil descifrados. Mínimo privilegio sugiere restringirlo a Administrador/Supervisor.
2. Objetivo 1.2 "disponibilidad 24/7": la demo es una sola VM sin redundancia (ADR-014).
3. Key Vault sin *purge protection*: purgar `data-encryption-key` o `qr-signing-key-b64`
   inutiliza datos cifrados y respaldos. Activarla es irreversible.
4. Permisos de clientes OAuth con rol Supervisor (decisión pendiente desde Fase 6).
5. Siguen fuera de alcance del código: SMS real (B-01), datos reales (B-04), Android físico (CA-6).
