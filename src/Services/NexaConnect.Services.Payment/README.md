# Payment Service

Owns payment intents and idempotency keys. `POST` and `GET /api/payment/v1/intents` provide the first payment-intent API. Set `Persistence:Provider=PostgreSQL` and `ConnectionStrings:Payment` to use the parameterized PostgreSQL repository; development defaults to the in-memory adapter. `IPaymentProvider` and `HttpPaymentProvider` provide the provider boundary for authorization without accepting raw card data; provider transaction persistence and capture orchestration remain application work.
