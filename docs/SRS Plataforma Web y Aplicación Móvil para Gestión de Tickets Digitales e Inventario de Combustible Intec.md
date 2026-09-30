1 SRS Ticket Digitales, v1.0

SRS Plataforma Web y Aplicación Móvil para Gestión de Tickets Digitales e Inventario de Combustible

Agosto 2026

1. Introducción

1. 1 Propósito

Definir los requisitos funcionales, no funcionales, de seguridad, infraestructura, integración, despliegue, soporte y capacitación necesarios para el desarrollo de una Plataforma Web y Aplicación Móvil destinada al control integral del despacho e inventario de combustible mediante tickets digitales con código QR único y trazabilidad completa.

1. 2 Objetivos del Sistema

La solución permitirá:

- Gestionar solicitudes de combustible.

- Generar tickets digitales únicos con código QR.

- Asignar combustible a empleados y vehículos.

- Controlar inventarios en tiempo real.

- Registrar entradas y salidas de combustible.

- Validar despachos mediante aplicación móvil.

- Mantener trazabilidad completa de operaciones.

- Gestionar cierres diarios.

- Emitir reportes gerenciales.

- Operar bajo esquema multiusuario y multirol.

- Garantizar disponibilidad 24/7.

2. Alcance del Proyecto

El proyecto contempla:

Plataforma Web

Para:

- Administración del sistema

- Gestión de usuarios

- Gestión de Empleados

- Gestión de vehículos

- Gestión de departamentos

- Gestión de inventario

- Configuración de parámetros

2 SRS Ticket Digitales, v1.0

- Creación y emisión de tickets

- Análisis y reportes

Aplicación Móvil

Para:

- Escaneo QR

- Validación de tickets

- Registro de despacho

- Consulta de estado de tickets

- Operación en estación de combustible

Servicios de Integración

- Servicio de generación QR

- Servicio de mensajería SMS

- Servicio de correo electrónico

- API REST unificada

- Servicio de autenticación

3. Actores del Sistema

3. 1 Administrador General

Responsable de:

- Configuración del sistema

- Gestión de usuarios

- Seguridad

- Auditoría

- Control de inventario

3. 2 Supervisor de Combustible

Responsable de:

- Gestión de solicitudes

- Aprobaciones

- Recepciones de combustible

- Ajustes de inventario

3. 3 Despachador

Responsable de:

- Escaneo QR

- Validación de tickets

3 SRS Ticket Digitales, v1.0

- Despacho de combustible

- Cierre diario

3. 4 Solicitante

Responsable de:

- Solicitar combustible

- Consultar estado de tickets

3. 5 Auditor

Responsable de:

- Consultar información

- Exportar reportes

- Revisar trazabilidad

4. Requisitos Funcionales

RF-01 Gestión de Usuarios

El sistema deberá:

- Crear usuarios.

- Modificar usuarios.

- Desactivar usuarios.

- Gestionar perfiles y roles.

- Restablecer contraseñas.

- Aplicar políticas de acceso.

Roles mínimos

- Administrador

- Supervisor

- Despachador

- Auditor

- Consulta

RF-02 Gestión de Empleados

El sistema deberá registrar:

- Código empleado

- Nombre completo

- Cédula

- Departamento

- Cargo

- Correo

4 SRS Ticket Digitales, v1.0

- Teléfono móvil

- Estado

RF-03 Gestión de Vehículos

El sistema deberá registrar:

- Placa

- Ficha (Código interno)

- Marca

- Modelo

- Año

- Tipo

- Departamento

- Capacidad tanque

- Kilómetros (Odómetro)

- Estado

RF-04 Gestión de Departamentos

El sistema deberá:

- Crear departamentos

- Modificar departamentos

- Asociar empleados

- Asociar vehículos

RF-05 Creación de Solicitudes de Combustible

La plataforma permitirá:

- Crear solicitudes manuales.

- Solicitudes automáticas programadas.

- Solicitudes recurrentes.

La solicitud incluirá:

- No. Empleado

- Vehículo

- Departamento

- Cantidad autorizada

- Tipo de combustible

- Fecha solicitud

- Fecha vencimiento

5 SRS Ticket Digitales, v1.0

RF-06 Emisión de Tickets Digitales

La plataforma deberá generar:

Ticket único

Con:

- Identificador único (UUID)

- Secuencia correlativa

- Fecha creación

- Fecha vencimiento

- Vehículo

- Empleado

- Departamento

- Cantidad autorizada

- Tipo combustible

Formato

- PDF

- Correo electrónico

- Código QR

RF-07 Generación de Código QR Seguro

Cada ticket deberá contener:

Datos protegidos mediante hash:

- Ticket ID

- Número secuencial

- Empleado

- Vehículo

- Cantidad combustible

- Fecha emisión

- Fecha expiración

Criterios

- No reutilizable

- No editable

- Único

- Verificación criptográfica

6 SRS Ticket Digitales, v1.0

RF-08 Numeración de Tickets

El sistema deberá:

- Mantener secuencia consecutiva.

- Configurar prefijo.

