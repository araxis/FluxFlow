param(
    [string] $ManifestPath = "eng/packages.json",

    [string[]] $AlreadyAvailable = @(),

    [switch] $AsJson
)

$ErrorActionPreference = "Stop"

function Normalize-Path {
    param(
        [string] $BasePath,
        [string] $Path
    )

    $platformPath = $Path.Replace('/', [System.IO.Path]::DirectorySeparatorChar)
    $platformPath = $platformPath.Replace('\', [System.IO.Path]::DirectorySeparatorChar)
    return [System.IO.Path]::GetFullPath((Join-Path $BasePath $platformPath))
}

function Read-Property {
    param(
        [xml] $Project,
        [string] $Name
    )

    return $Project.Project.PropertyGroup |
        ForEach-Object { $_.$Name } |
        Where-Object { -not [string]::IsNullOrWhiteSpace([string] $_) } |
        Select-Object -First 1
}

$manifestFullPath = [System.IO.Path]::GetFullPath($ManifestPath)
if (-not (Test-Path -LiteralPath $manifestFullPath -PathType Leaf)) {
    throw "Package manifest '$ManifestPath' was not found."
}

$manifestDirectory = Split-Path -Parent $manifestFullPath
$repositoryRoot = Split-Path -Parent $manifestDirectory
$packages = Get-Content -LiteralPath $manifestFullPath -Raw | ConvertFrom-Json
$packages = @($packages)
if ($packages.Count -eq 0) {
    throw "Package manifest '$ManifestPath' is empty."
}

$byAlias = [System.Collections.Generic.Dictionary[string, object]]::new([System.StringComparer]::OrdinalIgnoreCase)
$byProject = [System.Collections.Generic.Dictionary[string, string]]::new([System.StringComparer]::OrdinalIgnoreCase)

foreach ($package in $packages) {
    if ([string]::IsNullOrWhiteSpace([string] $package.alias) -or
        [string]::IsNullOrWhiteSpace([string] $package.packageId) -or
        [string]::IsNullOrWhiteSpace([string] $package.project)) {
        throw "Every package manifest entry must define alias, packageId, and project."
    }

    if ($byAlias.ContainsKey([string] $package.alias)) {
        throw "Package alias '$($package.alias)' is duplicated."
    }

    $projectPath = Normalize-Path $repositoryRoot ([string] $package.project)
    if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
        throw "Package project '$($package.project)' was not found."
    }

    if ($byProject.ContainsKey($projectPath)) {
        throw "Package project '$($package.project)' is duplicated in the manifest."
    }

    $byAlias.Add([string] $package.alias, $package)
    $byProject.Add($projectPath, [string] $package.alias)
}

$dependencies = [System.Collections.Generic.Dictionary[string, string[]]]::new([System.StringComparer]::OrdinalIgnoreCase)

foreach ($package in $packages) {
    $projectPath = Normalize-Path $repositoryRoot ([string] $package.project)
    [xml] $project = Get-Content -LiteralPath $projectPath -Raw
    $dependencyAliases = [System.Collections.Generic.List[string]]::new()

    foreach ($reference in @($project.Project.ItemGroup.ProjectReference)) {
        if ($null -eq $reference -or [string]::IsNullOrWhiteSpace([string] $reference.Include)) {
            continue
        }

        $referencePath = Normalize-Path (Split-Path -Parent $projectPath) ([string] $reference.Include)
        if ($byProject.ContainsKey($referencePath)) {
            $dependencyAliases.Add($byProject[$referencePath])
            continue
        }

        if (-not (Test-Path -LiteralPath $referencePath -PathType Leaf)) {
            throw "Project reference '$($reference.Include)' from '$($package.alias)' was not found."
        }

        [xml] $referencedProject = Get-Content -LiteralPath $referencePath -Raw
        $referencedPackageId = [string] (Read-Property $referencedProject "PackageId")
        if (-not [string]::IsNullOrWhiteSpace($referencedPackageId)) {
            throw "Package project '$referencePath' is referenced by '$($package.alias)' but is missing from '$ManifestPath'."
        }
    }

    $dependencies.Add(
        [string] $package.alias,
        @($dependencyAliases | Sort-Object -Unique))
}

$available = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($value in $AlreadyAvailable) {
    if ([string]::IsNullOrWhiteSpace($value)) {
        continue
    }

    $trimmed = $value.Trim()
    if (-not $byAlias.ContainsKey($trimmed)) {
        throw "Already-available package alias '$trimmed' is not present in '$ManifestPath'."
    }

    [void] $available.Add([string] $byAlias[$trimmed].alias)
}

$remaining = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($package in $packages) {
    if (-not $available.Contains([string] $package.alias)) {
        [void] $remaining.Add([string] $package.alias)
    }
}

$waves = [System.Collections.Generic.List[object]]::new()
while ($remaining.Count -gt 0) {
    $wave = @($remaining |
        Where-Object {
            $alias = $_
            @($dependencies[$alias] | Where-Object { $remaining.Contains($_) }).Count -eq 0
        } |
        Sort-Object)

    if ($wave.Count -eq 0) {
        $cycleAliases = @($remaining | Sort-Object)
        throw "Package dependency cycle detected among: $($cycleAliases -join ', ')."
    }

    $waves.Add($wave)
    foreach ($alias in $wave) {
        [void] $remaining.Remove($alias)
        [void] $available.Add($alias)
    }
}

$reused = @($AlreadyAvailable |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
    ForEach-Object { [string] $byAlias[$_.Trim()].alias } |
    Sort-Object -Unique)
$targetCount = $packages.Count - $reused.Count

if ($AsJson) {
    [pscustomobject]@{
        packageCount = $packages.Count
        reused = $reused
        targetCount = $targetCount
        waves = @($waves)
    } | ConvertTo-Json -Depth 5
    return
}

Write-Host "PACKAGE_COUNT=$($packages.Count)"
Write-Host "PACKAGE_REUSED=$($reused -join ',')"
Write-Host "PACKAGE_TARGET_COUNT=$targetCount"
Write-Host "PACKAGE_WAVE_COUNT=$($waves.Count)"
for ($index = 0; $index -lt $waves.Count; $index++) {
    Write-Host "PACKAGE_WAVE_$($index + 1)=$($waves[$index] -join ',')"
}
