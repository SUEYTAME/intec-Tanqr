# Activación de SMS reales — TanQR

Actualizado: 2026-09-30. SMS todavía no está integrado con un gateway.
`OutboxSmsSender` guarda un archivo y devuelve `Outbox`, nunca entrega a un teléfono.
Configurar `SMS_PROVIDER` actualmente lanza una excepción al iniciar: falta implementar
el adaptador. El correo ACS es un canal separado y no resuelve esta tarea.

## Pasos del titular

1. Abrir una cuenta en el proveedor elegido y verificar correo y teléfono propio.
   Twilio es una opción con documentación para República Dominicana; comprobar en
   su consola que la cuenta puede enviar a los números de prueba del país real.
2. Probar primero el flujo guiado SMS de la consola a un teléfono autorizado.
   La prueba actual de Twilio limita país, destinatarios verificados y contenido
   predefinido. Un ticket con enlace personalizado puede requerir actualizar la cuenta.
3. Antes de pagar o comprar un remitente, verificar compatibilidad de ruta/remitente,
   precio y límite de gasto. No asumir que un número local está disponible ni que
   la cuenta gratuita permite el mensaje de TanQR.
4. Tener listo un destinatario de prueba autorizado en formato E.164, el identificador
   de cuenta, credencial de API y remitente válido. Las credenciales se introducen
   directamente en Azure Key Vault; no se pegan en el chat ni en Git.
5. El agente del nuevo chat implementa el adaptador, instala la configuración y prueba
   entrega real. Cerrar RF-09 solo con recepción en el teléfono y evidencia del proveedor.

Fuentes consultadas: [prueba de Twilio](https://www.twilio.com/docs/usage/trials),
[reglas SMS de República Dominicana](https://www.twilio.com/en-us/guidelines/do/sms).

## Prompt para pegar en otro chat de este proyecto

```text
Trabaja en C:\Dev\intec-combustible y lee AGENTS.md, vault/Estado actual del proyecto.md
y vault/Tareas pendientes.md. Necesito completar RF-09: SMS reales para tickets TanQR.
Lee docs/handoff-sms.md. Hoy OutboxSmsSender guarda archivos locales; no hay gateway.
No basta con añadir SMS_PROVIDER: ese valor actualmente impide iniciar la app.

Ayúdame paso a paso a crear/configurar el proveedor (Twilio u otro que sirva para los
números del país real). Verifica documentación oficial vigente, ruta/remitente, trial,
precios y restricciones antes de pedirme pagar. Pregunta el país y teléfono autorizado
de prueba; no inventes destinatarios. No pidas claves por chat: guárdalas en Key Vault.

Implementa el adaptador mínimo, errores y resultado explícitos por canal, y evita
envíos duplicados o afirmar entrega porque existe un archivo Outbox. Conserva QR
firmado, esquema de tickets, RBAC y datos. Distingue aceptación del proveedor de
entrega confirmada; verifica callbacks si el proveedor los utiliza.

Azure existente: suscripción 44f41884-c42a-4162-898f-d83d8d987ff3,
grupo rg-intec-fuel-dev-b805, VM vm-intec-fuel-dev-b805, vault kv-intec-fuel-dev-b805,
URL https://intec-fuel-dev-b805.northcentralus.cloudapp.azure.com.
Usa el despliegue documentado sin SSH, revisión exacta y hash del paquete.
No uses cuentas ajenas ni imprimas secretos o datos personales.

Corre pruebas apropiadas, dotnet test y npm run build/lint; prueba un envío al teléfono
autorizado y confirma recepción. Después commit, push, CI y despliegue Azure verificado.
Actualiza bitácora, estado, backlog y trazabilidad con resultados y límites reales.
Si todavía falta mi alta/pago/credencial, avanza con código y pruebas aisladas, y señala
exactamente qué dato o paso falta. No cierres SMS como hecho sin entrega real.
```
