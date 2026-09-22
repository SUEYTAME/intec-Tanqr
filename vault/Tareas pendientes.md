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

## Fase 1 — Dominio y autenticación (activa)

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
- [ ] Integración OAuth 2.0/OIDC (RS-05); JWT local no cierra este requisito
- [x] Interfaz para desactivar MFA y gestionar recuperación después del alta
- [ ] Revisar rutas de cambios de acceso para que todas exijan versión y probar auditoría alterada
- [ ] Revisión de configuración y secretos de producción antes de desplegar
- [x] CI ampliada de Fase 1: run `35764488194` correcto para `70972c7`; verificar cada commit posterior

---

## Fase 2 — Tickets y QR

Cubre RF-05 a RF-11, RS-04. Es el núcleo del sistema.

- [ ] Modelo de Solicitud y Ticket con los 7 estados de RF-10
- [ ] Secuencia de numeración sin duplicados bajo concurrencia (RF-08, CA-1)
- [ ] Firma criptográfica del QR y verificación (RF-07, RS-04)
- [ ] Generación del PDF del ticket (RF-06)
- [ ] `IEmailSender` con implementación de desarrollo a disco (RF-06) — **[!] B-02 para producción**
- [ ] `ISmsSender` con implementación de desarrollo a disco (RF-09) — **[!] B-01 para producción**
- [ ] Solicitudes automáticas y recurrentes (RF-05, RF-11)
- [ ] Transiciones de estado y vencimiento automático (RF-10) — **necesita cerrar H-03**
- [ ] Pruebas: QR alterado se rechaza, QR reusado se rechaza, QR vencido se rechaza

---

## Fase 3 — Inventario y despacho

Cubre RF-12 a RF-18, CA-2, CA-3.

- [ ] Modelo de Tanque, Movimiento de inventario y Despacho — **necesita cerrar H-01**
- [ ] Despacho atómico: validar QR, descontar inventario y auditar en una sola transacción (CA-3)
- [ ] Regla de cantidad despachada contra autorizada — **necesita cerrar H-02 y H-04**
- [ ] Recepción de combustible con RNC, suplidor y factura (RF-16)
- [ ] Ajustes positivos y negativos con motivo obligatorio (RF-14)
- [ ] Cierre diario con acta digital y PDF (RF-18)
- [ ] Prueba de que no existe ruta de despacho que no pase por verificación de QR (CA-2)

---

## Fase 4 — PWA de despacho

Cubre RF-13, CA-6.

- [ ] Login y sesión en la PWA (RF-13)
- [ ] Escaneo de QR con la API de cámara del navegador
- [ ] Confirmación visual del ticket antes de despachar
- [ ] Registro de despacho y sincronización (RF-13)
- [ ] Service worker e instalabilidad
- [ ] Comportamiento sin conexión — **necesita cerrar H-07**
- [ ] Prueba de extremo a extremo en un Android real (CA-6)

---

## Fase 5 — Reportes, dashboard y notificaciones

Cubre RF-19, RF-20, RF-22, RF-23, CA-5.

- [ ] Consultas de reportes con los 6 filtros de RF-19
- [ ] Exportación a Excel, CSV y PDF (RF-20, CA-5)
- [ ] Dashboard ejecutivo con las 6 métricas de RF-22
- [ ] Motor de notificaciones con las 5 alertas de RF-23
- [ ] Trabajos programados de vencimiento e inventario bajo

---

## Fase 6 — Endurecimiento y entrega

Cubre RS-03, CA-7 y el cierre.

- [ ] Cifrado en reposo AES-256 de los campos sensibles (RS-03)
- [ ] TLS 1.3 en producción — **[!] B-03**
- [ ] Revisión de seguridad completa (CA-7)
- [ ] Pruebas de carga contra el requisito de 24/7
- [ ] Documentación de despliegue y manual de usuario
- [ ] Capacitación, que el SRS menciona en su propósito

---

## Reglas del backlog

- Una tarea marcada `[x]` significa que **se corrió la prueba y pasó**, no que se escribió el código.
- Si descubres trabajo que falta, añádelo aquí en el momento. Un hallazgo que no se escribe se pierde.
- Si una tarea está `[!]` bloqueada, no la rodees inventando un sustituto: sigue a la siguiente
  y deja constancia del bloqueo en [[Estado actual del proyecto]].
