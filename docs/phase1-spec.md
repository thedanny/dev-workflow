# Phase 1 — Technical specification

## Scope
Create a reusable development queue and MCP gateway, plus a source-neutral intake interface for raw signals. This is not the entire monitoring platform: listeners and the evaluator come later. Stack: .NET 10, PostgreSQL, Docker Compose, local Claude Code/Codex, provider adapters for GitHub and Bitbucket.

## Core model
- **Signal**: ID, source type, source reference, idempotency key, payload or payload reference, observed and received timestamps, redaction status, processing status. Raw inbound data from any source, stored before evaluation.
- **Repository**: ID, key, provider, canonical remote URL, default branch, enabled flag. A registry of the repositories work can be routed to.
- **WorkItem**: ID, title, description, target repository ID, originating signal IDs, source references, acceptance criteria, priority, risk, status, created/updated timestamps, version.
- **Authorization**: item ID, decision, actor, scope, reason, timestamp. Actionability is not authorization.
- **Execution**: item ID, claimed-by, lease expiration, heartbeat, branch, worktree, execution state, timestamps, errors.
- **Evidence/Activity**: append-only audit trail and references to originating systems.
- **PullRequest**: provider, repository, PR ID/URL, head/base, checks and review summary.

## Signal intake
Any listener can submit raw data through one intake operation, whatever the source: a Jira ticket, a CloudWatch or production log entry, a Grafana alert, a monitored email inbox, a Slack message or a support board. Source type is an open value, so adding a source needs no schema change. Listeners may push as events arrive or poll on a schedule; either way they submit the same record.

- Submission is idempotent. Resubmitting the same idempotency key returns the existing signal instead of creating a duplicate, so listeners can retry safely after a restart.
- Signals are append-only raw evidence. Processing status moves from Received to Evaluated or Discarded, but the payload is never edited.
- Secrets and personal data are redacted before storage, and the redaction status is recorded. Large payloads are stored by reference with a size limit on inline content.
- A signal never becomes executable work directly. Evaluation, or a human in Phase 1, proposes a work item that links to the signals it came from. One work item may link several signals, and one signal may feed several work items.

Callers have distinct roles. Listeners can submit signals only. Evaluators can read signals and propose work items. Executors can list and claim approved work only. Approvers record authorization decisions. A credential for one role cannot perform another role's operations.

## Repository routing
Every work item targets exactly one registered repository. Analysis assigns the target repository, or a human does during triage, and the routing decision is recorded in the audit trail. A work item cannot be approved without a target repository. Work that spans several repositories is split into one linked work item per repository.

An executor always works in the context of one repository. It identifies that repository from the git remote of its working directory, and the server matches it against the registry using a normalized remote URL, so SSH and HTTPS forms of the same remote match. An executor only sees and can only claim approved work for its own repository. A claim for a work item in a different repository is rejected with a structured mismatch error.

## Lifecycle
Proposed → Approved / Rejected → Claimed → InProgress → PRReady → Completed; support Blocked, Failed, Cancelled and lease expiry/requeue. Only approved work can be claimed. Use optimistic concurrency and transactional claim/lease to prevent double execution. Maintain audit history and idempotency keys.

## API and MCP contract
Provide authenticated equivalents of:
- submit_signal(source_type, source_reference, idempotency_key, payload, observed_at)
- list_signals(status, source_type, limit)
- propose_work_item(title, description, repository, signal_ids, acceptance_criteria, priority, risk)
- list_approved_work(repository, limit, filters), where repository is required
- get_work_item(id, repository), where executors must pass their repository and can only read work routed to it
- claim_work_item(id, executor_id, repository, lease_seconds), rejected if the repository does not match the work item
- heartbeat_execution(execution_id)
- update_execution(execution_id, state, summary, evidence)
- report_pull_request(execution_id, provider, url, branch, checks)
- release_claim(execution_id, reason)

Triage, authorization and the repository registry, needed to route and approve work:
- resolve_signal(signal_id, status, reason), marking a signal evaluated or discarded
- list_work_items(status, repository, limit)
- route_work_item(id, repository)
- approve_work_item(id, reason, scope), reject_work_item(id, reason)
- requeue_work_item(id, reason) to return Blocked work to the queue, and cancel_work_item(id, reason)
- register_repository(key, provider, remote_url, default_branch) and list_repositories()

Each operation is limited to the roles in Signal intake. An executor can only act on executions its own credential claimed. A retried claim by the same credential and executor ID returns the existing execution instead of a conflict.

Return structured validation and conflict errors. Never expose secrets in responses.

## Local executor
Human explicitly launches Claude Code or Codex using their own supported subscription sign-in. A reusable skill resolves the current repository from its git remote, reads approved tasks for that repository only, claims each task, checks out an isolated worktree and branch, plans and delegates according to complexity, implements, tests, critiques/reviews and opens a PR. Do not assume unattended subscription sessions are supported. Resume safely from persisted state.

## Repository abstraction
Support GitHub and Bitbucket through separate adapters: resolve repo, branch, push, open PR, retrieve PR status. No GitHub Actions assumption for Bitbucket. Credentials are scoped per provider and repository.

## Security and release boundary
- Development credentials cannot merge protected branches or deploy.
- Production deployment remains a separate, human-authorized workflow.
- Never give agents production secrets or unrestricted shell/cloud privileges.
- Restrict read-only monitoring credentials and redact sensitive logs.
- Audit all state changes and approval decisions.

## Delivery
Docker Compose for API/Postgres/MCP, migrations, health checks, structured logs, tests, sample environment template (no credentials), README with local setup, sample signals from at least two source types, and three seeded demonstration tasks across two sample repositories linked to those signals.

## Acceptance criteria
1. Create three sample tasks across two repositories and approve two.
2. Only approved tasks appear in the executable queue.
3. Concurrent claims cannot acquire the same task.
4. An executor working in one repository neither sees nor can claim approved work for another repository.
5. A work item without a target repository cannot be approved.
6. A signal submitted twice with the same idempotency key is stored once.
7. A proposed work item links back to the signals it came from, and a listener credential cannot list or claim work.
8. Restart preserves state; expired leases can be recovered.
9. A local agent can retrieve, claim, work, report a PR and complete a task.
10. No workflow has permission to merge or deploy.

## Later phases
Listener and connector implementations for Jira, CloudWatch, Grafana, email, Slack and support boards, with their own cursors and checkpoints, all submitting through the Phase 1 intake interface; three-hour evaluation with immediate critical-alert path; actionable/non-actionable decisions, reevaluation and digests; approval via Slack/Jira; dashboards and notifications. Keep these out of Phase 1 unless needed for interfaces.
