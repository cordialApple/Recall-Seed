#requires -Version 5.1
<#
.SYNOPSIS
  One-time repo config so the /next-stage loop can auto-merge: enable squash auto-merge and
  protect main behind the CI 'build-test' check (no human review required).

.DESCRIPTION
  Run once after the repo exists and main is pushed. Idempotent — safe to re-run.
  Requires: gh authenticated with 'repo' scope on the target repository.

.EXAMPLE
  powershell -File ./scripts/setup-automerge.ps1
#>
[CmdletBinding()]
param([string]$Repo)

$ErrorActionPreference = 'Stop'
if (-not $Repo) { $Repo = gh repo view --json nameWithOwner --jq '.nameWithOwner' }
Write-Host "==> Configuring $Repo" -ForegroundColor Cyan

# 1. Allow squash + auto-merge; delete the branch once merged.
gh api "repos/$Repo" -X PATCH -F allow_auto_merge=true -F allow_squash_merge=true -F delete_branch_on_merge=true | Out-Null
Write-Host "auto-merge + squash enabled" -ForegroundColor Green

# 2. Protect main: require the CI 'build-test' check to pass; no required human reviews.
$body = '{ "required_status_checks": { "strict": false, "contexts": ["build-test"] }, "enforce_admins": false, "required_pull_request_reviews": null, "restrictions": null }'
$tmp = [System.IO.Path]::GetTempFileName()
[System.IO.File]::WriteAllText($tmp, $body)   # UTF-8 no BOM, so gh api parses it
try { gh api "repos/$Repo/branches/main/protection" -X PUT --input $tmp | Out-Null }
finally { Remove-Item $tmp -ErrorAction SilentlyContinue }
Write-Host "main protected behind 'build-test'" -ForegroundColor Green

Write-Host "Done. PRs enable auto-merge with: gh pr merge <n> --squash --auto" -ForegroundColor Cyan
