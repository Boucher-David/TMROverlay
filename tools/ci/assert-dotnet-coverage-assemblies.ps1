param(
    [Parameter(Mandatory = $false)]
    [string]$CoveragePath = "artifacts\coverage\dotnet\Cobertura.xml",

    [Parameter(Mandatory = $false)]
    [string[]]$RequiredAssemblies = @("TMROverlay", "TmrOverlay.Core"),

    [Parameter(Mandatory = $false)]
    [string[]]$ForbiddenAssemblies = @("TmrOverlay.App.Tests")
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $CoveragePath -PathType Leaf)) {
    throw "Coverage report is missing: $CoveragePath"
}

[xml]$coverage = Get-Content -LiteralPath $CoveragePath
$assemblies = @($coverage.coverage.packages.package | ForEach-Object { $_.name } | Where-Object { $_ })

if ($assemblies.Count -eq 0) {
    throw "Coverage report has no package/assembly entries: $CoveragePath"
}

$missingAssemblies = @($RequiredAssemblies | Where-Object { $assemblies -notcontains $_ })
if ($missingAssemblies.Count -gt 0) {
    throw "Coverage report is missing required assemblies: $($missingAssemblies -join ', '). Found: $($assemblies -join ', ')"
}

$includedForbiddenAssemblies = @($ForbiddenAssemblies | Where-Object { $assemblies -contains $_ })
if ($includedForbiddenAssemblies.Count -gt 0) {
    throw "Coverage report includes forbidden test assemblies: $($includedForbiddenAssemblies -join ', ')"
}

Write-Host "Coverage report assemblies: $($assemblies -join ', ')"
