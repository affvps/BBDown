param(
    [Parameter(Mandatory = $true)][string]$LogPath,
    [string]$BaselinePath = "$PSScriptRoot/aot-warnings-baseline.txt"
)

$ErrorActionPreference = 'Stop'
$baseline = @(Get-Content -LiteralPath $BaselinePath | Where-Object { $_ -and -not $_.StartsWith('#') })
$warnings = @(Get-Content -LiteralPath $LogPath | ForEach-Object {
    if ($_ -match '\bwarning (IL\d{4}): (.+?)(?:\s+\[[^\]]+\.csproj\])?$') {
        $matches[1] + ': ' + $matches[2]
    }
} | Sort-Object -Unique)
$unexpected = @($warnings | Where-Object { $_ -notin $baseline })
if ($unexpected.Count -gt 0) {
    $unexpected | ForEach-Object { Write-Host "Unexpected AOT/trim warning: $_" }
    throw "Found $($unexpected.Count) AOT/trim warnings outside the reviewed baseline."
}
Write-Host "AOT/trim audit passed: $($warnings.Count) known warnings; no new warnings."
