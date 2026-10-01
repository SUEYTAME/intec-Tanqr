# Despliegue automático GitHub → Azure VM

Configurado 2026-10-01 por solicitud del titular. ADR-021.

## Flujo

Push a **fase-2-producto** → backend/pruebas de despliegue, frontend y navegador
→ imagen publicada en GHCR → imagen exacta y código transferidos al Blob privado
→ respaldo PostgreSQL → carga de imagen CI en la VM → inicialización/migraciones
→ recreación de app → validación de revisión, PostgreSQL y salud interna/externa.

`.github/workflows/ci.yml` conserva toda la ejecución en una cola con
`cancel-in-progress: false`, `queue: max` (hasta 100 pendientes). Una ejecución
activa termina antes de la siguiente del mismo grupo. GitHub ordena por entrada
en la cola; la VM rechaza un `run_number` menor al último publicado para evitar
que una ejecución retrasada revierta una versión más reciente.

Pull requests y ramas distintas ejecutan pruebas; no acceden a Azure ni publican
en la VM. `workflow_dispatch` en fase-2-producto permite repetir el flujo completo.
Los mensajes `[skip ci]` omiten el workflow según las reglas de GitHub: no usarlos
para cambios que necesiten desplegarse.

## Acceso ya configurado

No hay client secret, contraseña de Azure ni token GHCR permanente en la VM.
Azure Login usa OIDC; la VM conserva su identidad administrada para Blob/Key Vault.
Aplicación Entra `intec-tanqr-github-deploy`:

- Client ID: `c439c314-d887-49bc-807d-d66a33634116`.
- App object ID: `a78b8f14-8ef2-4519-a112-8fe78e621c54`.
- Service principal: `1e007bac-df8d-4373-9998-34d9e65e6e0c`.
- Federación: `infra/github-deploy-federation.json`, solo la rama indicada del repo.
- Rol custom `INTEC TanQR GitHub VM Deploy` (definición en
  `infra/github-deploy-role.json`), asignado únicamente a la VM.
- `Storage Blob Data Contributor`, únicamente en el contenedor `deployments`.

Variables del repositorio: `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`,
`AZURE_SUBSCRIPTION_ID`. Son identificadores, no secretos. `id-token: write`
solo se concede a los jobs container y deploy. Run Command ejecuta como root
en la VM: quien pueda cambiar el workflow de la rama de publicación controla
ese despliegue y debe ser un colaborador autorizado.

## Barreras y recuperación

Checksum SHA-256 de ambos paquetes; label OCI de commit completo; respaldo
terminado antes de cambiar la aplicación. La imagen cargada es la misma que
construyó CI; no se recompila en Azure. `flock` agrega un bloqueo local en la VM.
Run Command puede responder con éxito aunque el shell falle: el job exige el
marcador `TANQR_DEPLOY_OK:<commit>` después de todas las verificaciones.

Cada release guarda env/compose anteriores en
`/opt/combustible/releases/<commit>/`, acceso root. Un error del instalador o de
las verificaciones intenta restaurar configuración y app anteriores y comprobar
salud (`ROLLBACK_OK` / `ROLLBACK_FAILED`). El job queda fallido incluso si la
recuperación funciona. **No restaura automáticamente la base**: podría borrar
operaciones concurrentes. Una migración incompatible requiere intervención y
evaluar el respaldo; este flujo no promete rollback de esquema.

Última revisión aceptada: `/opt/combustible/deployed-revision`; orden publicado:
`/opt/combustible/deploy-run-number`. Logs en Actions y
`/var/log/combustible-instalar.log`. El disco mantiene imágenes anteriores para
rollback; supervisar espacio si se acumulan muchas publicaciones.

## Verificación

Pruebas locales: backend 122/122, build/lint correctos; seis escenarios aislados
en `scripts/test-azure-deploy.sh` (éxito, rollback, respaldo fallido, versión vieja,
parámetros inválidos y falso éxito de Run Command). Las mismas pruebas del
despliegue se ejecutan en CI antes de publicar la imagen.

Sintaxis bash correcta; actionlint 1.7.12 sin errores excepto su falta de soporte
para `queue`, propiedad documentada actualmente por GitHub. Ese diagnóstico
concreto se excluyó del lint local; la primera ejecución GitHub valida el workflow.

Fuentes oficiales:
[cola de concurrencia](https://docs.github.com/en/actions/how-tos/write-workflows/choose-when-workflows-run/control-workflow-concurrency),
[Azure Login OIDC](https://github.com/Azure/login),
[Run Command](https://learn.microsoft.com/en-us/azure/virtual-machines/linux/run-command).


## Primera activación

GitHub aceptó la propiedad queue:max y arrancó la ejecución
[36938991352](https://github.com/SUEYTAME/intec-Tanqr/actions/runs/36938991352)
para 175b052. Este registro de operación se publica con un segundo push para
comprobar que espera en cola sin cancelar el primer despliegue. Resultado final
en la bitácora del proyecto.

Para mantenimiento: GitHub → Actions → CI → Disable workflow; no inicia nuevos
trabajos. Esperar que termine el activo antes de ejecutar actualizaciones manuales.
Para reintentar un fallo: Re-run failed jobs o Run workflow en fase-2-producto.
Un commit con pruebas fallidas no llega al job deploy.
