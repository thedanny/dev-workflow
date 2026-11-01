# Phase 1 acceptance

There are two ways to check each acceptance criterion in [the Phase 1 spec](phase1-spec.md):

- **Automated tests,** which run against a real PostgreSQL in a container: `dotnet test DevWorkflow.slnx`.
- **The live demo,** which walks through all ten criteria against the Docker Compose stack, over HTTP and MCP, with the same keys an agent uses.

## Run the live demo

```sh
scripts/init-env.sh                       # or: pwsh scripts/init-env.ps1. Keep the four keys it prints.
docker compose up -d --build --wait
pwsh scripts/acceptance-demo.ps1 -ListenerKey <key> -EvaluatorKey <key> -ApproverKey <key> -ExecutorKey <key>
```

The script needs PowerShell 7, which runs on Windows, macOS and Linux. Instead of passing the keys as parameters, you can set `DEV_WORKFLOW_LISTENER_KEY`, `DEV_WORKFLOW_EVALUATOR_KEY`, `DEV_WORKFLOW_APPROVER_KEY` and `DEV_WORKFLOW_EXECUTOR_KEY`.

What the script does:

- Every run creates its own fictional repositories, signals and tasks, so it can run any number of times.
- For criterion 8, it restarts the `api` and `mcp` containers. Pass `-SkipRestart` to skip that.
- Leave the provider tokens in `.env` empty. The demo repositories don't exist on GitHub or Bitbucket, so with a token set, registering them fails.
- It prints one line per check, then a summary. It exits 1 if any check fails.

## Criteria

| # | Criterion | Automated tests | Live demo checks |
|---|---|---|---|
| 1 | Create three sample tasks across two repositories and approve two | `WorkflowTests.Of_three_tasks_across_two_repositories_only_the_two_approved_are_executable`; `SampleDataTests` for the seeded tasks | Proposes three tasks on a GitHub and a Bitbucket repository and approves two |
| 2 | Only approved tasks appear in the executable queue | `WorkflowTests.Only_approved_work_for_the_executors_repository_appears_in_its_queue`; `WorkItemTests.Only_approved_work_can_be_claimed` | The approved task is in the queue and the proposed one isn't |
| 3 | Concurrent claims cannot acquire the same task | `WorkflowTests.Concurrent_claims_cannot_acquire_the_same_work_item`; `PersistenceTests.The_database_allows_only_one_active_execution_per_work_item`; `PersistenceTests.A_stale_work_item_update_is_rejected` | Six simultaneous HTTP claims: one succeeds, five get `already_claimed` |
| 4 | An executor working in one repository neither sees nor can claim approved work for another | `WorkflowTests.An_executor_cannot_see_or_claim_work_for_another_repository`; `ApiTests.Claims_over_http_return_structured_conflict_and_mismatch_errors` | The other repository's task isn't in the queue. Claiming or reading it fails with `repository_mismatch`. |
| 5 | A work item without a target repository cannot be approved | `WorkItemTests.A_work_item_without_a_repository_cannot_be_approved`; `WorkflowTests.A_work_item_without_a_target_repository_cannot_be_approved_until_routed` | Approval fails with `repository_required` and succeeds after routing |
| 6 | A signal submitted twice with the same idempotency key is stored once | `PersistenceTests.The_same_idempotency_key_from_the_same_source_is_stored_once`; `WorkflowTests.Resubmitting_a_signal_returns_the_original_and_secrets_are_redacted`; `ApiTests.A_listener_can_submit_signals_idempotently_but_cannot_read_the_queue` | 201 and then 200 with the same signal. The reporter's email is redacted. |
| 7 | A proposed work item links back to the signals it came from, and a listener credential cannot list or claim work | `PersistenceTests.A_work_item_round_trips_with_routing_criteria_and_signal_links`; `McpTests.Each_role_only_sees_its_own_tools`; `McpTests.A_listener_cannot_call_an_executor_tool`; `WorkflowTests.Roles_and_execution_ownership_are_enforced` | The task lists its signal. The listener key gets 403 on the queue and on a claim. |
| 8 | Restart preserves state; expired leases can be recovered | `WorkflowTests.State_survives_a_restart_and_a_lease_that_expired_meanwhile_is_recovered`; `WorkflowTests.Expired_leases_are_recovered_and_the_work_can_be_claimed_again`; `WorkflowTests.A_claim_takes_over_a_stale_lease_without_waiting_for_the_sweep` | After restarting the containers, a completed task is still completed and a 30-second claim is still held. Once that lease runs out, another executor claims the task. |
| 9 | A local agent can retrieve, claim, work, report a PR and complete a task | `McpTests.An_agent_works_a_task_from_claim_to_completion_through_mcp_tools`; `WorkflowTests.A_signal_becomes_approved_work_that_an_agent_claims_reports_and_completes`; `ProviderVerificationTests` for verified pull requests | The executor finds its claim, heartbeats, records its branch, reports a pull request and completes. See also [a real agent session](#a-real-agent-session). |
| 10 | No workflow has permission to merge or deploy | `AgentSkillTests.No_mcp_tool_can_merge_or_deploy`; `ApiTests.No_http_route_can_merge_or_deploy`; `RepositoryClientTests.Repository_clients_cannot_merge_approve_or_deploy` | No executor or approver MCP tool merges or deploys, and the API has no merge route |

On criterion 10: the server's provider tokens are documented as read-only. The executor pushes and opens pull requests with the developer's own credentials, and branch protection on the git host decides what those can do. Production deployment isn't part of this system.

## A real agent session

The demo plays the agent's part over HTTP. To see Claude Code or Codex do the same with the [executor skill](../agents/skills/dev-workflow-executor/SKILL.md), you need a repository you can push to:

1. Register it with the approver key. Use the provider token in `.env` if you want pull requests verified.
   ```sh
   curl -X POST http://localhost:5080/api/v1/repositories -H "Authorization: Bearer <approver key>" \
     -H "Content-Type: application/json" \
     -d '{"key":"my-sandbox","provider":"github","remote_url":"https://github.com/<you>/<sandbox>.git"}'
   ```
2. Propose a small task with the evaluator key (`POST /api/v1/work-items` with `"repository":"my-sandbox"`), then approve it with the approver key (`POST /api/v1/work-items/<id>/approve`).
3. Set up the agent as described in [agents/README.md](../agents/README.md).
4. Start the agent in a checkout of the sandbox and ask it to work the Dev Workflow queue.

The agent should show you the task and claim it. Then it implements and tests the change in a new worktree, opens a pull request, reports it and completes the task. Check the result with `GET /api/v1/work-items/<id>` (status `completed`) and `GET /api/v1/work-items?status=completed`. The pull request stays open until you merge it.

If the agent stops after reporting the pull request but before completing the task, the task keeps its `pr_ready` status once the lease runs out. An approver closes it with `complete_work_item`, or `POST /api/v1/work-items/<id>/complete`.
