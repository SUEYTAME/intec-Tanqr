---
tipo: proyecto
estado: activo
actualizado: 2026-09-22
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
| RF-01 | Gestión de Usuarios | Crear, modificar, desactivar, perfiles y roles, reset de contraseña, políticas de acceso. 5 roles mínimos: Administrador, Supervisor, Despachador, Auditor, Consulta | EN CURSO | API Endpoints/UserEndpoints.cs; frontend/Administration.tsx | SecurityTests: edición, reset y revocación; ProductTests: RBAC |
| RF-02 | Gestión de Empleados | Código, nombre, cédula, departamento, cargo, correo, teléfono móvil, estado | EN CURSO | Domain/Catalogs.cs; Endpoints/CatalogEndpoints.cs | CatalogTests: persistencia y relaciones; edición adicional pendiente |
| RF-03 | Gestión de Vehículos | Placa, ficha, marca, modelo, año, tipo, departamento, capacidad tanque, odómetro, estado | EN CURSO | Domain/Catalogs.cs; Endpoints/CatalogEndpoints.cs | CatalogTests: precisión, odómetro y relaciones; edición adicional pendiente |
| RF-04 | Gestión de Departamentos | Crear, modificar, asociar empleados, asociar vehículos | EN CURSO | Endpoints/CatalogEndpoints.cs; frontend/src/App.tsx | ProductTests: persistencia/versiones; Playwright: crear/editar/desactivar |

### Ciclo de vida del ticket

| ID | Requisito | Qué exige el SRS | Estado | Dónde vive | Prueba |
|---|---|---|---|---|---|
| RF-05 | Creación de Solicitudes | Manuales, automáticas programadas y recurrentes. Campos: empleado, vehículo, departamento, cantidad autorizada, tipo combustible, fecha solicitud, fecha vencimiento | PENDIENTE | — | — |
| RF-06 | Emisión de Tickets Digitales | UUID, secuencia correlativa, fechas creación/vencimiento, vehículo, empleado, departamento, cantidad, tipo. Formatos: PDF, correo, QR | PENDIENTE | — | — |
| RF-07 | Generación de QR Seguro | Datos protegidos por hash: ticket ID, número secuencial, empleado, vehículo, cantidad, fecha emisión, fecha expiración. Criterios: no reutilizable, no editable, único, verificación criptográfica | PENDIENTE | — | — |
| RF-08 | Numeración de Tickets | Secuencia consecutiva, prefijo configurable, reinicio anual opcional, sin duplicados. Formato ejemplo: `COM-2026-000001` | PENDIENTE | — | — |
| RF-09 | Envío de Tickets | Correo con QR y datos. SMS con código corto, URL segura y QR descargable | BLOQUEADO (B-01, B-02) | — | — |
| RF-10 | Consulta de Estado | 7 estados: Creado, Enviado, Pendiente, Próximo a vencer, Vencido, Consumido, Anulado | PENDIENTE | — | — |
| RF-11 | Asignaciones | Manual por usuario autorizado. Automática por programación, reglas de negocio y consumo histórico | PENDIENTE | — | — |

### Despacho

| ID | Requisito | Qué exige el SRS | Estado | Dónde vive | Prueba |
|---|---|---|---|---|---|
| RF-12 | Despacho de Combustible | Escanear QR, validar ticket, confirmar identidad, registrar despacho. Datos: fecha, hora, galones servidos, operador, estación, observaciones | PENDIENTE | — | — |
| RF-13 | App Móvil de Despacho | Login seguro, escaneo QR, confirmación visual, validación en línea, registro de despacho, consulta de tickets, sincronización inmediata | PENDIENTE | — | — |

### Inventario

| ID | Requisito | Qué exige el SRS | Estado | Dónde vive | Prueba |
|---|---|---|---|---|---|
| RF-14 | Control de Inventario | Entradas (recepción, compra, transferencias), salidas (despachos, mermas), ajustes (positivos, negativos) | PENDIENTE | — | — |
| RF-15 | Inventario en Tiempo Real | Existencia actual, disponibilidad, consumo diario, consumo mensual, nivel crítico | PENDIENTE | — | — |
| RF-16 | Recepción de Combustible | RNC, nombre suplidor, factura, volumen recibido, fecha, tanque. Impacta inventario automáticamente | PENDIENTE | — | — |
| RF-17 | Movimientos de Inventario | Historial completo de entradas, salidas, ajustes y transferencias | PENDIENTE | — | — |
| RF-18 | Cierre Diario | Confirmar despachos, volumen despachado, inventario final, diferencias detectadas. Genera acta digital de cierre y reporte PDF | PENDIENTE | — | — |

