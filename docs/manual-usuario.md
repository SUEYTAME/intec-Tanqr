# Manual de usuario — Plataforma de tickets de combustible INTEC

Versión 0.4.0 · 2026-09-22. Sirve también como guía de capacitación: cada sección termina con
un ejercicio para practicar en un entorno de prueba (nunca con datos reales).

## 1. Conceptos

- **Solicitud:** pedido de combustible para un empleado y un vehículo. Puede ser manual,
  programada o recurrente.
- **Ticket:** autorización aprobada, con número correlativo (p. ej. `COM-2026-000001`), cantidad
  autorizada, vencimiento y un **código QR firmado**. El QR no se puede falsificar ni reutilizar.
- **Estados del ticket:** Creado · Enviado · Pendiente de entrega · Próximo a vencer · Vencido ·
  Consumido · Anulado. Solo los cuatro primeros se pueden despachar.
- **Despacho:** entrega del combustible en la estación, siempre leyendo el QR del ticket.
- **Todo queda auditado:** quién hizo qué, cuándo y desde qué IP. No se borra nada.

## 2. Entrar al sistema

1. Abrir la dirección institucional del sistema.
2. Escribir correo y contraseña. Si tienes la autenticación en dos pasos activa, abre
   *Usar código de autenticación* y escribe el código de 6 dígitos del autenticador.
3. La sesión se cierra al recargar o cerrar la pestaña (por seguridad).

**Mi seguridad:** activar la autenticación en dos pasos (recomendada; imprescindible para
administradores), guardar los códigos de recuperación y cambiar la contraseña.

## 3. Roles y qué ve cada uno

| Rol | Puede |
|---|---|
| Administrador | Usuarios, parámetros, integraciones, catálogos, solicitudes, tickets, inventario, reportes, auditoría. **No despacha.** |
| Supervisor | Catálogos, aprobar solicitudes, anular tickets, inventario, programaciones, despacho y cierre, reportes |
| Despachador | Despacho y cierre diario; consulta de tickets e inventario |
| Auditor | Reportes y auditoría (incluida el ancla firmada); solo lectura |
| Consulta | Crear solicitudes, ver información y reportes |

El menú solo muestra lo que tu rol puede usar.

## 4. Solicitudes y tickets (Supervisor, Administrador, Consulta)

1. *Solicitudes → Nueva solicitud*: elegir empleado, vehículo, departamento, combustible y
   cantidad. El sistema rechaza cantidades mayores que la capacidad del tanque del vehículo y
   respeta el máximo de tickets activos por vehículo.
2. Un Supervisor o Administrador **aprueba** (se emite el ticket) o **rechaza** con motivo.
3. Al emitirse, el ticket se envía por correo al empleado con su QR y un enlace seguro.
   Si el envío falla, el ticket queda *Pendiente de entrega* y el sistema reintenta; también se
   puede **reenviar** desde *Tickets*.
4. *Tickets*: buscar por número o persona, ver el detalle, descargar el PDF, ver el QR o
   **anular** con motivo (queda en auditoría).

*Programaciones:* crear solicitudes automáticas (una vez, diaria, semanal o mensual) con cantidad
fija o el promedio de los despachos anteriores; opcionalmente con aprobación automática.

**Ejercicio:** crea una solicitud de prueba, apruébala y localiza el ticket emitido en *Tickets*.

## 5. El empleado y su ticket

El empleado recibe un correo con el QR y un enlace. Al abrir el enlace ve su ticket, el QR para
mostrar en la estación y un botón para descargar el PDF. Si el ticket ya no es válido, no se
muestra el QR.

## 6. Despacho (Despachador, Supervisor) — en el teléfono

Instalar la aplicación: abrir el sistema en Chrome del teléfono → menú → *Instalar aplicación*.

1. *Despacho → Escanear con la cámara* y apuntar al QR. Alternativas: lector externo (escribe el
   código en el campo) o *Cargar foto del QR*.
