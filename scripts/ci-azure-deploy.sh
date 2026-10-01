#!/usr/bin/env bash
set -euo pipefail
: "${AZURE_SUBSCRIPTION_ID:?}" "${DEPLOY_REVISION:?}" "${SRC_SHA256:?}" "${IMAGE_SHA256:?}" "${GITHUB_RUN_NUMBER:?}"
[[ "$DEPLOY_REVISION" =~ ^[0-9a-f]{40}$ && "$SRC_SHA256" =~ ^[0-9a-f]{64}$ \
  && "$IMAGE_SHA256" =~ ^[0-9a-f]{64}$ && "$GITHUB_RUN_NUMBER" =~ ^[1-9][0-9]*$ ]] \
  || { echo 'Parámetros de CI inválidos'; exit 1; }
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
export DEPLOY_SCRIPT="$work/deploy.sh"
python3 - <<'PY'
import os, pathlib, base64, shlex
installer=base64.b64encode(pathlib.Path('deploy/azure/instalar.sh').read_bytes()).decode()
params={'REVISION':os.environ['DEPLOY_REVISION'], 'SRC_SHA256':os.environ['SRC_SHA256'],
        'IMAGE_SHA256':os.environ['IMAGE_SHA256'], 'DEPLOY_RUN_NUMBER':os.environ['GITHUB_RUN_NUMBER'],
        'INSTALLER_B64':installer}
wrapper=pathlib.Path('deploy/azure/actualizar-ci.sh').read_text()
script='#!/usr/bin/env bash\n'+''.join(f'export {k}={shlex.quote(v)}\n' for k,v in params.items())+wrapper
pathlib.Path(os.environ['DEPLOY_SCRIPT']).write_text(script)
PY
az vm run-command invoke --subscription "$AZURE_SUBSCRIPTION_ID" \
  -g rg-intec-fuel-dev-b805 -n vm-intec-fuel-dev-b805 --command-id RunShellScript \
  --scripts "@$DEPLOY_SCRIPT" -o json > "$work/result.json"
python3 - "$work/result.json" <<'PY'
import json, os, sys
result=json.load(open(sys.argv[1]))
messages='\n'.join(item.get('message','') for item in result.get('value',[]))
print(messages)
marker='TANQR_DEPLOY_OK:'+os.environ['DEPLOY_REVISION']
if marker not in messages.splitlines():
    raise SystemExit('La VM no confirmó la revisión solicitada. Despliegue fallido; revisar log/rollback.')
PY
curl --fail --silent --show-error --retry 5 --retry-delay 5 \
  https://intec-fuel-dev-b805.northcentralus.cloudapp.azure.com/health/ready
printf '\nAzure publicado: `%s` — salud externa correcta.\n' "$DEPLOY_REVISION" >> "$GITHUB_STEP_SUMMARY"
