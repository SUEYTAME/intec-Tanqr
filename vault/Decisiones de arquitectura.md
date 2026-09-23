---
tipo: proyecto
estado: activo
actualizado: 2026-09-22
---

# Decisiones de arquitectura

Cada decisión con su porqué y su fecha. **Antes de proponer arquitectura, lee las secciones
`DESCARTADO`**: guardan caminos ya evaluados y por qué se cayeron. Reproponer uno hace perder
el tiempo a dos agentes y a una persona.

Una decisión aquí no se cambia en una conversación suelta: se cambia escribiendo un ADR nuevo
que supersede al anterior, con la evidencia nueva que lo justifica.

---

## ADR-001 — Backend en .NET 10 LTS, no .NET 8

**Fecha:** 2026-09-22 · **Estado:** aceptada · **Desvía del SRS:** sí, conscientemente

**Contexto.** El SRS especifica ".NET 8 Web API (Entity Framework)". El índice oficial de
releases de Microsoft, consultado el 2026-09-22, dice:

| Canal | Tipo | Fase | Fin de soporte |
|---|---|---|---|
| 10.0 | LTS | active | 2028-11-14 |
| 9.0 | STS | maintenance | 2026-11-10 |
| 8.0 | LTS | maintenance | 2026-11-10 |

**Decisión.** .NET 10 LTS (SDK 10.0.401, instalado y verificado el 2026-09-22).

**Por qué.** El SRS fija como objetivo "garantizar disponibilidad 24/7". Nacer sobre un runtime
que deja de recibir parches de seguridad el 2026-11-10 —siete semanas después de esta fecha—
contradice ese objetivo. Un sistema que maneja inventario de combustible y registros de
auditoría no debe correr sobre una plataforma sin soporte.

**Coste de la desviación.** Si quien evalúe el SRS exige literalidad, esta decisión hay que
defenderla. La defensa es esta nota con las fechas oficiales. .NET 10 es compatible hacia atrás
con el código .NET 8 en lo que este proyecto necesita: EF Core, Minimal APIs y el stack de
autenticación no tienen rupturas que nos afecten.

**Cómo se revierte.** Cambiar el TargetFramework a net8.0 e instalar el SDK 8. Coste bajo,
porque no se usará ninguna API exclusiva de .NET 10 sin anotarlo aquí.

---

## ADR-002 — PostgreSQL 17 en Docker, no SQL Server

**Fecha:** 2026-09-22 · **Estado:** aceptada · **Desvía del SRS:** no, el SRS admite ambos

**Contexto.** El SRS dice "PostgreSQL o SQL Server". En la máquina hay Docker 29.6.1 y no hay
cliente psql ni instancia de SQL Server.

**Decisión.** PostgreSQL 17 en contenedor, definido en docker-compose.yml.

**Por qué.** Es la primera opción que lista el SRS. En contenedor, el entorno de desarrollo es
reproducible y no ensucia la máquina. Además tiene lo que la Fase 3 va a necesitar:
transacciones serializables reales para el despacho atómico y tipos numéricos exactos para
volúmenes de combustible.

**Advertencia para quien implemente.** Los volúmenes de combustible van en numeric/decimal,
**nunca** en coma flotante. Un double acumula error en cada suma y al cabo de un mes el
inventario no cuadra. Esto no es teórico: es la causa clásica de descuadres de inventario.

---

## ADR-003 — Frontend React + TypeScript + Vite

**Fecha:** 2026-09-22 · **Estado:** aceptada · **Desvía del SRS:** no, el SRS admite React

**Contexto.** El SRS admite "Angular, React o ASP.Net". El usuario tiene como defaults globales
npm y TypeScript sobre JavaScript. Node v24.15.0 y npm 11.12.1 están instalados.

**Decisión.** React con TypeScript, construido con Vite.

**Por qué.** Está permitido por el SRS y coincide con las preferencias ya establecidas del
usuario. Vite además da el camino más corto a la PWA del ADR-004 sin una segunda base de código.

---

## ADR-004 — PWA instalable en vez de Flutter Android

