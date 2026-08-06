# Notification Service

Owns notification requests and delivery status. `POST` and `GET /api/notification/v1/notifications` provide the initial queued-message slice. The current adapter is an in-memory queue; provider delivery, retries, durable outbox consumption, and delivery receipts remain follow-up work.
