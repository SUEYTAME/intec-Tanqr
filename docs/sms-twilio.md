# SMS con Twilio — RF-09

Actualizado: 2026-09-30.

El adaptador usa la API REST de Twilio con Basic auth y formulario `To`, `Body` y
exactamente uno de `From` / `MessagingServiceSid`. No añade paquetes NuGet.
`Sent` significa aceptado por Twilio (`queued`), no confirma recepción en el teléfono.
Fallos se guardan como `Failed` y generan la alerta IntegrationFailure existente.
No hay sustitución silenciosa por Outbox si Twilio falla.

## Configuración

En `.env` ignorado por Git, completar `SMS_PROVIDER=twilio`, `TWILIO_ACCOUNT_SID`,
`TWILIO_AUTH_TOKEN` y `TWILIO_FROM` en E.164, o `TWILIO_MESSAGING_SERVICE_SID`.
`scripts/config-local.ps1` conserva y carga todas las claves. Reiniciar con
`./scripts/iniciar.ps1 -Reiniciar` después de configurar.
El número local de diez dígitos solo se normaliza para los prefijos dominicanos
809/829/849; otros países requieren `+` y su código internacional.

Para Azure, `./scripts/azure-sms.ps1` carga las cinco variables desde `.env` al
Key Vault INTEC usando archivos temporales eliminados, sin imprimir valores.
El instalador lee secretos opcionales: solo HTTP 404 admite vacío; otros errores
detienen el despliegue. La cuenta y VM existentes son suficientes.

## Evidencia y bloqueo real

- Backend: `dotnet test backend -c Release --nologo`, 82/82, incluye 16 casos SMS.
- Frontend: `npm run build` y `npm run lint`, correctos.
- Cuenta Twilio consultada por API: Trial, active. El listado IncomingPhoneNumbers
  no mostró números; OutgoingCallerIds no confirmó el destino indicado por el usuario.
  Estos listados no prueban la validez de un número asignado por el nuevo flujo Trial.
- Envío único autorizado de texto de prueba, sin ticket ni consumo: rechazado por
  Twilio con **572006: Invalid template name. Trial accounts can only use predefined SMS templates.**
  No se obtuvo Message SID ni se afirmó recepción.
- Los cinco secretos Twilio se guardaron en Key Vault; sus valores y números privados
  no se documentan aquí.

El plan adjunto suponía las restricciones antiguas de la cuenta de prueba. La
[documentación actual de Trial](https://www.twilio.com/docs/usage/trials) exige
plantillas predefinidas. Se necesita habilitar mensajes personalizados mediante
Upgrade antes de validar RF-09 real con código y URL de ticket. La facturación de
Twilio queda a cargo del usuario. Después comprobar remitente, permisos geográficos
para República Dominicana y destinatario, y repetir un ticket controlado.

QA-PDF-01/02 ya estaban cerrados y publicados en e43897b antes de este cambio;
no se reabren por la suposición histórica del plan.
