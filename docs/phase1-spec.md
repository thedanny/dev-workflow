# Phase 1 — Technical specification

## Scope
Create a reusable development queue and MCP gateway, not the entire monitoring platform. Stack: .NET 10, PostgreSQL, Docker Compose, local Claude Code/Codex, provider adapters for GitHub and Bitbucket.

## Core model
- **WorkItem**: ID, title, description, repository provider/URL, source references, acceptance criteria, priority, risk, status, created/updated timestamps, version.
- **Authorization**: item ID, decision, actor, scope, reason, timestamp. Actionability is not authorization.
- **Execution**: item ID, claimed-by, lease expiration, heartbeat, branch, worktree, execution state, timestamps, errors.
- **Evidence/Activity**: append-only audit trail and references to originating systems.
- **PullRequest**: provider, repository, PR ID/URL, head/base, checks and review summary.

## Lifecycle
Proposed → Approved / Rejected → Claimed → InProgress → PRReady → Completed; support Blocked, Failed, Cancelled and lease expiry/requeue. Only approved work can be claimed. Use optimistic concurrency and transactional claim/lease to prevent double execution. Maintain audit history and idempotency keys.

## API and MCP contract
Provide authenticated equivalents of:
- list_approved_work(limit, filters)
- get_work_item(id)
- claim_work_item(id, executor_id, lease_seconds)
- heartbeat_execution(execution_id)
- update_execution(execution_id, state, summary, evidence)
- report_pull_request(execution_id, provider, url, branch, checks)
- release_claim(execution_id, reason)

Return structured validation and conflict errors. Never expose secrets in responses.

## Local executor
Human explicitly launches Claude Code or Codex using their own supported subscription sign-in. A reusable skill reads approved tasks, claims each task, checks out an isolated worktree and branch, plans and delegates according to complexity, implements, tests, critiques/reviews and opens a PR. Do not assume unattended subscription sessions are supported. Resume safely from persisted state.

## Repository abstraction
Support GitHub and Bitbucket through separate adapters: resolve repo, branch, push, open PR, retrieve PR status. No GitHub Actions assumption for Bitbucket. Credentials are scoped per provider and repository.

## Security and release boundary
- Development credentials cannot merge protected branches or deploy.
- Production deployment remains a separate, human-authorized workflow.
- Never give agents production secrets or unrestricted shell/cloud privileges.
- Restrict read-only monitoring credentials and redact sensitive logs.
- Audit all state changes and approval decisions.

## Delivery
Docker Compose for API/Postgres/MCP, migrations, health checks, structured logs, tests, sample environment template (no credentials), README with local setup, three seeded demonstration tasks.

## Acceptance criteria
1. Create three sample tasks and approve two.
2. Only approved tasks appear in the executable queue.
3. Concurrent claims cannot acquire the same task.
4. Restart preserves state; expired leases can be recovered.
5. A local agent can retrieve, claim, work, report a PR and complete a task.
6. No workflow has permission to merge or deploy.

## Later phases
Persistent discovery connectors/checkpoints; three-hour evaluation with immediate critical-alert path; actionable/non-actionable decisions, reevaluation and digests; approval via Slack/Jira; dashboards and notifications. Keep these out of Phase 1 unless needed for interfaces.
