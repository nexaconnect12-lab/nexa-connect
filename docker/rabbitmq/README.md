# RabbitMQ

The local `rabbitmq:4-management` container enables `rabbitmq_management` and `rabbitmq_prometheus` through the checked-in `enabled_plugins` file. AMQP (`127.0.0.1:5672`), the management UI (`127.0.0.1:15672`), and Prometheus metrics (`127.0.0.1:15692/metrics`) are bound to loopback and are development-only endpoints.

Prometheus owns scraping the internal `rabbitmq:15692` endpoint and evaluates the checked-in reconciliation queue/dead-letter alerts. Do not expose these unauthenticated local ports publicly or use the local guest identity in production. Production deployments require TLS, separately scoped publisher/consumer/monitoring credentials, reviewed network policy, durable queue topology, and environment-owned alert routing.
