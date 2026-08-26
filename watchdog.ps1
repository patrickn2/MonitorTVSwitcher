$ErrorActionPreference = "Continue"
$repoRoot = $PSScriptRoot
$exe = Join-Path $repoRoot "bin\Release\net10.0-windows\MonitorTVSwitcher.exe"
$log = Join-Path $env:APPDATA "MonitorTVSwitcher\switch.log"
$recovery = Join-Path $env:APPDATA "MonitorTVSwitcher\recovery.log"

function Write-Recovery($msg) {
    $line = "{0:yyyy-MM-dd HH:mm:ss.fff} {1}" -f (Get-Date), $msg
    Add-Content -Path $recovery -Value $line
    Write-Output $line
}

function Restore-Monitor {
    Write-Recovery "RECOVERY: stopping MonitorTVSwitcher and switching to monitor"
    Get-Process MonitorTVSwitcher, GameShortcut -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Seconds 1
    & $exe --switch-to monitor
    Write-Recovery "RECOVERY: monitor switch exit $LASTEXITCODE"
}

if (-not (Test-Path $log)) { New-Item -ItemType File -Path $log -Force | Out-Null }

Write-Output "WATCHDOG ready pid=$PID log=$log"
$events = New-Object System.Collections.Generic.List[object]
$recovered = $false

Get-Content $log -Wait -Tail 0 | ForEach-Object {
    $line = $_
    Write-Output $line
    $now = Get-Date
    $events.Add([pscustomobject]@{ At = $now; Text = $line }) | Out-Null
    while ($events.Count -gt 0 -and ($now - $events[0].At).TotalSeconds -gt 12) {
        $events.RemoveAt(0)
    }

    $exclusive = @($events | Where-Object { $_.Text -match "SetExclusive " }).Count
    $hdr = @($events | Where-Object { $_.Text -match "EnableHdr " }).Count
    $apply = @($events | Where-Object { $_.Text -match "SetDisplayConfig=" -or $_.Text -match "SetDisplayConfig flags=" }).Count
    $failed = $line -match "Switch failed|after-failure|Could not switch"

    if (-not $recovered -and ($exclusive -ge 3 -or $hdr -ge 3 -or $apply -ge 6 -or $failed)) {
        $recovered = $true
        Write-Recovery "LOOP detected exclusive=$exclusive hdr=$hdr apply=$apply failed=$failed last='$line'"
        Restore-Monitor
        break
    }
}
