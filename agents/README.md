# Local executor setup

The local executor is Claude Code or Codex, started by a person with their own sign-in, in a checkout of a repository that is registered with Dev Workflow. The [`dev-workflow-executor` skill](skills/dev-workflow-executor/SKILL.md) tells the agent how to work the queue:

1. Resume any execution it already holds.
2. Claim one approved task for the current repository.
3. Implement it in an isolated git worktree and branch.
4. Test it and review it.
5. Open a pull request, report it, and complete the task.

The agent never merges or deploys. The skill is the same file for both agents.

## Before you start

You need:

- **A running Dev Workflow server.** For a local one, see "Local setup" in the [README](../README.md). The MCP server listens on `http://localhost:5081/mcp`.
- **An executor key.** `scripts/init-env.sh` prints one. Use a separate key per person or machine. A key only ever acts on executions it claimed itself.
- **A registered repository.** An approver registers the repository with `register_repository`, and its `origin` remote must match. The sample data registers `sample-storefront` and `sample-billing`.
- **Your own git credentials,** with `gh` for GitHub or your usual tool for Bitbucket. The agent pushes branches and opens pull requests with them. Use credentials that can't bypass branch protection.

Put the key in an environment variable in the shell you start the agent from. Don't commit it to a file:

```sh
export DEV_WORKFLOW_EXECUTOR_KEY=<executor key>        # PowerShell: $env:DEV_WORKFLOW_EXECUTOR_KEY = '<executor key>'
```

## Claude Code

Install the skill for your user, so it is available in every repository:

```sh
mkdir -p ~/.claude/skills
cp -r agents/skills/dev-workflow-executor ~/.claude/skills/
```

On Windows, run `Copy-Item -Recurse agents/skills/dev-workflow-executor "$HOME/.claude/skills/"`.

Add the MCP server for your user:

```sh
claude mcp add --transport http --scope user dev-workflow http://localhost:5081/mcp \
  --header "Authorization: Bearer $DEV_WORKFLOW_EXECUTOR_KEY"
```

This stores the key in your local Claude Code configuration, not in any repository. To rotate it, run `claude mcp remove --scope user dev-workflow` and add the server again.

Then, in the repository you want to work on:

```sh
cd path/to/your-repository
claude
```

Ask for it in plain words, such as "work the dev workflow queue" or "resume my dev workflow task", or run `/dev-workflow-executor`. Run `/mcp` to check that `dev-workflow` is connected.

## Codex

Install the skill for your user:

```sh
mkdir -p ~/.codex/skills
cp -r agents/skills/dev-workflow-executor ~/.codex/skills/
```

If your Codex version doesn't load skills, point it at the instructions instead. Add a line to `~/.codex/AGENTS.md`: "When asked to work the Dev Workflow queue, follow `~/.codex/skills/dev-workflow-executor/SKILL.md`."

Add the MCP server to `~/.codex/config.toml`. Codex reads the key from the environment variable each time it starts, so the key is never written to the file:

```toml
[mcp_servers.dev-workflow]
url = "http://localhost:5081/mcp"
bearer_token_env_var = "DEV_WORKFLOW_EXECUTOR_KEY"
```

Then start `codex` in the repository and ask it to work the Dev Workflow queue. Run `/mcp` to check that the server's tools are listed.

How the skill differs under Codex:

- **Review.** Codex has no subagents for review. The skill uses `/review` against the default branch. A second Codex session started by the person gives a more independent review.
- **Approvals.** Keep Codex's normal approval mode. The skill needs to run git, the build, the tests and the pull request tool. It doesn't need network access beyond the Dev Workflow server and your git host. Don't run with sandboxing turned off in order to reach production systems.

## Resuming after a restart

The server, not the agent, holds the state. Each claim records its branch and worktree path through `update_execution`. When a session ends early:

1. Start the agent again in the same repository and ask it to resume.
2. The skill calls `list_my_executions`, renews each live lease, and continues from the work item's status.
3. If a lease expired first, the server returned the work to the queue, and it can be claimed again.

The `executor_id` the skill picks, such as `<hostname>-claude`, stays the same across sessions. A repeated claim with that ID therefore returns the existing execution instead of failing.

## What the executor can't do

The executor key only has the `Executor` role. The server rejects these operations for it:

- proposing, approving, rejecting or cancelling work
- reading other repositories' queues
- acting on another key's executions

The MCP server offers no merge, approve or deploy operation for any role. A test fails if one is added. The pull request waits for a person to review and merge it.
