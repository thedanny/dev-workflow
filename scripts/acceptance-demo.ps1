<#
.SYNOPSIS
Walks through the Phase 1 acceptance criteria against a running Docker Compose stack.

.DESCRIPTION
Every run creates its own fictional repositories, signals and work items, so the script can run
repeatedly against the same database. Start the stack first with `docker compose up -d --build --wait`.
Leave the provider tokens in .env empty: the demo repositories do not exist on GitHub or Bitbucket,
so a configured token makes registering them fail.

The keys are the raw keys that scripts/init-env.ps1 or init-env.sh printed. Each parameter falls back
to an environment variable: DEV_WORKFLOW_LISTENER_KEY, DEV_WORKFLOW_EVALUATOR_KEY,
DEV_WORKFLOW_APPROVER_KEY and DEV_WORKFLOW_EXECUTOR_KEY.

Exits 1 if any check fails.

.PARAMETER SkipRestart
Don't restart the api and mcp containers for criterion 8. The lease recovery part still runs.

.EXAMPLE
pwsh scripts/acceptance-demo.ps1 -ListenerKey <key> -EvaluatorKey <key> -ApproverKey <key> -ExecutorKey <key>
#>
param(
    [string]$ListenerKey = $env:DEV_WORKFLOW_LISTENER_KEY,
    [string]$EvaluatorKey = $env:DEV_WORKFLOW_EVALUATOR_KEY,
    [string]$ApproverKey = $env:DEV_WORKFLOW_APPROVER_KEY,
    [string]$ExecutorKey = $env:DEV_WORKFLOW_EXECUTOR_KEY,
    [string]$ApiUrl = 'http://localhost:5080',
    [string]$McpUrl = 'http://localhost:5081/mcp',
    [switch]$SkipRestart
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
foreach ($name in 'ListenerKey', 'EvaluatorKey', 'ApproverKey', 'ExecutorKey') {
    if (-not (Get-Variable $name -ValueOnly)) {
        Write-Error "Pass -$name or set its DEV_WORKFLOW_*_KEY environment variable."
    }
}

$checks = [System.Collections.Generic.List[object]]::new()

function Check([int]$Criterion, [string]$What, [bool]$Passed) {
    $checks.Add([pscustomobject]@{ Criterion = $Criterion; Check = $What; Result = $Passed ? 'pass' : 'FAIL' })
    Write-Host ("  [{0}] {1}" -f ($Passed ? 'pass' : 'FAIL'), $What) -ForegroundColor ($Passed ? 'Green' : 'Red')
}

function Step([string]$Title) { Write-Host "`n$Title" -ForegroundColor Cyan }

# Calls the API and returns the status code and parsed body without throwing on 4xx.
function Api([string]$Key, [string]$Method, [string]$Path, $Body = $null) {
    $request = @{
        Method = $Method
        Uri = "$ApiUrl/api/v1$Path"
        Headers = @{ Authorization = "Bearer $Key" }
        SkipHttpErrorCheck = $true
    }
    if ($null -ne $Body) {
        $request.ContentType = 'application/json'
        $request.Body = $Body | ConvertTo-Json -Depth 10 -Compress
    }
    $response = Invoke-WebRequest @request
    $parsed = (Text $response.Content) | ConvertFrom-Json -Depth 20
    [pscustomobject]@{ Status = [int]$response.StatusCode; Body = $parsed }
}

# Problem responses (application/problem+json) arrive as bytes rather than a string.
function Text($Content) { $Content -is [byte[]] ? [System.Text.Encoding]::UTF8.GetString($Content) : [string]$Content }

function Wait-Healthy {
    for ($i = 0; $i -lt 60; $i++) {
        try {
            if ((Invoke-WebRequest "$ApiUrl/health" -SkipHttpErrorCheck).StatusCode -eq 200) { return }
        } catch { }
        Start-Sleep -Seconds 1
    }
    Write-Error "The API at $ApiUrl did not become healthy."
}

# Lists the MCP tools a key can see, over streamable HTTP.
function McpToolNames([string]$Key) {
    $headers = @{ Authorization = "Bearer $Key"; Accept = 'application/json, text/event-stream' }
    function Send($Payload) {
        $response = Invoke-WebRequest -Method Post -Uri $McpUrl -Headers $headers -ContentType 'application/json' `
            -Body ($Payload | ConvertTo-Json -Depth 10 -Compress) -SkipHttpErrorCheck
        if ($response.Headers['Mcp-Session-Id']) { $headers['Mcp-Session-Id'] = @($response.Headers['Mcp-Session-Id'])[0] }
        $text = Text $response.Content
        $data = ($text -split "`n" | Where-Object { $_ -like 'data:*' } | Select-Object -Last 1) -replace '^data:\s*', ''
        $json = $data ? $data : $text
        $json ? ($json | ConvertFrom-Json -Depth 20) : $null
    }
    Send @{ jsonrpc = '2.0'; id = 1; method = 'initialize'; params = @{
        protocolVersion = '2025-06-18'; capabilities = @{}; clientInfo = @{ name = 'acceptance-demo'; version = '1.0' } } } | Out-Null
    Send @{ jsonrpc = '2.0'; method = 'notifications/initialized' } | Out-Null
    (Send @{ jsonrpc = '2.0'; id = 2; method = 'tools/list' }).result.tools.name
}

Wait-Healthy
$run = [System.Convert]::ToHexString([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(3)).ToLowerInvariant()
Write-Host "Demo run $run against $ApiUrl"

Step 'Setup: register two fictional repositories'
$web = Api $ApproverKey POST '/repositories' @{ key = "demo-$run-web"; provider = 'github'; remote_url = "https://github.com/example-org/demo-$run-web.git" }
$billing = Api $ApproverKey POST '/repositories' @{ key = "demo-$run-billing"; provider = 'bitbucket'; remote_url = "https://bitbucket.org/example-team/demo-$run-billing.git" }
if ($web.Status -ne 201 -or $billing.Status -ne 201) {
    Write-Error "Registering the demo repositories failed ($($web.Status) $($web.Body.code), $($billing.Status) $($billing.Body.code)). Are provider tokens set in .env?"
}
# Executors identify their repository by git remote, here in SSH form.
$webRemote = "git@github.com:example-org/demo-$run-web.git"
$billingRemote = "git@bitbucket.org:example-team/demo-$run-billing.git"
Write-Host "  demo-$run-web (GitHub) and demo-$run-billing (Bitbucket)"

Step '6. A signal submitted twice with the same idempotency key is stored once'
$jiraSignal = @{ source_type = 'jira'; source_reference = "PROJ-$run"; idempotency_key = "PROJ-${run}:created"; payload = '{"summary":"Export drops the last row","reporter":"someone@example.com"}' }
$first = Api $ListenerKey POST '/signals' $jiraSignal
$again = Api $ListenerKey POST '/signals' $jiraSignal
$support = Api $ListenerKey POST '/signals' @{ source_type = 'support'; source_reference = "TICKET-$run"; idempotency_key = "TICKET-$run"; payload = '{"subject":"Invoice total off by a cent"}' }
Check 6 "First submission creates the signal ($($first.Status))" ($first.Status -eq 201)
Check 6 "Second submission returns the same signal ($($again.Status))" ($again.Status -eq 200 -and $again.Body.signal.id -eq $first.Body.signal.id)
Check 6 'The email in the payload was redacted before storage' ($first.Body.signal.payload -like '*[REDACTED_EMAIL]*')

Step '1. Create three tasks across two repositories and approve two'
function Propose([string]$Title, [string]$Repository, [string[]]$Signals) {
    Api $EvaluatorKey POST '/work-items' @{
        title = $Title; description = "Demo task $run"; repository = $Repository; signal_ids = $Signals
        acceptance_criteria = @('Covered by a test'); priority = 'high'; risk = 'low'
    }
}
$webFix = (Propose 'Keep the last row in the CSV export' "demo-$run-web" @($first.Body.signal.id)).Body
$webFeature = (Propose 'Add a PDF export' "demo-$run-web" @()).Body
$billingFix = (Propose 'Round invoice totals half away from zero' "demo-$run-billing" @($support.Body.signal.id)).Body
$approvals = @($webFix, $billingFix) | ForEach-Object { Api $ApproverKey POST "/work-items/$($_.id)/approve" @{ reason = 'Confirmed defect' } }
Check 1 'Three tasks proposed across two repositories' ((@($webFix, $webFeature, $billingFix) | Where-Object { $_.status -eq 'proposed' }).Count -eq 3)
Check 1 'Two of them approved' ((@($approvals | Where-Object { $_.Body.status -eq 'approved' })).Count -eq 2)

Step '2. Only approved tasks appear in the executable queue'
$webQueue = @((Api $ExecutorKey GET "/queue?repository=$([uri]::EscapeDataString($webRemote))").Body)
Check 2 'The approved web task is in the web queue' ($webQueue.id -contains $webFix.id)
Check 2 'The proposed web task is not' ($webQueue.id -notcontains $webFeature.id)

Step '5. A work item without a target repository cannot be approved'
$unrouted = (Propose 'Fix the date picker in Safari' $null @()).Body
$refused = Api $ApproverKey POST "/work-items/$($unrouted.id)/approve" @{ reason = 'Confirmed defect' }
$routed = Api $ApproverKey POST "/work-items/$($unrouted.id)/route" @{ repository = "demo-$run-web" }
$approvedAfterRouting = Api $ApproverKey POST "/work-items/$($unrouted.id)/approve" @{ reason = 'Confirmed defect' }
Check 5 "Approval is refused with repository_required ($($refused.Status))" ($refused.Status -ge 400 -and $refused.Body.code -eq 'repository_required')
Check 5 'After routing it to a repository it can be approved' ($routed.Status -eq 200 -and $approvedAfterRouting.Body.status -eq 'approved')

Step '7. A work item links back to its signals, and a listener cannot list or claim work'
$detail = (Api $ApproverKey GET "/work-items/$($webFix.id)").Body
$listenerQueue = Api $ListenerKey GET "/queue?repository=demo-$run-web"
$listenerClaim = Api $ListenerKey POST "/work-items/$($webFix.id)/claim" @{ executor_id = 'listener'; repository = "demo-$run-web" }
Check 7 'The web task links to the Jira signal' (@($detail.signal_ids) -contains $first.Body.signal.id)
Check 7 "A listener key cannot read the queue ($($listenerQueue.Status))" ($listenerQueue.Status -eq 403)
Check 7 "A listener key cannot claim work ($($listenerClaim.Status))" ($listenerClaim.Status -eq 403)

Step '4. An executor in one repository neither sees nor can claim work for another'
$crossClaim = Api $ExecutorKey POST "/work-items/$($billingFix.id)/claim" @{ executor_id = 'demo-web-agent'; repository = $webRemote }
$crossRead = Api $ExecutorKey GET "/work-items/$($billingFix.id)?repository=$([uri]::EscapeDataString($webRemote))"
Check 4 'The billing task is not in the web queue' ($webQueue.id -notcontains $billingFix.id)
Check 4 "Claiming the billing task from the web repository fails with repository_mismatch ($($crossClaim.Status))" ($crossClaim.Body.code -eq 'repository_mismatch')
Check 4 "Reading the billing task from the web repository fails ($($crossRead.Status))" ($crossRead.Status -ge 400)

Step '3. Concurrent claims cannot acquire the same task'
$claims = 1..6 | ForEach-Object -ThrottleLimit 6 -Parallel {
    $response = Invoke-WebRequest -Method Post -Uri "$($using:ApiUrl)/api/v1/work-items/$($using:webFix.id)/claim" `
        -Headers @{ Authorization = "Bearer $($using:ExecutorKey)" } -ContentType 'application/json' `
        -Body (@{ executor_id = "demo-agent-$_"; repository = $using:webRemote } | ConvertTo-Json) -SkipHttpErrorCheck
    $content = $response.Content -is [byte[]] ? [System.Text.Encoding]::UTF8.GetString($response.Content) : $response.Content
    [pscustomobject]@{ Status = [int]$response.StatusCode; Body = $content | ConvertFrom-Json -Depth 20 }
}
$winners = @($claims | Where-Object Status -eq 200)
$losers = @($claims | Where-Object { $_.Body.code -eq 'already_claimed' })
Check 3 "Six simultaneous claims: exactly one succeeded ($($winners.Count))" ($winners.Count -eq 1)
Check 3 "The other five got already_claimed ($($losers.Count))" ($losers.Count -eq 5)

Step '9. A local agent retrieves, claims, works, reports a pull request and completes a task'
$execution = $winners[0].Body.execution
$held = @((Api $ExecutorKey GET "/executions?repository=$([uri]::EscapeDataString($webRemote))").Body)
$heartbeat = Api $ExecutorKey POST "/executions/$($execution.id)/heartbeat" @{ lease_seconds = 1800 }
$progress = Api $ExecutorKey POST "/executions/$($execution.id)/update" @{ state = 'in_progress'; summary = 'Reproduced; fixing'; branch = "dw/$run-csv-last-row"; worktree_path = "../demo-worktrees/$run" }
$pullRequest = Api $ExecutorKey POST "/executions/$($execution.id)/pull-request" @{ url = "https://github.com/example-org/demo-$run-web/pull/1"; branch = "dw/$run-csv-last-row"; checks = 'build ok; tests passed' }
$completed = Api $ExecutorKey POST "/executions/$($execution.id)/update" @{ state = 'completed'; summary = 'PR opened for review'; evidence = @("https://github.com/example-org/demo-$run-web/pull/1") }
Check 9 'The agent finds its claim with list_my_executions' ($held.execution.id -contains $execution.id)
Check 9 'Heartbeat and in_progress update succeed' ($heartbeat.Status -eq 200 -and $progress.Body.work_item.status -eq 'in_progress')
Check 9 "The pull request report moves the task to pr_ready (verified: $($pullRequest.Body.pull_request.verified_at ? 'yes' : 'no, no provider token'))" ($pullRequest.Body.work_item.status -eq 'pr_ready')
Check 9 'Completing finishes the task and the execution' ($completed.Body.work_item.status -eq 'completed' -and $completed.Body.execution.state -eq 'completed')

Step '8. Restart preserves state, and expired leases can be recovered'
$short = Api $ExecutorKey POST "/work-items/$($billingFix.id)/claim" @{ executor_id = 'demo-billing-agent'; repository = $billingRemote; lease_seconds = 30 }
$leaseEnds = [datetimeoffset]::Parse($short.Body.execution.lease_expires_at)
if (-not $SkipRestart) {
    Write-Host '  Restarting the api and mcp containers...'
    docker compose --project-directory $root restart api mcp | Out-Null
    Wait-Healthy
}
$afterRestart = (Api $ApproverKey GET "/work-items/$($webFix.id)").Body
$stillHeld = @((Api $ExecutorKey GET "/executions?repository=$([uri]::EscapeDataString($billingRemote))").Body)
Check 8 "$($SkipRestart ? 'Without a restart' : 'After a restart'), the completed task is still completed" ($afterRestart.status -eq 'completed')
Check 8 'The 30-second claim is still held' ($stillHeld.execution.id -contains $short.Body.execution.id)
$wait = [math]::Max(0, ($leaseEnds - [datetimeoffset]::UtcNow).TotalSeconds) + 2
Write-Host ("  Waiting {0:N0} seconds for the lease to expire..." -f $wait)
Start-Sleep -Seconds $wait
$recovered = Api $ExecutorKey POST "/work-items/$($billingFix.id)/claim" @{ executor_id = 'demo-billing-agent-2'; repository = $billingRemote }
Check 8 'Another executor can claim the task once the lease expired' ($recovered.Status -eq 200 -and $recovered.Body.execution.id -ne $short.Body.execution.id)
Api $ExecutorKey POST "/executions/$($recovered.Body.execution.id)/release" @{ reason = 'demo finished' } | Out-Null

Step '10. No workflow has permission to merge or deploy'
$forbidden = 'merge|deploy|protect'
$executorTools = @(McpToolNames $ExecutorKey)
$approverTools = @(McpToolNames $ApproverKey)
$mergeRoute = Api $ApproverKey POST "/work-items/$($webFix.id)/merge" @{ reason = 'try' }
Check 10 "The executor's $($executorTools.Count) MCP tools include none that merge or deploy" ($executorTools.Count -gt 0 -and -not ($executorTools -match $forbidden))
Check 10 "The approver's $($approverTools.Count) MCP tools include none that merge or deploy" ($approverTools.Count -gt 0 -and -not ($approverTools -match $forbidden))
Check 10 "The API has no merge route ($($mergeRoute.Status))" ($mergeRoute.Status -eq 404 -or $mergeRoute.Status -eq 405)

$failed = @($checks | Where-Object Result -ne 'pass')
Write-Host ''
$checks | Sort-Object Criterion | Format-Table -AutoSize | Out-String | Write-Host
Write-Host ("{0} of {1} checks passed." -f ($checks.Count - $failed.Count), $checks.Count) -ForegroundColor ($failed ? 'Red' : 'Green')
exit ($failed ? 1 : 0)
