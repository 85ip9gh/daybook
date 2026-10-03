#!/usr/bin/env bash
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
k() { k3s kubectl --namespace daybook "$@"; }

k scale deployment/api --replicas=0
k wait --for=delete pod --selector app.kubernetes.io/name=daybook-api --timeout=120s 2>/dev/null || true
k rollout status statefulset/db --timeout=300s

k exec -i statefulset/db -- sh -c \
  '/opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -b -v app_password="$LEDGER_APP_PASSWORD"' \
  < "$here/recreate-database.sql"

k scale deployment/api --replicas=1
k rollout status deployment/api --timeout=300s

k delete job seed --ignore-not-found --wait=true
k apply --filename "$here/seed-job.yaml"
k wait --for=condition=complete job/seed --timeout=300s
k logs job/seed
