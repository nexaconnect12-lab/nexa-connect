#!/usr/bin/env bash
set -Eeuo pipefail

required_variables=(
    NEXACONNECT_MIGRATION_PASSWORD
    PLATFORM_DIRECTORY_DB_PASSWORD
    RESTAURANT_DB_PASSWORD
    CATALOG_DB_PASSWORD
    INVENTORY_DB_PASSWORD
    ORDER_DB_PASSWORD
    KITCHEN_DB_PASSWORD
    CUSTOMER_DB_PASSWORD
    PAYMENT_DB_PASSWORD
    POS_DB_PASSWORD
    MEDIA_DB_PASSWORD
    REPORTING_DB_PASSWORD
    AUTHORIZATION_DB_PASSWORD
)

for variable_name in "${required_variables[@]}"; do
    if [[ -z "${!variable_name:-}" ]]; then
        echo "Required environment variable ${variable_name} is not set." >&2
        exit 1
    fi
done

migration_role="nexaconnect_migration"

ensure_login_role() {
    local role_name="$1"
    local role_password="$2"

    NEXACONNECT_ROLE_NAME="$role_name" NEXACONNECT_ROLE_PASSWORD="$role_password" \
        psql --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<'SQL'
\getenv role_name NEXACONNECT_ROLE_NAME
\getenv role_password NEXACONNECT_ROLE_PASSWORD
SELECT format('CREATE ROLE %I LOGIN', :'role_name')
WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = :'role_name')
\gexec
ALTER ROLE :"role_name" WITH LOGIN PASSWORD :'role_password' NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION;
SQL
}

ensure_database() {
    local database_name="$1"

    if [[ "$(psql --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" --tuples-only --no-align \
        --command="SELECT 1 FROM pg_database WHERE datname = '${database_name}'")" != "1" ]]; then
        createdb --username "$POSTGRES_USER" --owner "$migration_role" "$database_name"
    fi
}

configure_database_access() {
    local database_name="$1"
    local application_role="$2"

    psql --username "$POSTGRES_USER" --dbname "$database_name" \
        --set=database_name="$database_name" \
        --set=migration_role="$migration_role" \
        --set=application_role="$application_role" <<'SQL'
REVOKE CONNECT ON DATABASE :"database_name" FROM PUBLIC;
GRANT CONNECT ON DATABASE :"database_name" TO :"migration_role";
GRANT CONNECT ON DATABASE :"database_name" TO :"application_role";

REVOKE CREATE ON SCHEMA public FROM PUBLIC;
GRANT USAGE ON SCHEMA public TO :"application_role";

GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO :"application_role";
GRANT USAGE, SELECT, UPDATE ON ALL SEQUENCES IN SCHEMA public TO :"application_role";

ALTER DEFAULT PRIVILEGES FOR ROLE :"migration_role" IN SCHEMA public
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO :"application_role";
ALTER DEFAULT PRIVILEGES FOR ROLE :"migration_role" IN SCHEMA public
    GRANT USAGE, SELECT, UPDATE ON SEQUENCES TO :"application_role";
SQL
}

provision_service_database() {
    local database_name="$1"
    local application_role="$2"
    local application_password="$3"

    ensure_login_role "$application_role" "$application_password"
    ensure_database "$database_name"
    configure_database_access "$database_name" "$application_role"
}

ensure_login_role "$migration_role" "$NEXACONNECT_MIGRATION_PASSWORD"

provision_service_database "PlatformDirectory" "platform_directory_app" "$PLATFORM_DIRECTORY_DB_PASSWORD"
provision_service_database "NexaConnect_Restaurant" "nexaconnect_restaurant_app" "$RESTAURANT_DB_PASSWORD"
provision_service_database "NexaConnect_Catalog" "nexaconnect_catalog_app" "$CATALOG_DB_PASSWORD"
provision_service_database "NexaConnect_Inventory" "nexaconnect_inventory_app" "$INVENTORY_DB_PASSWORD"
provision_service_database "NexaConnect_Order" "nexaconnect_order_app" "$ORDER_DB_PASSWORD"
provision_service_database "NexaConnect_Kitchen" "nexaconnect_kitchen_app" "$KITCHEN_DB_PASSWORD"
provision_service_database "NexaConnect_Customer" "nexaconnect_customer_app" "$CUSTOMER_DB_PASSWORD"
provision_service_database "NexaConnect_Payment" "nexaconnect_payment_app" "$PAYMENT_DB_PASSWORD"
provision_service_database "NexaConnect_POS" "nexaconnect_pos_app" "$POS_DB_PASSWORD"
provision_service_database "NexaConnect_Media" "nexaconnect_media_app" "$MEDIA_DB_PASSWORD"
provision_service_database "NexaConnect_Reporting" "nexaconnect_reporting_app" "$REPORTING_DB_PASSWORD"
provision_service_database "NexaConnect_Authorization" "nexaconnect_authorization_app" "$AUTHORIZATION_DB_PASSWORD"

echo "NexaConnect PostgreSQL databases and runtime roles provisioned."
