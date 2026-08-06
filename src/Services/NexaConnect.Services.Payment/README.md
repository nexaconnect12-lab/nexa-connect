# Payment Service

Owns payment intents and idempotency keys. `POST` and `GET /api/payment/v1/intents` provide the first payment-intent API. The current adapter is in-memory and intentionally stores no provider or card data; durable PostgreSQL persistence and provider adapters remain follow-up work.
