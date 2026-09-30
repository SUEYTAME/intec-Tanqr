# Verificación previa a la presentación — 2026-09-30

Revisión de código: `5aabf44663531081334c5358b32e6cdf01ef41dc`, rama `fase-2-producto`.
Pedido: confirmar dos errores reportados por compañeros y ejecutar regresión general.
Tres agentes `gpt-6-luna` revisaron reporte, ticket y pruebas generales; Codex también
inspeccionó visualmente los dos PDF originales. No se corrigió código de aplicación.

**Actualización:** la revisión anterior corresponde al diagnóstico inicial. Los
arreglos posteriores y su verificación están al final de esta nota.

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

## Corrección de QA-PDF-01/02 — 2026-09-30

**Commit de código:** `f10045c`. Cambio mínimo en `Documents.cs`:

- Setup nuevo con dimensiones Letter explícitas, vertical para ticket/consumo y
  horizontal para tablas anchas. El ancho de la tabla se calcula a partir del ancho
  efectivo y los márgenes. Se evita clonar las dimensiones A4 del setup predeterminado.
- El PDF incrusta el QR generado por `BitmapByteQRCodeHelper` (portable, sin dependencia
  nueva). PDFsharp no importa el PNG monocromo de 1 bit que genera el helper original.
  Los endpoints PNG y el payload firmado se conservan.

**Pruebas realizadas tras el arreglo:**

- `dotnet build backend -c Release --nologo`: cero warnings/errores.
- `dotnet test backend -c Release --nologo`: **65/65**, cero fallos/omitidas.
- `npm run build` y `npm run lint`: correctos.
- `DocumentTests.cs`: siete nuevos casos. Inspeccionan geometría real del PDF,
  encabezados repetidos y última fila en múltiples páginas (12/10/9/7 columnas),
  acta con detalle, consumo vertical y objeto de imagen incrustado sin mensaje de error.
- Render visual con encabezados reales y 60 filas largas: despachos/tickets 3 páginas,
  movimientos/acta 2 páginas, todas las columnas dentro de la página. Evidencia en
  `artifacts/qa-20260930/report/final-visual/`.
- Ticket firmado sintético de 146 caracteres: firma ECDSA verificada, método real
  `TicketPdf`, render Poppler y decodificación de la página completa por el mismo
  `zxing-wasm` de la aplicación. Payload decodificado idéntico al original. PDF de
  una página con imagen RGB 456 × 456, sin placeholder; evidencia en
  `artifacts/qa-20260930/fix-ticket/signedharness/actual-ticket-fixed.pdf` y `.png`.

**Plan proporcionado:** `C:\Users\proje\Downloads\Plan_de_Pruebas_Tickets_Digitales_INTEC.pdf`,
versión propuesta 1.1, 2026-09-30. Se usa para priorizar el flujo crítico y sus
comprobaciones negativas, sin asumir ejecutados todos sus casos ni adoptar sus
reglas recomendadas como nuevos requisitos.

**Flujo de navegador (`20fb182`):** suite completa **10/10** en una sola base aislada,
Desktop Chrome y Pixel 7 emulado. La nueva prueba recorre recepción → solicitud por
Consulta → aprobación por Supervisor → QR visible y decodificado/descargas → despacho
por Despachador → balance/movimientos → cierre/acta → filtros y CSV/XLSX/PDF.
Comprueba rechazo de exceso autorizado, QR consumido, existencia insuficiente,
cierre duplicado y recepción posterior al cierre, con inventario sin cambios tras
los rechazos. La cédula sintética es única por ejecución/proyecto.
Log final: `artifacts/qa-20260930/overall/workflow-full-01/playwright.log`.
Entorno efímero detenido al finalizar. Muestra de CP-007/011/013/021/034/035/036/053/055;
no se declaran ejecutados los 55 casos completos, correo/SMS externos, cámara física
ni offline. CI `36723232494` de `20fb182` terminó success en los cuatro jobs.
Esa revisión fue desplegada en Azure y los archivos originales se descargaron nuevamente:
ticket con imagen incrustada y QR decodificado idéntico al endpoint; reporte horizontal
con las 12 columnas y cinco registros de los mismos filtros. La revisión visual Linux
detectó palabras largas que todavía excedían el ancho interior de algunas celdas;
se añadió ajuste medido por fuente con puntos de corte invisibles, solo al PDF.
No se altera el contenido de CSV/XLSX ni los datos guardados.

**Portada de la exposición:** el archivo exacto que coincide con la captura es
`C:\Users\proje\Downloads\TanQR (1).pptx` (igual a `TanQR.pptx`, 28 diapositivas).
Su QR decodifica a la URL de la aplicación, que respondió HTTP 200; no es un ticket.
Eso explica el login desde la cámara y el rechazo de formato en Despacho. Se entregó
una copia `C:\Users\proje\Downloads\portada corregida.pptx` con el QR real del ticket
`COM-2026-000008`, estado Enviado verificado, vencimiento 2026-10-03 e instrucción
«Escanéalo desde Despacho». El QR del render final coincide con los 146 caracteres
del PNG original. Las 28 diapositivas se renderizaron; validación de paquete sin
hallazgos tras retirar 27 declaraciones de maestros inexistentes y sin referencias,
heredadas del original. No se ejecutó ni consumió el despacho de ese ticket.

