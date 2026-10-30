# AGENTS.md

Guidance for AI coding agents (Claude Code, Codex, and others) working in this repository.

## Current state

Milestones 1 to 4 are done. The solution has a domain model, PostgreSQL persistence, application services, an authenticated HTTP API under `/api/v1`, an MCP server at `/mcp` with the same operations as tools, and GitHub and Bitbucket adapters that verify reported pull requests. Docker Compose, seeding and the local agent skill come next. Before adding features, read in this order:

1. `docs/phase1-spec.md` – the Phase 1 technical spec (core model, lifecycle, API/MCP contract, acceptance criteria)
2. `docs/architecture.md` – runtime separation, approval policy, extensibility
3. `docs/build-prompt.md` – the milestone-ordered implementation handoff

## Commands

```sh
dotnet build DevWorkflow.slnx        # build everything; warnings are errors
dotnet test DevWorkflow.slnx         # run all tests; persistence tests need Docker running
dotnet test DevWorkflow.slnx --filter "FullyQualifiedName~PersistenceTests"   # run one test class
dotnet run --project src/DevWorkflow.Api   # API on http://localhost:5080
dotnet run --project src/DevWorkflow.Mcp   # MCP host on http://localhost:5081

dotnet tool restore                                  # installs the pinned dotnet-ef
dotnet ef migrations add <Name> --project src/DevWorkflow.Infrastructure --startup-project src/DevWorkflow.Infrastructure --output-dir Persistence/Migrations
dotnet ef database update --project src/DevWorkflow.Infrastructure --startup-project src/DevWorkflow.Infrastructure
```

The hosts and `dotnet ef` read the `ConnectionStrings__DevWorkflow` environment variable. In Development they fall back to a local database at `localhost:5432` with database, user and password all `devworkflow`. Add Docker Compose commands here when that milestone lands.

### API keys

Every endpoint except `/health` needs `Authorization: Bearer <key>`. Each key has one role: `Listener`, `Evaluator`, `Executor` or `Approver`. Configuration holds only the SHA-256 hash of each key, never the key itself:

```sh
KEY=$(openssl rand -hex 32)
HASH=$(printf %s "$KEY" | sha256sum | cut -d' ' -f1)
export Authentication__ApiKeys__0__Name=local-executor
export Authentication__ApiKeys__0__Role=Executor
export Authentication__ApiKeys__0__KeyHash=$HASH
```

Both hosts must be given the same key list. With no keys configured, every authenticated request returns 401.

### Repository provider credentials

Optional. Without them, pull request reports are stored unverified. Give both hosts the same values. Tokens should only read repositories, pull requests, checks and reviews. Never use a token that can merge, administer or deploy.

```sh
export RepositoryProviders__GitHub__Token=<token>             # every GitHub repository
export RepositoryProviders__GitHub__ApiBaseUrl=<url>          # optional, e.g. GitHub Enterprise Server
export RepositoryProviders__Bitbucket__Token=<token>          # Bitbucket Cloud access token (bearer)
export RepositoryProviders__Bitbucket__Username=<user>        # optional: send the token with basic auth
export RepositoryProviders__Credentials__0__Repository=<key>  # a single repository's own credential,
export RepositoryProviders__Credentials__0__Token=<token>     # which takes precedence
```

The API host refreshes open pull requests every `PullRequests__SyncIntervalSeconds` (default 300; 0 disables).

## Application conventions

