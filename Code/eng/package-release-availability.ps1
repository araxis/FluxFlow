param(
    [Parameter(Mandatory = $true)]
    [string] $Package,

    [string] $Version = "",

    [string] $ManifestPath = "eng/packages.json",

    [string] $PackageSource = "https://api.nuget.org/v3/index.json",

    [ValidateSet("Any", "Missing", "Present")]
    [string] $ExpectedState = "Any",

    [ValidateRange(1, 300)]
    [int] $TimeoutSeconds = 30
)

$ErrorActionPreference = "Stop"

function Read-KeyValueFile {
    param([string] $Path)

    $values = @{}
    foreach ($line in Get-Content -LiteralPath $Path) {
        if ([string]::IsNullOrWhiteSpace($line)) {
            continue
        }

        $separator = $line.IndexOf("=")
        if ($separator -le 0) {
            continue
        }

        $key = $line.Substring(0, $separator).Trim()
        $value = $line.Substring($separator + 1).Trim()
        $values[$key] = $value
    }

    return $values
}

function Require-Value {
    param(
        [hashtable] $Values,
        [string] $Key
    )

    if (-not $Values.ContainsKey($Key) -or [string]::IsNullOrWhiteSpace($Values[$Key])) {
        throw "Resolved release value '$Key' is missing."
    }

    return $Values[$Key]
}

function Resolve-HttpUri {
    param(
        [string] $Value,
        [string] $Label
    )

    $uri = $null
    if (-not [System.Uri]::TryCreate($Value, [System.UriKind]::Absolute, [ref] $uri) -or
        $uri.Scheme -notin "http", "https") {
        throw "$Label '$Value' must be an absolute HTTP or HTTPS URI."
    }

    if (-not [string]::IsNullOrWhiteSpace($uri.UserInfo)) {
        throw "$Label must not contain embedded credentials."
    }

    return $uri
}

function Invoke-JsonRequest {
    param(
        [System.Uri] $Uri,
        [switch] $AllowNotFound
    )

    try {
        $content = Invoke-RestMethod -Uri $Uri -Method Get -TimeoutSec $TimeoutSeconds
        return [pscustomobject]@{
            Found = $true
            Content = $content
        }
    }
    catch {
        $response = $_.Exception.Response
        $statusCode = if ($null -eq $response) { $null } else { [int] $response.StatusCode }
        if ($AllowNotFound -and $statusCode -eq 404) {
            return [pscustomobject]@{
                Found = $false
                Content = $null
            }
        }

        $status = if ($null -eq $statusCode) { "no HTTP status" } else { "HTTP $statusCode" }
        throw "Package availability request to '$Uri' failed ($status): $($_.Exception.Message)"
    }
}

if ([string]::IsNullOrWhiteSpace($Package)) {
    throw "Package is required."
}

$repoRoot = (Get-Location).Path
$resolverPath = Join-Path $repoRoot "eng/resolve-package-release.ps1"
if (-not (Test-Path -LiteralPath $resolverPath -PathType Leaf)) {
    throw "Release resolver '$resolverPath' was not found."
}

$environmentPath = Join-Path ([System.IO.Path]::GetTempPath()) "fluxflow-availability-$([Guid]::NewGuid().ToString('N')).env"

try {
    $resolveArgs = @{
        Package = $Package
        ManifestPath = $ManifestPath
        EnvironmentPath = $environmentPath
    }

    if (-not [string]::IsNullOrWhiteSpace($Version)) {
        $resolveArgs.Version = $Version
    }

    & $resolverPath @resolveArgs 6> $null

    $resolved = Read-KeyValueFile $environmentPath
    $packageAlias = Require-Value $resolved "PACKAGE_ALIAS"
    $packageId = Require-Value $resolved "PACKAGE_ID"
    $packageVersion = Require-Value $resolved "PACKAGE_VERSION"

    $serviceIndexUri = Resolve-HttpUri $PackageSource "Package source"
    $serviceIndexResponse = Invoke-JsonRequest $serviceIndexUri
    $serviceIndex = $serviceIndexResponse.Content
    if ($null -eq $serviceIndex -or $null -eq $serviceIndex.resources) {
        throw "Package source '$serviceIndexUri' did not return a V3 service index with resources."
    }

    $flatContainerResource = $serviceIndex.resources |
        Where-Object { @($_.'@type') -contains "PackageBaseAddress/3.0.0" } |
        Select-Object -First 1

    if ($null -eq $flatContainerResource -or
        [string]::IsNullOrWhiteSpace([string] $flatContainerResource.'@id')) {
        throw "Package source '$serviceIndexUri' does not expose PackageBaseAddress/3.0.0."
    }

    $flatContainerBase = Resolve-HttpUri ([string] $flatContainerResource.'@id') "Flat-container address"
    $baseText = $flatContainerBase.AbsoluteUri
    if (-not $baseText.EndsWith("/", [System.StringComparison]::Ordinal)) {
        $baseText += "/"
    }

    $normalizedId = $packageId.ToLowerInvariant()
    $indexUri = Resolve-HttpUri "$baseText$normalizedId/index.json" "Package index"
    $packageIndexResponse = Invoke-JsonRequest $indexUri -AllowNotFound
    if ($packageIndexResponse.Found -and $null -eq $packageIndexResponse.Content.versions) {
        throw "Package index '$indexUri' did not return a versions collection."
    }

    $isPresent = $packageIndexResponse.Found -and
        @($packageIndexResponse.Content.versions) -contains $packageVersion
    $state = if ($isPresent) { "Present" } else { "Missing" }

    Write-Host "PACKAGE_ALIAS=$packageAlias"
    Write-Host "PACKAGE_ID=$packageId"
    Write-Host "PACKAGE_VERSION=$packageVersion"
    Write-Host "PACKAGE_AVAILABILITY=$state"

    if ($ExpectedState -ne "Any" -and $state -ne $ExpectedState) {
        throw "Package '$packageId' version '$packageVersion' is $state; expected $ExpectedState."
    }
}
finally {
    Remove-Item -LiteralPath $environmentPath -Force -ErrorAction SilentlyContinue
}