**Fecha:** 2026-09-22 · **Estado:** aceptada · **Desvía del SRS:** no, el SRS ofrece ambas

**Contexto.** El SRS lista "Flutter Android" y, en la línea siguiente, "Desarrollo de una PWA
All in One". Flutter no está instalado en la máquina.

**Decisión.** PWA instalable, compartiendo base de código con el frontend web.

**Por qué.** Una sola base de código en vez de dos (React/TS y Dart). El escaneo de QR se
resuelve con la API de cámara del navegador. Se instala en Android sin pasar por el ciclo de
revisión de la Play Store, lo que importa cuando hay que corregir algo en una estación de
combustible el mismo día.

**Riesgo asumido y cómo se vigila.** El escaneo por navegador puede rendir peor que el nativo
con poca luz o con QR sucios o arrugados — condiciones reales en una estación. La tarea CA-6
de la Fase 4 es una prueba en un Android real precisamente para medirlo. **Si esa prueba falla,
este ADR se supersede y se va a Flutter.** No se da por bueno sin medirlo.

---

## ADR-005 — Los servicios externos se consumen tras una interfaz, siempre

**Fecha:** 2026-09-22 · **Estado:** aceptada

**Contexto.** RF-09 exige envío por SMS y correo. Hoy no hay cuenta de pasarela SMS (bloqueo
B-01) ni credenciales SMTP (B-02). Un agente no puede crear cuentas ni introducir datos de pago,
así que estos bloqueos solo los levanta el usuario.

**Decisión.** Todo servicio externo se consume detrás de una interfaz (IEmailSender, ISmsSender,
IPdfRenderer). En desarrollo se registra una implementación que escribe el mensaje a disco en
artifacts/outbox/.

**Por qué.** El bloqueo deja de detener el desarrollo: la Fase 2 se construye y se prueba entera
sin credenciales, y el día que lleguen solo cambia el registro de dependencias.

**Lo que esta decisión NO autoriza.** La implementación de desarrollo **nunca** devuelve "enviado
con éxito" simulando un envío real. Escribe a disco y lo reporta como tal. Un falso éxito en el
envío de un ticket significa que un empleado se presenta en la estación con un ticket que nunca
recibió.

---

## ADR-006 — Cómo se consigue la auditoría "inalterable" de RS-06

**Fecha:** 2026-09-22 · **Estado:** ABIERTA — hay que decidirla antes de la Fase 1

**Contexto.** RS-06 exige "registro inalterable de accesos, cambios, despachos y ajustes". Una
tabla corriente de PostgreSQL no es inalterable: quien tenga permiso puede hacer UPDATE o DELETE
sobre ella sin dejar rastro. Marcar RS-06 como hecho con una tabla normal sería faltar a la
verdad.

**Opciones sobre la mesa, ninguna elegida todavía:**

1. **Encadenamiento por hash.** Cada fila guarda el hash de la anterior. Alterar una rompe la
   cadena y se detecta. Barato de implementar; detecta la manipulación pero no la impide.
2. **Permisos a nivel de base de datos.** El usuario de la aplicación solo tiene INSERT sobre la
   tabla de auditoría; ni UPDATE ni DELETE. Impide la manipulación desde la aplicación, no desde
   un superusuario.
3. **Destino externo de solo anexado.** Los eventos se escriben además a un almacén fuera de la
   base. Es lo más fuerte y lo más caro de operar.

**Recomendación de partida:** combinar 1 y 2. Juntas dan prevención y detección con un coste
razonable. **Requiere confirmación del usuario** antes de implementarse.

---

## DESCARTADO

Caminos evaluados que **no** se toman. No reproponerlos sin evidencia nueva.

### DESCARTADO — .NET 9 porque ya estaba instalado

Evaluado el 2026-09-22. Ahorra la instalación, pero el canal 9.0 está en maintenance con fin de
soporte el 2026-11-10, exactamente la misma fecha que .NET 8, y además es STS. Se ahorraban
cinco minutos de instalación a cambio de heredar el mismo problema de soporte. No compensa.

