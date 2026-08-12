# Payment Service

Owns payment intents and idempotency keys. `POST` and `GET /api/payment/v1/intents` provide the first payment-intent API. Set `Persistence:Provider=PostgreSQL` and `ConnectionStrings:Payment` to use the parameterized PostgreSQL repository; development defaults to the in-memory adapter. `IPaymentProvider` and `HttpPaymentProvider` provide the provider boundary for authorization without accepting raw card data; provider transaction persistence and capture orchestration remain application work.

Customer Portal payment-intent calls carry the shared tenant context. Payment validates Platform Directory access, referenced Order ownership, Restaurant scope, and `payment.intent.create` or `payment.intent.read`. Cross-tenant reads return `404`. Configure Platform Directory, Order, Restaurant, Authorization, and workload credentials.
