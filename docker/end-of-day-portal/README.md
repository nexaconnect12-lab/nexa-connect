# Disposable end-of-day portal infrastructure

Use only `scripts/test-end-of-day-portal.ps1 -ConfirmDisposableInfrastructure`. This isolated topology reuses the financial portal realm template and adds the seventh POS-owned database/runtime role. PostgreSQL 17 stores application data in tmpfs; Keycloak 26.7.0 has a separate disposable database; RabbitMQ 4 carries actual retained sale/refund events. All published listeners bind to IPv4 loopback with generated secrets and run-specific identity/database names.

The shared launcher starts eight application children (seven APIs and the Customer BFF), creates fixture state through the guarded test-only tool and requires eight real-OIDC browser passes plus six Authorization persistence cases. It owns exact process handles/project/certificate cleanup and emits bounded evidence. No production topology/schema/grants change. See [runbook and exclusions](../../docs/Deployment/End-Of-Day-Portal-Acceptance.md).
