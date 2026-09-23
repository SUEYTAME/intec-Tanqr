---
tipo: proyecto
estado: activo
actualizado: 2026-09-22
---

# Tareas pendientes

Backlog por fases. **Una fase no empieza hasta que la anterior está verificada**, porque cada
una depende de contratos que fija la anterior.

Cómo se usa: toma la primera tarea sin marcar de la fase activa cuyas dependencias estén
cumplidas. Al terminarla, márcala aquí, anota en [[Bitacora de cambios]] y actualiza
[[Trazabilidad de requisitos del SRS]] si cerró un RF o RS.

Notación: `[ ]` pendiente · `[~]` en curso · `[x]` hecho y verificado · `[!]` bloqueada.

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
- [ ] Revisión de configuración y secretos de producción antes de desplegar — **[!] B-03**
- [x] CI ampliada de Fase 1: run `35764488194` correcto para `70972c7`. **La rama `fase-2-producto` aún no se ha empujado ni pasado por CI.**

---

## Fases 2 a 5 — Tickets, inventario, despacho, PWA, reportes (código completo 2026-09-22, rama `fase-2-producto`)

Evidencia: backend 51/51 (`dotnet test backend -c Release`), Playwright 6/6. Detalle en [[Bitacora de cambios]].

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
- [ ] Decidir si un cliente OAuth con rol Supervisor puede aprobar/anular/escribir inventario (hoy puede; ver bitácora)

---

## Fase 6 — Endurecimiento y entrega

- [x] Cifrado AES-256-GCM de cédula/correo/móvil con índice ciego (RS-03, ADR-010)
- [x] Kestrel solo TLS 1.3 (`Kestrel_rechaza_TLS_1_2_y_negocia_TLS_1_3`) — certificado real **[!] B-03**
- [x] Revisión de seguridad CA-7 (manual, 2026-09-22): sin vulnerabilidades en dependencias; un punto de decisión abierto
- [ ] Empujar `fase-2-producto` y verificar CI remota
- [x] Despliegue de un solo origen (Kestrel TLS 1.3 + CSP), Dockerfile y compose de producción probados (`1c05ca7`)
- [x] Prueba de carga básica: 250 rps, 0 errores (base casi vacía; repetir con volumen real)
- [x] Manual de usuario con ejercicios de capacitación (`docs/manual-usuario.md`)
- [x] Informe final (`docs/informe-final.md`)

---

## Reglas del backlog

- Una tarea marcada `[x]` significa que **se corrió la prueba y pasó**, no que se escribió el código.
- Si descubres trabajo que falta, añádelo aquí en el momento. Un hallazgo que no se escribe se pierde.
- Si una tarea está `[!]` bloqueada, no la rodees inventando un sustituto: sigue a la siguiente
  y deja constancia del bloqueo en [[Estado actual del proyecto]].
