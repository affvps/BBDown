$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$project = [xml](Get-Content -Raw -LiteralPath "$root/BBDown/BBDown.csproj")
$rids = @($project.Project.PropertyGroup.RuntimeIdentifiers | Where-Object { $_ })[0].Split(';')
$assets = Get-Content -Raw -LiteralPath "$root/BBDown/obj/project.assets.json" | ConvertFrom-Json
$targets = @($assets.targets.PSObject.Properties.Name)
foreach ($rid in $rids) {
    if ("net10.0/$rid" -notin $targets) {
        throw "Restore did not include publish target $rid. Restore the complete graph without -r."
    }
}
Write-Host "Locked restore contains all $($rids.Count) publish targets: $($rids -join ', ')."