- The API and MCP are thin. All rules live in the application services under `src/DevWorkflow.Application/Services`, which both hosts call. Add an operation there first, then expose it in `Endpoints.cs` and in a tool class under `src/DevWorkflow.Mcp/Tools`.
- Services take a `Caller` and check its role themselves. Hosts also put a role attribute on each endpoint and tool, which hides tools a credential cannot use. Keep both in sync.
- Expected failures throw `WorkflowException` or `DomainException` with a stable snake_case code. `WorkflowError.From` classifies them. The API returns a problem response with a `code` field, and MCP returns `{"error":{"kind","code","message"}}` with `isError` set.
- Executor operations are scoped twice: by the executor's repository, given as a key or any form of git remote URL, and by `Execution.ClaimedBy`, the credential that made the claim.
- Claims race safely. A stale version or the one-active-execution index turns into `already_claimed`. A retried claim by the same credential and executor ID returns the existing execution.
- Signals are redacted on the server before storage by `SignalRedactor`, which removes common secrets and email addresses.
- JSON is snake_case everywhere, including enum values such as `pr_ready`, through `WorkflowJson`. MCP tool parameters are snake_case C# parameter names for the same reason.
- The API host runs `LeaseExpiryWorker`, which requeues work whose lease expired. `Leases__SweepIntervalSeconds` sets the interval, and 0 disables it. Claims also expire a stale lease inline.
- Provider access goes through `IRepositoryClientFactory`, which binds an `IRepositoryClient` to one repository and its credential, or returns null when none is configured. Services must handle the null case by skipping verification, never by failing. The adapters live in `src/DevWorkflow.Infrastructure/Providers`. Provider failures become `ErrorKind.Unavailable` (HTTP 502) with codes such as `provider_unavailable`, and messages never include a credential.
- Never add merge, approve, deploy or branch-protection operations to `IRepositoryClient`. A test fails if one appears.
- Provider tests use the in-memory fakes in `tests/DevWorkflow.Tests/Providers/FakeProviders.cs`, wired with `UseFakeProviders`. Tests never call a real provider.

## Solution conventions

- Shared compiler settings live in `Directory.Build.props`: net10.0, nullable, latest analyzers, warnings as errors. Keep project files minimal.
- Package versions are central in `Directory.Packages.props`. Add a `PackageVersion` there and a versionless `PackageReference` in the project.
- `nuget.config` clears machine-level feeds and uses only nuget.org, so the public repo never depends on private package sources.
- `global.json` pins the SDK to 10.0.x.
- Dependency direction is Domain, then Application, then Infrastructure, with Api and Mcp on top. Api and Mcp never reference each other. `ProjectLayeringTests` enforces this from the project files and fails if a new `src/` project has no rule.
- Test names may use underscores; `tests/.editorconfig` relaxes that one analyzer rule for tests only.

## Persistence conventions

- Domain entities have private setters and change state only through methods that enforce the rules and throw `DomainException` with a stable `Code`. EF maps them through backing fields; the Domain project has no EF dependency.
- The registered repository entity is `CodeRepository`, so it isn't confused with the data-access pattern.
- Tables and columns are snake_case. Enums are stored as strings, except work item priority, which is stored as a number so the queue can sort by it.
- `WorkItem.Version` and `Execution.Version` are concurrency tokens. A stale save throws `DbUpdateConcurrencyException`.
- Some rules live in the database on purpose, and tests prove each one. A partial unique index allows one active execution per work item. A unique index on source type plus idempotency key deduplicates signals. Triggers make `audit_events` append-only and make a signal's raw data immutable. Only a signal's status may change.
- When the model changes, add a migration. The `Migrations_match_the_model` test fails if you forget.
- Persistence tests share one Testcontainers Postgres instance. Keep them independent by creating unique data instead of cleaning up, since triggers block deleting signals and audit events.

## What is being built

A provider-neutral, agent-assisted development platform. Phase 1 is the **development queue, MCP gateway, and signal intake interface**. The listeners that feed intake and the evaluator that reads it come later.

Planned stack: .NET 10, PostgreSQL, Docker Compose, MCP server, GitHub and Bitbucket adapters.

Implementation milestones (from `docs/build-prompt.md`): scaffold solution → Postgres persistence/migrations → authenticated API + MCP tools → GitHub/Bitbucket adapters behind interfaces (mock remotes in tests) → Docker Compose + `.env.example` + seeding → reusable local Claude Code skill (and Codex equivalent) → demonstrate the acceptance tests in `docs/phase1-spec.md`.

