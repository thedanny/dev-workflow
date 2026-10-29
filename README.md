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

## Planned layout
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
The source directories are proposed, not implemented.

## Safety
Agents get least-privilege repository access; production credentials and deployment permissions stay outside the development executor. Require explicit human authorization for release.
