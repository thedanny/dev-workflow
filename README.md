# Dev Workflow

Reusable, provider-neutral agent-assisted development platform.

## Goals
- Collect signals from Slack, Jira/support boards, Grafana and CloudWatch with durable checkpoints.
- Evaluate findings every three hours, preserving evidence, decisions and reevaluation history.
- Queue only authorized work for local Claude Code or Codex execution.
- Support GitHub and Bitbucket pull requests; **never automatically merge or deploy**.

## Phase 1: local development queue
Build a .NET 10 API, PostgreSQL persistence, and an MCP gateway. A human launches Claude Code or Codex locally, retrieves approved work, claims it, implements in isolated Git worktrees, tests and opens a PR.

Read [the Phase 1 specification](docs/phase1-spec.md) and [implementation prompt](docs/build-prompt.md).

## Layout
```
src/
  DevWorkflow.Api/
  DevWorkflow.Application/
  DevWorkflow.Domain/
  DevWorkflow.Infrastructure/
  DevWorkflow.Mcp/
tests/
  DevWorkflow.Tests/
docs/
  phase1-spec.md
  build-prompt.md
  architecture.md
```

## Local setup
You need Docker with Compose v2. Building and testing without containers also needs the .NET 10 SDK.

1. Create `.env` with a random database password and one API key per role. The script prints the raw keys once; keep them, because the server stores only their hashes.
   ```sh
   scripts/init-env.sh          # or, on Windows: pwsh scripts/init-env.ps1
   ```
   You can instead copy `.env.example` to `.env` and fill it in by hand.
2. Start PostgreSQL, apply migrations, and run the API and MCP server:
   ```sh
   docker compose up -d --build --wait
   ```
3. Optionally, add the fictional sample data. It contains two repositories (`sample-storefront` on GitHub and `sample-billing` on Bitbucket), signals from Jira, a support board and CloudWatch, and three approved tasks linked to them:
   ```sh
   docker compose run --rm migrate seed
   ```
4. Try it with the executor key:
   ```sh
   curl -H "Authorization: Bearer <executor key>" "http://localhost:5080/api/v1/queue?repository=sample-storefront"
   ```

| Service | Address | Notes |
|---|---|---|
| API | http://localhost:5080/api/v1 | `/health` needs no key |
| MCP | http://localhost:5081/mcp | Streamable HTTP; send the key as a bearer token |
| PostgreSQL | localhost:5432 | Database and user `devworkflow`, password from `.env` |

All ports bind to 127.0.0.1. Logs are JSON on stdout (`docker compose logs -f api`). `docker compose down` stops the stack; add `-v` to delete the database.

Repository provider tokens in `.env` are optional. Without them, pull requests that agents report are stored unverified. With them, the server checks reported pull requests on GitHub or Bitbucket and keeps their status current. Use read-only tokens.

To have Claude Code or Codex work the queue in one of your repositories, install the executor skill and connect the agent to the MCP server as described in [agents/README.md](agents/README.md).

See AGENTS.md for build, test and migration commands.

## Safety
Agents get least-privilege repository access; production credentials and deployment permissions stay outside the development executor. Require explicit human authorization for release.
