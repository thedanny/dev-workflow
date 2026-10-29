# Claude Code / Codex — implementation handoff

You are implementing Phase 1 of the Dev Workflow platform. Read README.md, docs/phase1-spec.md and docs/architecture.md first.

Work in milestones, keep the solution small and extensible, and do not invent production integration credentials.

1. Scaffold a .NET 10 solution with Domain, Application, Infrastructure, API and MCP projects plus tests.
2. Implement PostgreSQL persistence/migrations for work items, approvals, execution leases, PR metadata and append-only audit events.
3. Implement authenticated API and MCP tools for listing, claiming, heartbeating, updating and reporting approved work.
4. Add provider-neutral GitHub and Bitbucket adapters behind interfaces; mock remote operations in tests.
5. Add Docker Compose, .env.example, setup documentation and local sample task seeding.
6. Add a reusable local Claude Code skill/instructions for claiming approved tasks, using isolated git worktrees, running tests, conducting review and creating PRs. Provide Codex-equivalent guidance.
7. Demonstrate the acceptance tests from docs/phase1-spec.md.

**Boundaries:** No automatic merge, production deployment or access to production credentials. Human initiates subscription-authenticated local coding sessions. Don't conflate classification with authorization.

Use independent critique/QA where useful, but avoid a rigid multi-agent script. Prefer a concise execution plan, incremental commits and verified tests. At the end, report what was built, commands to run, tests and any unresolved risks. If a significant architectural decision is ambiguous, explain the tradeoff before committing to it.
