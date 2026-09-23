# Informe final — Plataforma de Tickets Digitales de Combustible (INTEC)

Fecha: 2026-09-22 · Versión 0.4.0 · Rama `fase-2-producto`.
Fuente de requisitos: `docs/SRS.pdf` (RF-01..RF-24, RS-01..RS-06, CA-1..CA-7).

## 1. Resumen

El sistema está **funcionalmente completo**: los 24 requisitos funcionales y los 6 de seguridad
tienen implementación y prueba automatizada. **No está en producción** porque faltan recursos
institucionales que el código no puede suplir: SMTP (B-02), pasarela SMS (B-01), dominio y
certificado (B-03), datos reales (B-04). El único criterio de aceptación sin cumplir es **CA-6**
(prueba en un Android físico), que necesita el despliegue con certificado real.

Esos recursos corresponden a la operación institucional real. Para una demostración se
pueden usar datos ficticios y un buzón de prueba, identificados como tales. INTEC es el
contexto del SRS; no es necesario que la institución provea infraestructura para alojar
una demo. Falta acordar su destino de alojamiento. El código y el contenedor publicados
en GitHub son entregables, no un servicio web ya desplegado.

## 2. Arquitectura

- **Backend:** .NET 10 LTS, API mínima, EF Core 10 + PostgreSQL 17, ASP.NET Identity, OpenIddict 7.7.1.
- **Frontend:** React 19 + TypeScript + Vite 8; PWA instalable (vite-plugin-pwa) para el despacho.
- **Despliegue:** un contenedor de un solo origen; Kestrel termina TLS 1.3 y sirve API e interfaz.
- Decisiones y alternativas descartadas: `vault/Decisiones de arquitectura.md` (ADR-001..013).

## 3. Cobertura de requisitos

Detalle por requisito, con archivo y prueba: `vault/Trazabilidad de requisitos del SRS.md`.

| Grupo | Estado |
|---|---|
| RF-01..RF-04 usuarios y catálogos | Hecho |
| RF-05..RF-11 solicitudes, tickets, QR, numeración, envío, estados, asignaciones | Hecho; SMS solo a bandeja local (B-01) |
| RF-12..RF-18 despacho, PWA, inventario, recepción, movimientos, cierre | Hecho |
| RF-19..RF-24 reportes, exportación, trazabilidad, panel, alertas, API | Hecho |
| RS-01 autenticación (MFA) · RS-02 RBAC | Hecho |
| RS-03 cifrado | AES-256-GCM en reposo y TLS 1.3 hechos; certificado real pendiente (B-03) |
| RS-04 QR firmado · RS-05 OAuth 2.0/JWT · RS-06 auditoría inalterable | Hecho |

| Criterio | Evidencia |
|---|---|
| CA-1 tickets únicos | `Emision_concurrente_no_duplica_ni_salta_numeros` |
| CA-2 despacho solo con QR | `Solo_existe_una_ruta_de_despacho_y_la_base_impide_atajos` |
| CA-3 inventario en tiempo real | `Despacho_atomico_...`, `Despachos_concurrentes_no_sobregiran_el_tanque` |
| CA-4 trazabilidad completa | auditoría en la misma transacción de cada escritura; pruebas de seguridad |
| CA-5 reportes exportables | `Reportes_filtran_y_exportan_excel_csv_y_pdf` |
| CA-6 PWA en producción | **Pendiente**: requiere Android físico + HTTPS real |
| CA-7 seguridad | Pruebas RS-01..RS-06 + revisión manual (sección 5) |

## 4. Verificación ejecutada (2026-09-22)

| Qué | Resultado |
|---|---|
| `dotnet test backend -c Release` (PostgreSQL 17 real, Testcontainers) | **52/52** |
| `npm run build`, `oxlint` | OK, 0 avisos |
| Playwright, escritorio + Pixel 7 emulado (`scripts/probar-interfaz.ps1`) | **6/6** |
| Imagen Docker construida y ejecutada | init OK; TLS 1.3 aceptado, 1.2 rechazado; CSP; SPA, service worker, manifiesto y WASM servidos |
| Carga: 50 usuarios concurrentes × 30 s (`scripts/prueba-carga.mjs`) | 7 552 solicitudes, 250,6 rps, **0 errores**; p95 270–400 ms en listados, 1,05 s en el panel |
| `npm audit` / `dotnet list package --vulnerable` | 0 vulnerabilidades |

Límite de la prueba de carga: base casi vacía. Mide estabilidad y concurrencia, no rendimiento
con años de datos. Repetirla con volumen realista antes de producción.

## 5. Revisión de seguridad (CA-7)

Sin hallazgos abiertos de severidad alta. Controles verificados: QR firmado ECDSA y rechazo de
alterados, reutilizados y vencidos; enlace público con token de 128 bits, comparación en tiempo
constante y límite de tasa; contraseñas con PBKDF2 de 600 000 iteraciones, bloqueo 5 intentos /
15 min, MFA; tokens solo en memoria; rol SQL de aplicación sin UPDATE/DELETE sobre auditoría ni
historial; ancla firmada contra reconstrucción por superusuario; cifrado de datos personales;
TLS 1.3; CSP de un solo origen, `no-store`, `nosniff`, anti-framing; sin `innerHTML`/`eval`.

**Decisión pendiente del responsable:** un cliente OAuth con rol Supervisor puede hoy aprobar
solicitudes, anular tickets y escribir catálogos e inventario (solo despacho y cierre exigen
sesión humana). Si las integraciones deben ser de solo lectura, se restringe con un cambio
pequeño en las políticas.

## 6. Para pasar a producción

1. Entregar B-02 (SMTP), B-03 (dominio + certificado) y B-04 (datos reales); B-01 si se requiere SMS.
2. Seguir `docs/despliegue.md`.
3. Ejecutar CA-6 en un Android físico contra ese despliegue.
4. Repetir la prueba de carga con volumen realista.
5. Capacitar con `docs/manual-usuario.md` (incluye ejercicios por rol).
