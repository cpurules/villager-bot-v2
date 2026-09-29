#!/usr/bin/env bash
# Creates one login role and one database per bot listed in BOT_DATABASES (space-separated, e.g. "villager_bot other_bot").
# Each bot's password comes from <NAME>_DB_PASSWORD, upper-cased (e.g. VILLAGER_BOT_DB_PASSWORD).
# Each database is owned by its bot's role and closed to everyone else, so bots can't read each other's data.
#
# Runs automatically the first time the postgres container starts with an empty data volume.
# Idempotent: after adding a bot to compose.yaml, re-run it with
#   docker compose exec postgres bash /docker-entrypoint-initdb.d/10-provision-bots.sh
#
# No `set -u`: the postgres entrypoint may source this file rather than execute it.
set -eo pipefail

for name in ${BOT_DATABASES:-}; do
  password_var="${name^^}_DB_PASSWORD"
  password="${!password_var:-}"
  if [[ -z "$password" ]]; then
    echo "provision-bots: $password_var is not set for bot database '$name'" >&2
    exit 1
  fi

  echo "provision-bots: ensuring role and database '$name'"
  psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname postgres \
       -v name="$name" -v password="$password" <<'SQL'
SELECT format('CREATE ROLE %I LOGIN PASSWORD %L', :'name', :'password')
 WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = :'name')\gexec
SELECT format('ALTER ROLE %I LOGIN PASSWORD %L', :'name', :'password')\gexec
SELECT format('CREATE DATABASE %I OWNER %I', :'name', :'name')
 WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = :'name')\gexec
SELECT format('REVOKE ALL ON DATABASE %I FROM PUBLIC', :'name')\gexec
SQL
done
