# Verificación previa a la presentación — 2026-09-30

Revisión de código: `5aabf44663531081334c5358b32e6cdf01ef41dc`, rama `fase-2-producto`.
Pedido: confirmar dos errores reportados por compañeros y ejecutar regresión general.
Tres agentes `gpt-6-luna` revisaron reporte, ticket y pruebas generales; Codex también
inspeccionó visualmente los dos PDF originales. No se corrigió código de aplicación.

## Defectos confirmados

### QA-PDF-01 — Tabla de despachos recortada

- Fuente exacta: `C:\Users\proje\Downloads\despachos-202609301150.pdf`.
- SHA-256: `5E1EFC95963EB3B4A837AF4FA78BCF9E18472FE8C4A6409F0CAF767F50D3EAB1`.
- La tabla sale por el borde derecho de la página; no es solo un problema del visor.
- Reproducido con datos ficticios mediante `DocumentRenderer.TablePdf` del código actual.
- Evidencia local: `artifacts/qa-20260930/report/despachos-1.png` y
  `artifacts/qa-20260930/report/harness/report-current-source.pdf`.
- Revisar `backend/src/Combustible.Infrastructure/Documents/Documents.cs`,
  `NewDocument`, `TablePdf` y `AddGrid`: formato/orientación y ancho útil deben coincidir
  con las dimensiones efectivas de página.
- Causa comprobada en la salida: se asignan **24.94 cm** a las tablas de más de seis
  columnas, pero el PDF emitido conserva **A4 vertical (595.276 × 841.89 puntos)**.
  Aunque el código solicita Letter/Horizontal, la página efectiva no coincide.
- Alcance reproducido: despachos (12 columnas), tickets (10), movimientos (9) y
  detalle del acta de cierre (7). Consumo (3 columnas) cabe en la página.
- Prioridad: corregir antes de mostrar/exportar reportes en la presentación.

### QA-PDF-02 — QR ausente en ticket PDF

- Fuente exacta: `C:\Users\proje\Downloads\COM-2026-000008.pdf`.
- SHA-256: `5AF8EE19AA58845D3C959AAD1C2C32159FF70AF8E668289C8D4BCEBE93091B19`.
- El área del QR contiene un recuadro gris con texto rojo `Image has no valid type.`.
- El PDF original no contiene recursos de imagen `/XObject`; el QR no está incrustado.
- Reproducido con `DocumentRenderer.TicketPdf` y datos ficticios, sin emitir un ticket
  real ni enviar correo. El PDF generado reproduce el mismo mensaje.
- Evidencia local: `artifacts/qa-20260930/ticket/provided-ticket.png` y
  `artifacts/qa-20260930/ticket/harness/actual-current-generator.png`.
- Revisar `Documents.cs:40`: incorporación de PNG generado por `QrPng` a MigraDoc.
- Prioridad: corregir antes de presentar el ticket impreso/PDF como medio de despacho.

## Brecha de cobertura

`FuelTests.cs:141,527,559,609` comprueba la cabecera `%PDF` de los documentos.
Eso valida que existe un archivo PDF, pero no comprueba su apariencia, sus límites
de página ni que el QR esté incrustado y pueda decodificarse.

Las pruebas Playwright existentes cubren catálogos, MFA, navegación de los cinco
roles y carga de pantallas/reportes en escritorio y Pixel 7 emulado. No equivalen
a un recorrido completo de solicitud → aprobación → despacho → cierre mediante UI,
ni a una prueba de cámara en Android físico.

## Regresión ejecutada

- `dotnet test backend -c Release --nologo`: **58/58**, cero fallos y cero omitidas.
- `npm run build`: correcto.
- `npm run lint`: correcto.
- Playwright completo: **8/8**, cuatro pruebas en Desktop Chrome y cuatro en Pixel 7
  emulado (catálogos, roles, pantallas/reportes y MFA). Resultado limpio en
  `artifacts/qa-20260930/overall/attempt-02/playwright.log`.
- Logs locales: `artifacts/qa-20260930/overall/` (ignorados por Git).

La ejecución de navegador usó PostgreSQL efímero en puerto 15433 y API/Vite locales.
El primer lanzador temporal falló por compatibilidad con PowerShell y vida de procesos
desacoplados; produjo fallos de conexión que no se contaron como defectos del producto.
La repetición completa con API/Vite como hijos del lanzador en primer plano pasó 8/8.
No se modificó el lanzador del proyecto ni se probaron nuevas escrituras en Azure.

Los resultados aprobados de regresión no cierran los dos defectos de PDF.
La revisión no certifica una aplicación sin defectos ni una nueva versión de Azure.

## Observación adicional

En algunas celdas del reporte original se ve texto mal codificado, por ejemplo
`EstaciÃ³n`. Los encabezados sí muestran acentos correctamente y el reporte con
datos ficticios nuevos los conserva: el origen del problema en esos datos DEMO
no quedó determinado. Revisarlo antes de presentar esos registros.
