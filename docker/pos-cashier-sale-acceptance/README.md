# POS cashier-sale acceptance infrastructure

This Compose project is owned exclusively by `scripts/test-pos-cashier-sale.ps1`. The launcher generates a unique project name and secrets, binds PostgreSQL and RabbitMQ to random loopback ports, stores PostgreSQL on tmpfs, verifies the project is initially empty, and removes its containers and volumes in `finally`.

Run the launcher with PowerShell 7 after restoring the repository's .NET dependencies. It accepts only the local Docker `default` or `desktop-linux` context through a recognized Docker Desktop named pipe or local Unix socket and rejects an explicit `DOCKER_HOST`.

The automated stage runs the protected POS recovery/projection matrix plus Order receipt, authorization, and migration acceptance. It does not authenticate a human, drive WPF, or open a printer dialog. Complete those boundaries against the supervised real service stack and run `scripts/verify-pos-cashier-live-acceptance.ps1` with every explicit confirmation switch. Neither stage certifies production infrastructure.

Do not start this Compose file directly. Never reuse its generated credentials or connection strings.
