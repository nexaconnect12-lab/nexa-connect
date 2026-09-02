#!/usr/bin/env bash
set -Eeuo pipefail
run_id="${NEXACONNECT_JOINED_RUN_ID:?}"
[[ "$run_id" =~ ^[a-f0-9]{32}$ ]] || { echo 'Invalid joined acceptance run ID.' >&2; exit 1; }

psql --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" \
  --set=run_id="$run_id" \
  --set=migration_password="$NEXACONNECT_JOINED_MIGRATION_PASSWORD" \
  --set=runtime_password="$NEXACONNECT_JOINED_RUNTIME_PASSWORD" <<'SQL'
CREATE ROLE nexaconnect_migration LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION;
ALTER ROLE nexaconnect_migration PASSWORD :'migration_password';
CREATE ROLE platform_directory_app LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION;
CREATE ROLE nexaconnect_restaurant_app LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION;
CREATE ROLE nexaconnect_authorization_app LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION;
CREATE ROLE nexaconnect_order_app LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION;
ALTER ROLE platform_directory_app PASSWORD :'runtime_password';
ALTER ROLE nexaconnect_restaurant_app PASSWORD :'runtime_password';
ALTER ROLE nexaconnect_authorization_app PASSWORD :'runtime_password';
ALTER ROLE nexaconnect_order_app PASSWORD :'runtime_password';
SELECT format('CREATE DATABASE %I OWNER nexaconnect_migration','nexa_review_it_' || :'run_id' || '_platform') \gexec
SELECT format('CREATE DATABASE %I OWNER nexaconnect_migration','nexa_review_it_' || :'run_id' || '_restaurant') \gexec
SELECT format('CREATE DATABASE %I OWNER nexaconnect_migration','nexa_review_it_' || :'run_id' || '_authorization') \gexec
SELECT format('CREATE DATABASE %I OWNER nexaconnect_migration','nexa_review_it_' || :'run_id' || '_order') \gexec
SQL

for suffix_role in 'platform platform_directory_app' 'restaurant nexaconnect_restaurant_app' 'authorization nexaconnect_authorization_app' 'order nexaconnect_order_app'; do
  read -r suffix role <<<"$suffix_role"
  database="nexa_review_it_${run_id}_${suffix}"
  psql --username "$POSTGRES_USER" --dbname "$database" --set=database="$database" --set=role="$role" <<'SQL'
REVOKE CONNECT ON DATABASE :"database" FROM PUBLIC;
GRANT CONNECT ON DATABASE :"database" TO nexaconnect_migration;
GRANT CONNECT ON DATABASE :"database" TO :"role";
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
GRANT USAGE ON SCHEMA public TO :"role";
ALTER DEFAULT PRIVILEGES FOR ROLE nexaconnect_migration IN SCHEMA public GRANT SELECT,INSERT,UPDATE,DELETE ON TABLES TO :"role";
ALTER DEFAULT PRIVILEGES FOR ROLE nexaconnect_migration IN SCHEMA public GRANT USAGE,SELECT,UPDATE ON SEQUENCES TO :"role";
SQL
done