**Alcance de entrega:** los flujos críticos automatizados y los PDF se comprueban;
esto no certifica los 55 casos del plan ni habilita proveedores SMS, configuración
institucional o prueba de cámara en Android físico. La publicación sigue siendo una
demo con datos ficticios. Para una venta con operación real, esas tareas siguen
en `vault/Tareas pendientes.md`.

## Retroalimentación adicional: consumo y entrega

Auditoría de Azure de solo lectura, 2026-09-30: cinco tickets consumidos corresponden
a cinco despachos, sin tickets consumidos sin despacho ni despachos con estado distinto
a Consumido. `COM-2026-000008` está Enviado sin despacho; el número aclarado por el
usuario, `COM-2026-000009`, está Pendiente sin despacho. No aparece en ninguno de los
reportes de consumo/despacho. Validar un QR no lo consume: la transición se escribe al
confirmar el despacho, en la misma transacción que movimiento, inventario y auditoría.

El correo de `COM-2026-000009` sí falló: código SMTP extendido `5.1.4`,
«Recipient address reserved by RFC 2606». El destinatario registrado usa un dominio
reservado `.test`; no es un correo entregable. Los siete intentos de correo fallidos
recientes comparten esa causa. Dos intentos sí fueron aceptados por SMTP.
No se reintentó enviar a una dirección ficticia ni se sustituyó por otra inventada.
El usuario proporcionó y autorizó un correo real. Se actualizó únicamente el correo
de ese empleado mediante PUT con If-Match, conservando los demás campos, y se ejecutó
el reenvío una sola vez. Resultado: Email=Sent, SMS=Outbox, ticket=Sent, dispatch=null.
El PDF nuevo de COM-2026-000009 se renderizó y su QR se decodificó idéntico al PNG
de origen (146 caracteres). SMTP aceptó el mensaje; recepción en bandeja pendiente
de confirmación del destinatario. No se consumió el ticket ni se modificó inventario.

Los nueve SMS recientes tienen resultado `Outbox`, no Sent. No existe adaptador de
gateway: la app guarda archivos locales y lo indica. El usuario pidió un prompt para
resolver el proveedor en otro chat; pasos y handoff en `docs/handoff-sms.md`.

La lista y el detalle de tickets abiertos no se refrescaban al cambiar el estado desde
otra sesión. Se reproduce y corrige esa pantalla por separado del estado persistido;
la base no mostraba una inconsistencia que justificara alterar tickets o inventario.

**Cierre de pruebas:** `93ce2f3` corrige palabras largas; backend **66/66**.
`e43897b` corrige actualización de lista/detalle al recuperar foco/visibilidad y
cada 15 s visibles, más botón Actualizar. CI `36730599453` terminó success:
backend, frontend, navegador y publicación GHCR. La suite local final **10/10**
incluye dos sesiones con detalle/lista abiertos; escanear deja el ticket activo,
confirmar lo consume y ambas vistas se actualizan. Se limpian contextos también
ante fallo de prueba. Un ensayo intermedio perdió los servidores del harness y
dio connection refused; se registró y no se contó como verificación satisfactoria.
El ensayo limpio posterior `ticket-status-full-02` pasó 10/10 en 3.3 min,
sin salida de servidores hasta la limpieza y con puertos/contenedor liberados.

**Datos de presentación:** se observó un nombre de estación DEMO guardado como
`EstaciÃ³n`, y se corrigió solo ese texto por API con If-Match, conservando código,
estado e inventario; rollback local en `artifacts/qa-20260930/`.

**Azure final:** `e43897b0422630018b69143ec6cd1a639af2e4f2`,
imagen `intec-combustible:0.4.0-e43897b`, paquete privado 478858 bytes,
SHA-256 `a7d9c6ec030af1de26dd1d330afa0eaca2f78633e9baf0a9ca7b7e9881731fa9`.
RunCommand terminó 14:48:35 UTC, checksum OK, migraciones actualizadas, PostgreSQL
healthy preservado. Salud externa ready/HTTP 200. Bundle web descargado incluye
refresco por visibilidad e intervalo 15 s. PDF reales descargados tras desplegar:
reporte con cinco filas/12 columnas completas y textos dentro de celdas, estación
DEMO corregida; COM-2026-000008 con QR visible y decodificado idéntico al endpoint.
Evidencia local final en `artifacts/qa-20260930/azure-final/`.

**Revalidación ante aviso de instalador antiguo:** GitHub, rama predeterminada
fase-2-producto, devuelve REVISION=e43897b y su SHA-256 correcto. El commit
93b7635 ya había actualizado instalar.sh. RunCommand de solo lectura vuelve a
confirmar APP_VERSION=0.4.0-e43897b, contenedor con esa imagen, db healthy y
ready. Descarga nueva de COM-2026-000008: imagen incrustada sin placeholder y
QR decodificado idéntico al PNG. Reporte nuevo generado 2026-09-30 10:58
Santo Domingo: cinco filas/12 columnas, render revisado sin recorte derecho.
El aviso sobre 18a1e17 corresponde a un estado anterior; no se necesitó otro
despliegue ni cambio de código. No reutilizar adjuntos PDF descargados antes del arreglo.