2. El sistema muestra el ticket: nombre del empleado, últimos 4 dígitos de la cédula, vehículo,
   combustible y cantidad autorizada. **Verifica la identidad** y marca la casilla de confirmación.
3. Elegir el tanque, escribir los galones servidos (y el odómetro si aplica). Si la cantidad
   difiere de la autorizada, escribir el motivo.
4. *Confirmar despacho*. El inventario del tanque se descuenta en el mismo instante y el ticket
   queda *Consumido*.

**Sin conexión no se despacha.** Aparece un aviso rojo y nada se guarda para enviarlo después:
espera a tener conexión y repite. Un ticket rechazado muestra el motivo (vencido, consumido,
anulado, alterado).

**Ejercicio:** despacha un ticket de prueba y luego intenta despacharlo otra vez: el sistema
debe rechazarlo como consumido.

## 7. Inventario (Supervisor, Administrador)

- *Inventario → Existencias*: saldo, comprometido y disponible por tanque; nivel crítico marcado.
- **Recepción:** suplidor, RNC (9 u 11 dígitos), factura, tanque y volumen. Suma al inventario.
- **Transferencia** entre tanques del mismo combustible.
- **Ajuste** positivo, negativo o merma, siempre con motivo (genera alerta).
- *Movimientos*: historial completo; no se puede editar ni borrar.
- Pestañas de catálogo para combustibles, estaciones y tanques.

## 8. Cierre diario (Despachador, Supervisor)

1. *Cierre diario*: elegir estación y día → ver el resumen (despachos, volumen, saldo teórico).
2. Escribir la **medición física** de cada tanque; el sistema calcula las diferencias.
3. Confirmar. Se genera el acta en PDF y el día queda **bloqueado**: no se aceptan más
   movimientos con esa fecha en esa estación.

## 9. Reportes (Administrador, Supervisor, Auditor, Consulta)

Elegir el tipo (Tickets, Despachos, Movimientos de inventario, Consumo), el periodo y los filtros
(empleado, vehículo, departamento, combustible, estado). *Ver reporte* muestra hasta 500 filas;
*Exportar CSV / XLSX / PDF* descarga todo (hasta 20 000 filas). Cada exportación queda auditada.

## 10. Panel y notificaciones

- *Tablero:* inventario por tanque, despachado hoy y en el mes, tickets activos, próximos a
  vencer, vencidos, solicitudes pendientes y consumo por departamento y vehículo.
- *Notificaciones:* tickets por vencer o vencidos, inventario bajo, ajustes, fallas de entrega
  o de programaciones. El número entre paréntesis en el menú indica las no leídas.

## 11. Administración (Administrador)

- *Usuarios:* crear, asignar rol, activar/desactivar y restablecer contraseña.
- *Parámetros:* prefijo y reinicio anual de la numeración, días de vigencia, horas de aviso y
  tickets activos por vehículo.
- *Integraciones:* credenciales OAuth 2.0 para otros sistemas. El secreto se muestra **una sola
  vez**; revocar corta el acceso de inmediato.
- *Auditoría:* bitácora completa; *Descargar ancla firmada* y guardarla fuera del sistema cada semana.

## 12. Problemas frecuentes

| Síntoma | Causa y solución |
|---|---|
| "Demasiados intentos. Espera un minuto." | Protección contra ataques de contraseña. Esperar un minuto. |
| La cámara no abre | Dar permiso de cámara al sitio; el sistema debe abrirse por HTTPS. Usar *Cargar foto del QR* como alternativa. |
| "El registro cambió; vuelve a cargarlo." | Otra persona modificó el mismo dato. Recargar y repetir. |
| Ticket *Pendiente de entrega* | El correo no salió. Usar *Reenviar*; revisar la notificación de falla de integración. |
| No se puede registrar un movimiento | El día ya tiene cierre en esa estación. |