### DESCARTADO — Flutter como aplicación de despacho en la primera versión

Evaluado el 2026-09-22. Ver ADR-004. Implica una segunda base de código en Dart, instalar el
SDK, y el ciclo de publicación de la Play Store. **Puede volver a la mesa** si la prueba CA-6
demuestra que el escaneo por navegador no aguanta las condiciones reales de la estación. Ese es
el único camino de regreso: con la medición, no con la intuición.

### DESCARTADO — Coma flotante para volúmenes de combustible

Evaluado el 2026-09-22. float y double acumulan error de redondeo en cada operación. En un
sistema cuyo criterio de aceptación CA-3 es que el inventario cuadre en tiempo real, eso produce
descuadres que nadie sabe explicar. Se usa numeric/decimal sin excepción.

### DESCARTADO — Confiar en el contenido del QR sin verificar la firma

Evaluado el 2026-09-22. Un QR es texto: cualquiera imprime uno con la cantidad que quiera. RF-07
exige "verificación criptográfica" y CA-2 exige que el despacho se valide exclusivamente por QR
válido. El servidor verifica la firma **siempre**, y la cantidad autorizada se lee de la base de
datos, nunca del contenido del QR. El QR solo transporta un identificador y su firma.

---

## ADR-007 — Decisiones delegadas para desbloquear el producto

**Fecha:** 2026-09-22. **Estado:** aceptada por delegación explícita del usuario
("créalos tú mismo, te delego ese trabajo" y continuar). No describe datos reales de INTEC.
Supersede la exigencia de respuesta individual para H-01..H-07 y resuelve ADR-006.

- H-01: estaciones con N tanques configurables; cada tanque tiene combustible y capacidad.
  No se inventa el número real ni se siembran instalaciones de INTEC.
- H-02/H-04: un ticket permite un único despacho, mayor que cero y menor o igual a lo
  autorizado. Un despacho menor consume el ticket completo; se registra diferencia y motivo.
  Nunca se permite excedente ni segundo uso; una nueva necesidad requiere otro ticket.
- H-03: valor inicial configurable de 7 días de vigencia y alerta 24 horas antes.
  Fechas en UTC; presentación y cierre en America/Santo_Domingo. El vencimiento concreto
  se persiste al emitir, no cambia retroactivamente al modificar parámetros.
- H-05: operador confirma visualmente documento de identidad y coincidencia con empleado;
  registrar confirmación y operador, sin almacenar foto del documento ni firma biométrica.
- H-06: frases de 15 a 128 caracteres, espacios y pegado permitidos, sin caducidad periódica
  ni reglas de mezcla de caracteres. Bloquear contraseñas comunes/contextuales; 5 fallos
  bloquean 15 minutos. Reset y cambio de permisos invalidan sesiones. No guardar contraseñas
  antiguas. Referencia: https://pages.nist.gov/800-63-4/sp800-63b.html.
- H-07: el despacho requiere conexión y confirmación del servidor; no encolar consumos
  offline. La PWA puede mostrar su interfaz sin red, pero no confirmar un despacho ficticio.
- ADR-006: combinar cadena SHA-256 con usuario SQL de aplicación sin UPDATE/DELETE/TRUNCATE
  sobre auditoría. Mismo commit/transacción que la operación. Un administrador de base de
  datos sigue siendo de confianza: no afirmar resistencia frente al superusuario ni cerrar
  RS-06 como protección externa WORM. Exportación/anclaje externo permanece pendiente.

Acceso sencillo significa un arranque local reproducible y una interfaz clara; no eliminar
RBAC ni publicar PostgreSQL. Credenciales locales generadas criptográficamente en `.env`
ignorado. Repositorio privado creado en la cuenta GitHub autenticada del usuario.

## ADR-008 — Persistencia y autenticación de la primera entrega