### Análisis y plataforma

| ID | Requisito | Qué exige el SRS | Estado | Dónde vive | Prueba |
|---|---|---|---|---|---|
| RF-19 | Reportes | Filtrables por fecha, empleado, vehículo, departamento, tipo combustible, estado ticket | PENDIENTE | — | — |
| RF-20 | Exportación | Excel, CSV, PDF | PENDIENTE | — | — |
| RF-21 | Trazabilidad | Auditoría de creaciones, modificaciones, despachos, ajustes, anulaciones y accesos. Incluye usuario, fecha, hora, dirección IP | EN CURSO | Infrastructure/Data/AuditWriter.cs; Endpoints/UserEndpoints.cs | ProductTests: cadena y prohibición SQL; futuro despacho/ajustes pendientes |
| RF-22 | Dashboard Ejecutivo | Inventario actual, combustible despachado, tickets activos, tickets vencidos, consumo por departamento, consumo por vehículo | PENDIENTE | — | — |
| RF-23 | Notificaciones | Alertas automáticas: ticket próximo a vencer, ticket vencido, inventario bajo, fallo de integración, ajustes de inventario | PENDIENTE | — | — |
| RF-24 | API REST | Servicios de generación de tickets, consulta, estado de inventario, despachos, reportes | PENDIENTE | — | — |

---

## Requisitos de seguridad

| ID | Requisito | Qué exige el SRS | Estado | Dónde vive | Prueba |
|---|---|---|---|---|---|
| RS-01 | Autenticación | Usuario/contraseña, MFA opcional, gestión de sesiones | EN CURSO | Endpoints/AuthEndpoints.cs; Security/SessionService.cs | SecurityTests: TOTP, recuperación, bloqueo y sesiones; cobertura disable pendiente |
| RS-02 | Autorización | Control RBAC basado en roles | EN CURSO | Program.cs; políticas admin/catalog-write/audit-read | ProductTests: los cinco roles y acceso anónimo |
| RS-03 | Cifrado | Tránsito: TLS 1.3. Reposo: AES-256 | BLOQUEADO (B-03) para el tránsito en producción | — | — |
| RS-04 | Seguridad de QR | Firma digital, hash SHA-256, token de validación | PENDIENTE | — | — |
| RS-05 | Seguridad de APIs | OAuth 2.0, JWT | EN CURSO | Security/SessionService.cs; Program.cs | JWT alterado/revocado rechazado; OAuth 2.0 aún pendiente |
| RS-06 | Auditoría | Registro **inalterable** de accesos, cambios, despachos y ajustes | EN CURSO | AuditWriter.cs; DatabaseBootstrap.cs | UPDATE/DELETE/TRUNCATE rechazados y hash comprobado; sin protección frente a superusuario |

> **Nota sobre RS-06.** El SRS dice "inalterable". Una tabla normal de base de datos no lo es:
> quien tenga permiso de escritura puede modificarla. Cómo se consigue realmente esa
> inalterabilidad es una decisión de arquitectura pendiente — ver ADR-006 en
> [[Decisiones de arquitectura]]. No se marca `HECHO` con una tabla de auditoría corriente.

---

## Criterios de aceptación del SRS

Los 7 criterios de la sección 7. El proyecto no está terminado mientras uno siga en rojo.

| # | Criterio | Cómo se demuestra | Estado |
|---|---|---|---|
| CA-1 | Se emiten tickets QR únicos sin duplicidad | Prueba de concurrencia: N emisiones en paralelo, cero colisiones de secuencia | PENDIENTE |
| CA-2 | El despacho se valida **exclusivamente** mediante QR válido | Prueba de que no existe ninguna ruta alterna de despacho sin QR verificado | PENDIENTE |
| CA-3 | El inventario se actualiza en tiempo real | Prueba de integración: despacho → el saldo del tanque refleja el cambio en la misma transacción | PENDIENTE |
| CA-4 | Existe trazabilidad completa de las operaciones | Prueba de que toda operación de escritura deja registro de auditoría | PENDIENTE |
| CA-5 | Los reportes son exportables | Prueba de generación de Excel, CSV y PDF con datos reales | PENDIENTE |
| CA-6 | La app móvil opera correctamente en producción | Prueba de extremo a extremo de la PWA en Android real, incluido el escaneo | PENDIENTE |
| CA-7 | Se cumplen los requisitos de seguridad establecidos | RS-01 a RS-06 todos en `HECHO` + revisión de seguridad | PENDIENTE |

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
