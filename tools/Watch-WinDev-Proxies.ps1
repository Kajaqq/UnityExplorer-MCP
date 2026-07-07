[CmdletBinding()]
param(
    [string]$TargetHost = "127.0.0.1",
    [int[]]$Ports = @(8082, 8083),
    [int]$TimeoutSec = 4,
    [int]$TailLines = 20,
    [switch]$NoRestart
)

$ErrorActionPreference = "Stop"

function Invoke-ProxyHealth
{
    param(
        [Parameter(Mandatory = $true)][int]$Port
    )

    $url = "http://$($TargetHost):$Port/mcp"
    $body = '{"jsonrpc":"2.0","id":"health","method":"tools/list","params":{}}'
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    try
    {
        $resp = Invoke-WebRequest -UseBasicParsing -Uri $url -Method Post -ContentType "application/json" -Headers @{ Accept = "application/json, text/event-stream" } -Body $body -TimeoutSec $TimeoutSec -SkipHttpErrorCheck
        $sw.Stop()
        $status = $resp.StatusCode
        $healthy = $status -ge 200 -and $status -lt 500
        return [pscustomobject]@{
            Port       = $Port
            Healthy    = $healthy
            StatusCode = $status
            ElapsedMs  = [int]$sw.Elapsed.TotalMilliseconds
            Error      = $null
        }
    } catch
    {
        $sw.Stop()
        return [pscustomobject]@{
            Port       = $Port
            Healthy    = $false
            StatusCode = $null
            ElapsedMs  = [int]$sw.Elapsed.TotalMilliseconds
            Error      = $_.Exception.Message
        }
    }
}

function Restart-Proxy
{
    param(
        [Parameter(Mandatory = $true)][int]$Port
    )

    $taskName = "McpProxy$Port"
    $logPath = "C:\\codex-workspace\\logs\\mcp-proxy-$Port.log"

    $remoteScript = @"
`$ErrorActionPreference = 'Stop'
Start-ScheduledTask -TaskName '$taskName' | Out-Null
`$info = Get-ScheduledTaskInfo -TaskName '$taskName'
Write-Output "[$taskName] restarted; LastRunTime=$($info.LastRunTime); LastTaskResult=$($info.LastTaskResult)"
if (Test-Path '$logPath') {
    Write-Output "[$taskName] log tail ($TailLines lines): $logPath"
    Get-Content -Path '$logPath' -Tail $TailLines
} else {
    Write-Output "[$taskName] log missing: $logPath"
}
"@

    $sshArgs = @("GPUVM@$TargetHost", "pwsh", "-NoLogo", "-NoProfile", "-Command", $remoteScript)
    & ssh @sshArgs
    if ($LASTEXITCODE -ne 0)
    {
        throw "ssh restart failed for $taskName (exit $LASTEXITCODE)"
    }
}

$unhealthy = @()
foreach ($port in $Ports)
{
    $result = Invoke-ProxyHealth -Port $port
    if ($result.Healthy)
    {
        Write-Host "[OK] $port healthy (HTTP $($result.StatusCode), $($result.ElapsedMs)ms)" -ForegroundColor Green
        continue
    }

    Write-Warning "[$port] unhealthy: $($result.Error ?? 'bad status')"
    $unhealthy += $port

    if ($NoRestart)
    { continue 
    }

    Write-Host "[$port] restarting scheduled task..." -ForegroundColor Yellow
    Restart-Proxy -Port $port
    $result = Invoke-ProxyHealth -Port $port
    if ($result.Healthy)
    {
        Write-Host "[OK] $port healthy after restart (HTTP $($result.StatusCode), $($result.ElapsedMs)ms)" -ForegroundColor Green
        $unhealthy = $unhealthy | Where-Object { $_ -ne $port }
    } else
    {
        Write-Error "[$port] still unhealthy after restart: $($result.Error ?? 'bad status')"
    }
}

if ($unhealthy.Count -gt 0)
{
    exit 1
}