**Fecha:** 2026-09-22. **Estado:** aceptada por delegación. EF Core + Npgsql 10 y
ASP.NET Core Identity para usuarios, hashes, bloqueo y TOTP. Roles del SRS exactos.
Migraciones explícitas; la API normal no migra ni corre con el propietario SQL.
CRUD con validación, claves únicas y concurrencia optimista. Bajas lógicas conservan historia.
Auditoría omite contraseñas, tokens y valores de cédula; registra entidad, operación y actor.

JWT de acceso corto (15 min) y sesión revocable; refresh aleatorio de un solo uso, guardado
como SHA-256, vigencia absoluta 8 horas. Cliente conserva tokens solo en memoria en esta
primera entrega. Login local no equivale a OAuth 2.0: RS-05 queda parcial hasta integrar
un proveedor OAuth/OIDC con Authorization Code + PKCE. No usar un password grant como
sustituto de OAuth. No hay registro público; alta de usuarios solo Administrador.

La delegación permite continuar desarrollo con validación local mientras CI externa se
resuelve; ninguna ejecución remota fallida se etiqueta como exitosa.

## ADR-009 — Estados del ticket y semántica de entrega

**Fecha:** 2026-09-22. Estados: Created, Sent, Pending, NearExpiry, Expired, Consumed, Voided.
La entrega (correo/SMS) ocurre **después** del commit del ticket (ADR-005): una falla de SMTP
nunca revierte la emisión. `Pending` = entrega fallida o solo en bandeja local. La bandeja
local (outbox) **nunca** cuenta como enviada. SMS es solo outbox hasta resolver B-01.
Numeración sin huecos con fila contador (`INSERT … ON CONFLICT DO UPDATE … RETURNING`), no
SEQUENCE, porque una SEQUENCE deja huecos al revertir.

## ADR-010 — Cifrado de datos personales del empleado (RS-03 en reposo)

**Fecha:** 2026-09-22. Cédula, correo y móvil se guardan con AES-256-GCM por campo
(prefijo `v1:`, propósito como datos asociados, subclaves HKDF). La unicidad de cédula usa
un índice ciego HMAC-SHA256 (`NationalIdHash`). La clave es `DATA_ENCRYPTION_KEY` (32 bytes).
Los registros anteriores se cifran al inicializar (`encrypt_backfill`, auditado).
**Advertencia:** el `Down` de la migración no descifra; revertirla deja datos cifrados en
columnas que la versión anterior lee como texto. Perder la clave = perder esos datos.

## ADR-011 — OAuth 2.0 client credentials para integraciones (reemplaza la redacción de ADR-008)

**Fecha:** 2026-09-22. RS-05 se cumple con OpenIddict 7.7.1 emitiendo JWT `at+jwt` por
client credentials a sistemas externos, cada cliente atado a un rol (Supervisor, Auditor o
Consulta; nunca Administrador ni Despachador). Los usuarios interactivos conservan el login
propio con MFA (ADR-008); Authorization Code + PKCE queda **DESCARTADO por ahora**: no hay
proveedor de identidad institucional confirmado (B-03/B-04). Despacho y cierre exigen `sid`
(sesión humana), así que ningún cliente OAuth puede despachar ni cerrar.

## ADR-012 — Ancla externa de la auditoría (RS-06)

**Fecha:** 2026-09-22. `GET /api/auditoria/ancla` firma (ECDSA, dominio `AUDIT-ANCHOR-v1`)
el último id, hash y conteo de la cadena. Guardada fuera del sistema (correo, papel, WORM),
`POST /api/auditoria/ancla/verificar` detecta que un superusuario reconstruyó o truncó la
cadena. La interfaz de Auditoría la descarga en JSON.

## ADR-013 — Entorno local: Windows PowerShell 5.1 y Mailpit

**Fecha:** 2026-09-22. La máquina solo tiene Windows PowerShell 5.1: los scripts evitan
APIs de PowerShell 7 y usan `Invoke-Native` para ejecutables que escriben en stderr.
El correo local va a Mailpit (SMTP 127.0.0.1:11025, UI 18025) hasta tener el SMTP
institucional (B-02). La PWA nunca guarda respuestas de la API (NetworkOnly, H-07).
