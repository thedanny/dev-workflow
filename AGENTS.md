# AGENTS.md

Guidance for AI coding agents (Claude Code, Codex, and others) working in this repository.

## Current state

Milestone 1 is done. The .NET 10 solution is scaffolded with Domain, Application, Infrastructure, Api, Mcp and Tests projects. Api and Mcp are empty hosts that only expose `/health`. There is no domain model, persistence, or MCP tooling yet. Before adding features, read in this order:

1. `docs/phase1-spec.md` – the Phase 1 technical spec (core model, lifecycle, API/MCP contract, acceptance criteria)
2. `docs/architecture.md` – runtime separation, approval policy, extensibility
3. `docs/build-prompt.md` – the milestone-ordered implementation handoff

## Commands

```sh
dotnet build DevWorkflow.slnx        # build everything; warnings are errors
dotnet test DevWorkflow.slnx         # run all tests
dotnet test DevWorkflow.slnx --filter "FullyQualifiedName~ProjectLayeringTests"   # run one test class
dotnet run --project src/DevWorkflow.Api   # API on http://localhost:5080
dotnet run --project src/DevWorkflow.Mcp   # MCP host on http://localhost:5081
```

Add migration and Docker Compose commands here when those milestones land.

## Solution conventions

- Shared compiler settings live in `Directory.Build.props`: net10.0, nullable, latest analyzers, warnings as errors. Keep project files minimal.
- Package versions are central in `Directory.Packages.props`. Add a `PackageVersion` there and a versionless `PackageReference` in the project.
- `nuget.config` clears machine-level feeds and uses only nuget.org, so the public repo never depends on private package sources.
- `global.json` pins the SDK to 10.0.x.
- Dependency direction is Domain, then Application, then Infrastructure, with Api and Mcp on top. Api and Mcp never reference each other. `ProjectLayeringTests` enforces this from the project files and fails if a new `src/` project has no rule.
- Test names may use underscores; `tests/.editorconfig` relaxes that one analyzer rule for tests only.

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