- Reinicio anual opcional.

- Evitar duplicidad.

Ejemplo:

COM-2026-000001

RF-09 Envío de Tickets

El sistema deberá enviar tickets por:

Correo electrónico

Incluyendo:

- QR

- Datos del ticket

- SMS

Incluyendo:

- Código corto

- URL segura

- QR descargable

RF-10 Consulta de Estado de Ticket

Estados:

- Creado

- Enviado

- Pendiente

- Próximo a vencer

- Vencido

- Consumido

- Anulado

RF-11 Asignaciones Automáticas y Manuales

Permitir:

Manual

7 SRS Ticket Digitales, v1.0

Por usuario autorizado.

Automática

Basada en:

- Programaciones.

- Reglas de negocio.

- Consumo histórico.

RF-12 Despacho de Combustible

El despachador podrá:

- Escanear QR.

- Validar ticket.

- Confirmar identidad.

- Registrar despacho.

Datos registrados:

- Fecha

- Hora

- Galones servidos

- Operador

- Estación

- Observaciones

RF-13 Aplicación Móvil para Despacho

La aplicación móvil deberá permitir:

- Login seguro.

- Escaneo QR.

- Confirmación visual.

- Validación en línea.

- Registro de despacho.

- Consulta de tickets.

- Sincronización inmediata.

RF-14 Control de Inventario

Registrar:

Entradas

- Recepción de combustible

- Compra

8 SRS Ticket Digitales, v1.0

- Transferencias

Salidas

- Despachos

- Mermas

Ajustes

- Positivos

- Negativos

RF-15 Inventario en Tiempo Real

El sistema deberá mostrar:

- Existencia actual.

- Disponibilidad.

- Consumo diario.

- Consumo mensual.

- Nivel crítico.

RF-16 Recepción de Combustible

Registrar:

- RNC

- Nombre Suplidor

- Factura

- Volumen recibido

- Fecha

- Tanque

Impactando inventario automáticamente.

RF-17 Movimientos de Inventario

Historial completo de:

- Entradas

- Salidas

- Ajustes

- Transferencias

RF-18 Cierre Diario

Funcionalidad para:

- Confirmar despachos realizados.

- Volumen despachado.

9 SRS Ticket Digitales, v1.0

- Inventario final.

- Diferencias detectadas.

Generar:

- Acta digital de cierre.

- Reporte PDF.

RF-19 Reportes

La plataforma deberá generar reportes filtrables por:

- Fecha

- Empleado

- Vehículo

- Departamento

- Tipo combustible

- Estado ticket

RF-20 Exportación de Reportes

Formatos:

- Excel

- CSV

- PDF

RF-21 Trazabilidad

Registrar auditoría de:

- Creaciones

- Modificaciones

- Despachos

- Ajustes

- Anulaciones

- Accesos

Incluyendo:

- Usuario

- Fecha

- Hora

- Dirección IP

RF-22 Dashboard Ejecutivo

Visualización de:

10 SRS Ticket Digitales, v1.0

- Inventario actual

- Combustible despachado

- Tickets activos

- Tickets vencidos

- Consumo por departamento

- Consumo por vehículo

RF-23 Notificaciones

Alertas automáticas:

- Ticket próximo a vencer

- Ticket vencido

- Inventario bajo

- Fallo de integración

- Ajustes de inventario

RF-24 API REST

Servicios para:

- Generación de tickets

- Consulta de tickets

- Estado de inventario

- Despachos

- Reportes

5. Requisitos de Seguridad

RS-01 Autenticación

- Usuario/contraseña.

- MFA opcional.

- Gestión de sesiones.

RS-02 Autorización

Control RBAC basado en roles.

RS-03 Cifrado

Datos en tránsito

TLS 1.3

Datos en reposo

AES-256

11 SRS Ticket Digitales, v1.0

RS-04 Seguridad de QR

Los QR deberán contener:

- Firma digital.

- Hash SHA-256.

- Token de validación.

RS-05 Seguridad de APIs

- OAuth 2.0

- JWT

RS-06 Auditoría

Registro inalterable de:

- Accesos

- Cambios

- Despachos

- Ajustes

6. Arquitectura Propuesta

Frontend

- Angular, React o ASP.Net

Backend

- .NET 8 Web API (Entity Framework)

Base de Datos

- PorstgreSQL o SQL Server

Aplicación Móvil

- Flutter Android

- Desarrollo de una PWA All in One

Integraciones

- SMTP

- SMS Gateway

- API REST

Punto de Despacho

12 SRS Ticket Digitales, v1.0

- Smartphone Android

- Conectividad Internet

- Lectores QR opcionales

7. Criterios de Aceptación

La solución será aceptada cuando:

1. Se emitan tickets QR únicos sin duplicidad.

2. El despacho se valide exclusivamente mediante QR válido.

3. El inventario se actualice en tiempo real.

4. Exista trazabilidad completa de las operaciones.

5. Los reportes sean exportables.

6. La aplicación móvil opere correctamente en producción.

7. Se cumplan los requisitos de seguridad establecidos.