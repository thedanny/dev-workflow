<#
.SYNOPSIS
Creates .env for compose.yaml with a random database password and one API key per role.

.DESCRIPTION
The server only ever sees the key hashes; the raw keys are printed once, here.

.PARAMETER Force
Replace an existing .env, which invalidates the current keys.
#>
param([switch]$Force)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$target = Join-Path $root '.env'

if ((Test-Path $target) -and -not $Force) {
    Write-Error '.env already exists. Pass -Force to replace it, which invalidates the current keys.'
}

function New-RandomHex {
    $bytes = [System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32)
    [System.Convert]::ToHexString($bytes).ToLowerInvariant()
}

function Get-Sha256Hex([string]$value) {
    $hash = [System.Security.Cryptography.SHA256]::HashData([System.Text.Encoding]::UTF8.GetBytes($value))
    [System.Convert]::ToHexString($hash).ToLowerInvariant()
}

$keys = [ordered]@{
    listener  = New-RandomHex
    evaluator = New-RandomHex
    approver  = New-RandomHex
    executor  = New-RandomHex
}

$content = (Get-Content (Join-Path $root '.env.example') -Raw) -replace "`r`n", "`n"
$content = $content -replace '(?m)^POSTGRES_PASSWORD=.*$', "POSTGRES_PASSWORD=$(New-RandomHex)"
foreach ($role in $keys.Keys) {
    $name = "$($role.ToUpperInvariant())_KEY_HASH"
    $content = $content -replace "(?m)^$name=.*$", "$name=$(Get-Sha256Hex $keys[$role])"
}

[System.IO.File]::WriteAllText($target, $content)

Write-Output 'Wrote .env. Store these API keys somewhere safe; they are not saved anywhere:'
Write-Output ''
foreach ($role in $keys.Keys) {
    Write-Output ('  {0,-10} {1}' -f $role, $keys[$role])
}
