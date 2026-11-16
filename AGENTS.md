# Agent guide

Read [docs/architecture.md](docs/architecture.md) first.

- `src/`: the .NET 10 backend in layers (Domain ← Application ← Infrastructure ← Server). Business rules go on the
  Domain's aggregates, not in services, the hub or the grains.
- `client/`: the Babylon.js client (TypeScript, Vite).
- `tests/`: NUnit 4, NSubstitute for test doubles. Unit tests go in the test project of the layer they test; anything
  touching a real database or the hosted server goes in `Pspsps.Integration.Test` (Testcontainers PostgreSQL).
- `charts/pspsps`: the Helm chart.

Rules:

- Every change comes with tests.
- Entity Framework Core for all database access. A model change needs a new migration; never edit one on master.
- Keep this repository free of anything about where it is deployed (hosts, addresses, private repositories).
