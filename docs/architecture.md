# Architecture and roadmap

## Runtime separation
1. **Discovery (later):** listeners for Slack, Jira, support boards, monitored email inboxes, Grafana and CloudWatch. Each listener pushes or polls, then submits raw signals through the shared intake interface built in Phase 1. Owns cursors, deduplication, source versions and raw evidence. Checkpoints and idempotency prevent repeated processing after restarts.
2. **Evaluation (later):** runs every three hours; urgent production incidents may trigger sooner. Correlates findings, assigns each finding a target repository, records decisions (actionable, needs investigation, not actionable, duplicate), reasons and revisit conditions. Sends weekly actionable and monthly non-actionable digests.
3. **Development queue (Phase 1):** also provides the signal intake interface and role-scoped credentials; persists approved work and execution leases. Human launches local Claude Code/Codex to pull approved tasks, implement and open PRs.

Services exchange durable records/events, not agent chat sessions. Start with PostgreSQL transactional outbox and polling; introduce a broker only when justified.

## Approval policy
Confirmed low-risk defects may qualify for automatic development **through PR creation only**; feature requests and scope expansions require human authorization. No autonomous merge or deployment. Production deployment must be gated by infrastructure permissions, not prompts.

## Extensibility
Provider-neutral repository adapters (GitHub/Bitbucket), model-neutral local executor, source-specific discovery connectors and policy-driven authorization. Prefer simple containers and .NET workers over heavyweight orchestration initially.
