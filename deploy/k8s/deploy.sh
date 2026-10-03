#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "$0")/../.." && pwd)"
manifests="$root/deploy/k8s"
env_file="${DAYBOOK_ENV:-$HOME/daybook/.env}"
k() { sudo k3s kubectl "$@"; }

for key in MSSQL_SA_PASSWORD LEDGER_JWT_KEY LEDGER_ADMIN_PASSWORD LEDGER_APP_PASSWORD LEDGER_DEMO_PASSWORD; do
  grep -q "^$key=." "$env_file" || { echo "$key is missing from $env_file" >&2; exit 1; }
done
demo_password="$(grep '^LEDGER_DEMO_PASSWORD=' "$env_file" | cut -d= -f2-)"

docker build --file "$root/src/Ledger.Api/Dockerfile" --tag daybook-api:g7 "$root"
docker build --tag daybook-web:g7 "$root/web"
docker save daybook-api:g7 daybook-web:g7 | sudo k3s ctr images import -

k apply --filename "$manifests/namespace.yaml"
k --namespace daybook create secret generic daybook --from-env-file="$env_file" --dry-run=client --output=yaml | k apply --filename -
sed "s/__DEMO_PASSWORD__/$demo_password/" "$manifests/web-config.json" \
  | k --namespace daybook create configmap web-config --from-file=config.json=/dev/stdin --dry-run=client --output=yaml \
  | k apply --filename -
k --namespace daybook create configmap seed-script --from-file="$root/tools/seed-demo.mjs" --dry-run=client --output=yaml | k apply --filename -

for manifest in networkpolicy db api web ingress; do
  k apply --filename "$manifests/$manifest.yaml"
done

k --namespace daybook rollout restart deployment/web deployment/api
k --namespace daybook rollout status deployment/web --timeout=120s
