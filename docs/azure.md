# Publicación en Azure — INTEC

Actualizado: 2026-09-23. **Infraestructura preparada; aún no desplegada.**
La validación remota fue rechazada porque Microsoft requiere autenticación MFA nueva.
No hay todavía una URL pública operativa.

## Cuenta y alcance autorizados

El usuario autorizó crear lo necesario exclusivamente en su cuenta de INTEC:

- Cuenta: `1128305@est.intec.edu.do`.
- Suscripción: `Azure for Students`, `44f41884-c42a-4162-898f-d83d8d987ff3`.
- Tenant: `6856181f-daf8-4725-ac51-dd9f7dfe2f2b`.
- Grupo previsto: `rg-intec-fuel-dev-b805`, región `northcentralus`.

Los scripts pasan la suscripción explícitamente: la sesión CLI también tiene una cuenta
de otra organización que no está autorizada para este despliegue.

## Diseño y costo

`infra/main.bicep` crea Ubuntu 24.04 en `Standard_B2als_v2` (2 CPU, 4 GiB), disco SSD
Standard de 64 GiB, IP pública estática, red/NSG, Key Vault con RBAC e identidad administrada
y almacenamiento Blob privado para respaldos y archivos de despliegue.

Se conserva el contenedor existente con Kestrel TLS 1.3 directo y PostgreSQL 17 en Docker.
La base no publica puertos. HTTPS usa 443; 80 queda para validación ACME; SSH se limita a
la IP de administración detectada. No se han creado contraseñas de aplicación en Azure.
La clave SSH local vive en `artifacts/azure/`, excluido de Git.

Capacidad comprobada: 6 núcleos regionales, 0 usados; 10 núcleos Basv2, 0 usados;
3 IP públicas Standard, 0 usadas. B1ms y B2s están restringidas para esta suscripción en
las cinco regiones permitidas; B2als_v2 está disponible en `northcentralus`.

Estimación consultada en [Azure Retail Prices API](https://prices.azure.com/api/retail/prices):

| Concepto | USD/mes |
|---|---:|
| VM, 730 horas | 27.448 |
| Disco y 1 millón de operaciones estimadas | 5.000 |
| IPv4 estática, 730 horas | 3.650 |
| Blob 5 GB y operaciones estimadas | 0.158 |
| Key Vault, 10 mil operaciones | 0.030 |
| **Total aproximado** | **36.29** |

No incluye impuestos, tráfico saliente adicional ni correo/SMS. El crédito restante de la
cuenta no se ha consultado. Una sola VM no ofrece alta disponibilidad.

## Continuar

1. Autenticarse con MFA en el tenant indicado: `az login --tenant 6856181f-daf8-4725-ac51-dd9f7dfe2f2b --use-device-code`.
2. Ejecutar `./scripts/azure-infra.ps1` para revisar what-if. **Debe pasar antes de desplegar.**
3. Ejecutar `./scripts/azure-infra.ps1 -Deploy` y guardar los outputs en `artifacts/azure/`.
4. Completar la instalación de la aplicación, secretos en Key Vault, certificado ACME y
   renovación, inicialización de PostgreSQL y correo. Usar la imagen de la revisión probada.
5. Configurar respaldo diario privado fuera de la VM y probar una restauración.
6. Verificar salud, login real, rechazo TLS 1.2, aceptación TLS 1.3, interfaz/PWA y aislamiento
   de la base antes de afirmar que el sitio está disponible.

Los pasos 3–6 **no se han ejecutado**. El diseño de correo mediante Azure Communication
Services está investigado, pero no se han creado servicios de correo ni aplicaciones Entra.
SMS y la prueba en Android físico siguen pendientes. No se inventarán datos reales.
