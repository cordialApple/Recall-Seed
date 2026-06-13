#requires -Version 5.1
<#
.SYNOPSIS
  Non-interactive end-to-end smoke test for the PersonalServer MCP server (stdio).

.DESCRIPTION
  Builds the project, launches the built server executable, and drives it over stdio with:
    initialize -> notifications/initialized -> tools/list -> tools/call ping
  It keeps stdin open until the responses arrive (a real MCP client does the same), prints the
  raw JSON-RPC responses from stdout, and asserts that `ping` is listed and returns `pong`.

  stdout carries only JSON-RPC; the server's logs go to stderr.

.EXAMPLE
  pwsh ./scripts/smoke-ping.ps1
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Debug',
    [int]$TimeoutMs = 20000
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$proj = Join-Path $repoRoot 'PersonalServer.csproj'

Write-Host "==> Building ($Configuration)..." -ForegroundColor Cyan
dotnet build $proj -c $Configuration | Out-Host
if ($LASTEXITCODE -ne 0) { throw "Build failed (exit $LASTEXITCODE)." }

# Locate the built server host (RID-specific output dir, since the project is self-contained).
$exe = Get-ChildItem -Path (Join-Path $repoRoot "bin/$Configuration") -Recurse -Filter 'PersonalServer.exe' -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1 -ExpandProperty FullName
if (-not $exe) { throw "Could not find a built PersonalServer.exe under bin/$Configuration." }
Write-Host "==> Server: $exe" -ForegroundColor Cyan

# JSON-RPC frames (newline-delimited). protocolVersion matches the current MCP spec.
$frames = @(
    '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"smoke-ping","version":"1.0.0"}}}',
    '{"jsonrpc":"2.0","method":"notifications/initialized"}',
    '{"jsonrpc":"2.0","id":2,"method":"tools/list"}',
    '{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"ping","arguments":{"message":"hello"}}}'
)
$payload = ($frames -join "`n") + "`n"

$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = $exe
$psi.RedirectStandardInput  = $true
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError  = $true
$psi.UseShellExecute = $false
$psi.CreateNoWindow  = $true
$psi.StandardOutputEncoding = New-Object System.Text.UTF8Encoding($false)
$psi.StandardErrorEncoding  = New-Object System.Text.UTF8Encoding($false)

$proc = [System.Diagnostics.Process]::Start($psi)
$errTask = $proc.StandardError.ReadToEndAsync()   # drain stderr so it can't block the pipe
$out = $proc.StandardOutput

# Send all requests, keep stdin OPEN so the server doesn't shut down before flushing responses.
$proc.StandardInput.Write($payload)
$proc.StandardInput.Flush()

# Read response lines until we have 3 (initialize, tools/list, tools/call) or we time out.
$responses = New-Object System.Collections.Generic.List[string]
$sw = [System.Diagnostics.Stopwatch]::StartNew()
$pending = $out.ReadLineAsync()
while ($sw.ElapsedMilliseconds -lt $TimeoutMs -and $responses.Count -lt 3) {
    if ($pending.Wait(500)) {
        $line = $pending.Result
        if ($null -eq $line) { break }            # EOF
        if ($line.Trim() -ne '') { $responses.Add($line) }
        $pending = $out.ReadLineAsync()
    }
}

# Done reading: close stdin and let the server exit cleanly.
$proc.StandardInput.Close()
if (-not $proc.WaitForExit(5000)) { $proc.Kill() }
$null = $errTask.GetAwaiter().GetResult()

Write-Host ""
Write-Host "===== RAW STDOUT (JSON-RPC) =====" -ForegroundColor Green
$responses | ForEach-Object { $_ }
Write-Host ""

# Assertions.
$listOk = $responses | Where-Object { $_ -match '"tools/list"|"tools"' } | Where-Object { $_ -match '"name":"ping"' }
$callOk = $responses | Where-Object { $_ -match 'pong \| msg=hello' }

$failed = $false
if ($responses.Count -lt 3) { Write-Host "FAIL: expected 3 responses, got $($responses.Count)." -ForegroundColor Red; $failed = $true }
if (-not $listOk) { Write-Host "FAIL: 'ping' not found in tools/list." -ForegroundColor Red; $failed = $true }
if (-not $callOk) { Write-Host "FAIL: tools/call did not return 'pong | msg=hello'." -ForegroundColor Red; $failed = $true }

if ($failed) { exit 1 }
Write-Host "PASS: ping listed and tools/call returned pong." -ForegroundColor Green
exit 0
