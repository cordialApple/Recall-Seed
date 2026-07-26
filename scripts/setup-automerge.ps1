#requires -Version 5.1
<#
.SYNOPSIS
  One-time repo config for the /next-stage auto-merge loop: allow squash + auto-merge and
  delete the branch on merge. Idempotent, safe to re-run.

.DESCRIPTION
  Merging itself is done by .github/workflows/auto-merge.yml (label-gated, on CI success), so
  no branch protection is required, branch protection needs GitHub Pro on a private repo and
  is deliberately NOT used here. Requires gh authenticated with 'repo' scope.

.EXAMPLE
  powershell -File ./scripts/setup-automerge.ps1
#>
[CmdletBinding()]
param([string]$Repo)

$ErrorActionPreference = 'Stop'
if (-not $Repo) { $Repo = gh repo view --json nameWithOwner --jq '.nameWithOwner' }
Write-Host "==> Configuring $Repo" -ForegroundColor Cyan

gh api "repos/$Repo" -X PATCH -F allow_auto_merge=true -F allow_squash_merge=true -F delete_branch_on_merge=true | Out-Null
Write-Host "squash + auto-merge + delete-branch-on-merge enabled" -ForegroundColor Green

Write-Host "Merging is handled by the auto-merge workflow on CI success + the 'auto-merge' label." -ForegroundColor Cyan
