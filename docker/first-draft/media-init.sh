#!/usr/bin/env bash
set -Eeuo pipefail
run_id="${NEXACONNECT_JOINED_RUN_ID:?}"
[[ "$run_id" =~ ^[a-f0-9]{32}$ ]] || exit 1
database="nexa_review_it_${run_id}_media"
psql -v ON_ERROR_STOP=1 --username "${POSTGRES_USER:-postgres}" --dbname "${POSTGRES_DB:-postgres}" \
  --set=database="$database" --set=password="$NEXACONNECT_JOINED_RUNTIME_PASSWORD" <<'SQL'
SELECT 'CREATE ROLE nexaconnect_media_app LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION'
 WHERE NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname='nexaconnect_media_app') \gexec
SELECT 1 / CASE WHEN NOT rolsuper AND NOT rolcreatedb AND NOT rolcreaterole AND NOT rolreplication THEN 1 ELSE 0 END FROM pg_roles WHERE rolname='nexaconnect_media_app';
ALTER ROLE nexaconnect_media_app PASSWORD :'password';
SELECT format('CREATE DATABASE %I OWNER nexaconnect_migration', :'database')
 WHERE NOT EXISTS(SELECT 1 FROM pg_database WHERE datname=:'database') \gexec
SELECT 1 / CASE WHEN pg_get_userbyid(datdba)='nexaconnect_migration' THEN 1 ELSE 0 END
 FROM pg_database WHERE datname=:'database';
SQL
psql -v ON_ERROR_STOP=1 --username "${POSTGRES_USER:-postgres}" --dbname "$database" --set=database="$database" <<'SQL'
REVOKE CONNECT ON DATABASE :"database" FROM PUBLIC;
GRANT CONNECT ON DATABASE :"database" TO nexaconnect_migration,nexaconnect_media_app;
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
GRANT USAGE ON SCHEMA public TO nexaconnect_media_app;
ALTER DEFAULT PRIVILEGES FOR ROLE nexaconnect_migration IN SCHEMA public GRANT SELECT,INSERT,UPDATE,DELETE ON TABLES TO nexaconnect_media_app;
ALTER DEFAULT PRIVILEGES FOR ROLE nexaconnect_migration IN SCHEMA public GRANT USAGE,SELECT,UPDATE ON SEQUENCES TO nexaconnect_media_app;
SQL
