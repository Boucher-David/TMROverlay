param(
    [Parameter(Mandatory = $true)]
    [string]$ResultsDirectory
)

function Escape-AnnotationValue {
    param([AllowNull()][string]$Value)

    if ($null -eq $Value) {
        return ""
    }

    return $Value.Replace("%", "%25").Replace("`r", "%0D").Replace("`n", "%0A")
}

function Escape-AnnotationProperty {
    param([AllowNull()][string]$Value)

    return (Escape-AnnotationValue $Value).Replace(",", "%2C").Replace(":", "%3A")
}

function Resolve-StackLocation {
    param([AllowNull()][string]$StackTrace)

    if ([string]::IsNullOrWhiteSpace($StackTrace)) {
        return $null
    }

    $match = [regex]::Match($StackTrace, '\s+in\s+(?<path>.+?):line\s+(?<line>\d+)')
    if (-not $match.Success) {
        return $null
    }

    $path = $match.Groups["path"].Value
    if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_WORKSPACE)) {
        $workspace = [System.IO.Path]::GetFullPath($env:GITHUB_WORKSPACE)
        $fullPath = [System.IO.Path]::GetFullPath($path)
        if ($fullPath.StartsWith($workspace, [System.StringComparison]::OrdinalIgnoreCase)) {
            $path = $fullPath.Substring($workspace.Length).TrimStart([char[]]@('\', '/'))
        }
    }

    return [pscustomobject]@{
        Path = $path.Replace('\', '/')
        Line = $match.Groups["line"].Value
    }
}

$trxFiles = @(Get-ChildItem -Path $ResultsDirectory -Filter "*.trx" -Recurse -ErrorAction SilentlyContinue)
if ($trxFiles.Count -eq 0) {
    Write-Host "::warning::No TRX test result files were found under $(Escape-AnnotationValue $ResultsDirectory)."
    exit 0
}

$failureCount = 0
foreach ($trxFile in $trxFiles) {
    try {
        [xml]$document = Get-Content -Raw -LiteralPath $trxFile.FullName
    }
    catch {
        Write-Host "::warning file=$(Escape-AnnotationProperty $trxFile.FullName)::Could not parse TRX file: $(Escape-AnnotationValue $_.Exception.Message)"
        continue
    }

    $namespace = New-Object System.Xml.XmlNamespaceManager($document.NameTable)
    $namespace.AddNamespace("trx", "http://microsoft.com/schemas/VisualStudio/TeamTest/2010")
    $results = $document.SelectNodes('//trx:UnitTestResult[@outcome="Failed"]', $namespace)
    foreach ($result in $results) {
        $failureCount++
        $testName = $result.testName
        $message = $result.Output.ErrorInfo.Message
        $stackTrace = $result.Output.ErrorInfo.StackTrace
        $body = "$testName failed."
        if (-not [string]::IsNullOrWhiteSpace($message)) {
            $body = "$body $message"
        }

        $location = Resolve-StackLocation $stackTrace
        if ($null -ne $location) {
            Write-Host "::error file=$(Escape-AnnotationProperty $location.Path),line=$(Escape-AnnotationProperty $location.Line),title=$(Escape-AnnotationProperty $testName)::$(Escape-AnnotationValue $body)"
        }
        else {
            Write-Host "::error title=$(Escape-AnnotationProperty $testName)::$(Escape-AnnotationValue $body)"
        }
    }
}

if ($failureCount -eq 0) {
    Write-Host "::warning::TRX files were found, but no failed UnitTestResult entries were present."
}