## Architecture essentials

- **Three runtimes, one shared store.** Discovery (later) ingests signals; Evaluation (later) runs every three hours and records decisions; Development queue (Phase 1) persists approved work and execution leases. Services communicate through durable records and events (Postgres transactional outbox + polling), never through agent chat sessions. Do not introduce a message broker unless justified.
- **WorkItem lifecycle:** Proposed → Approved / Rejected → Claimed → InProgress → PRReady → Completed, plus Blocked, Failed, Cancelled, and lease expiry → requeue. Only Approved items can be claimed.
- **Concurrency is a core requirement, not a nicety.** Claims and leases must be transactional with optimistic concurrency (WorkItem carries a `version`) so two executors can never hold the same item. Heartbeats extend leases; expired leases are recoverable after restart. Use idempotency keys.
- **Audit is append-only.** Every state change and authorization decision is recorded as an Evidence/Activity event with references to originating systems.
- **Signals come in through one intake interface.** Listeners for any source submit raw signals idempotently. Signals are append-only, redacted before storage, and never become executable work directly; a proposed work item links back to its signals. Listener, evaluator, executor, and approver credentials are separate roles.
- **Work is routed to one repository.** Each work item targets exactly one registered repository and cannot be approved without one. An executor identifies its repository from its git remote and only lists and claims work for that repository; a mismatched claim is rejected. Cross-repository work is split into linked items.
- **The MCP and API expose the same contract:** `list_approved_work`, `get_work_item`, `claim_work_item`, `heartbeat_execution`, `update_execution`, `report_pull_request`, `release_claim`. Return structured validation and conflict errors.
- **Repository adapters** sit behind interfaces: resolve repo, branch, push, open PR, get PR status. Bitbucket has no GitHub Actions; do not assume CI shape. Credentials are scoped per provider and per repository.

## Hard boundaries (do not relax these)

- **Never automatically merge or deploy.** Automatic work ends at PR creation. Production deployment is a separate, human-authorized workflow gated by infrastructure permissions, not prompts.
- **Classification is not authorization.** A finding being "actionable" does not approve it. Authorization is a distinct record (decision, actor, scope, reason) and only authorized work enters the executable queue. Confirmed low-risk defects may auto-qualify for development through PR creation only; features and scope expansions need a human.
- **No production credentials in the development executor.** Development credentials cannot merge protected branches or deploy. Monitoring credentials are read-only. Never expose secrets in API/MCP responses or logs.
- **The local executor is human-launched.** A person starts Claude Code or Codex with their own subscription sign-in. Do not design for unattended subscription sessions. The executor must resume safely from persisted state.
- **Do not invent integration credentials.** Use `.env.example` with placeholders only.

## Working conventions from the handoff

- Work in milestones with incremental commits and verified tests; keep the solution small and extensible.
- Implement in isolated git worktrees and branches; open a PR rather than committing to main.
- If a significant architectural decision is ambiguous, explain the tradeoff before committing to it.
- Keep later-phase concerns (discovery connectors, evaluation cadence, digests, Slack/Jira approval, dashboards) out of Phase 1 unless an interface needs them.

## Public repository: keep it generic

This repository is public and must stay organization-neutral. Do not add anything specific to an employer, client, or internal environment:

- No company, product, team, or customer names, and no internal repository, service, or hostnames.
- No real Slack channels or workspaces, Jira projects or keys, Grafana dashboards, AWS accounts, or CloudWatch log groups.
- No real people's names or work email addresses. Use placeholders such as `example.com`, `acme`, or `PROJ-123`.
- Seed data, sample tasks, tests, and docs use fictional examples only.
- Configuration values come from environment variables. `.env.example` holds placeholders only.

Organization-specific setup belongs in a private deployment repository or local, gitignored config, not here.
