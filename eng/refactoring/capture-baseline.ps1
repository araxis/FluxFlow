[CmdletBinding()]
param([string] $OutputDirectory = 'artifacts/core-refactoring/baseline-initial')

$ErrorActionPreference = 'Stop'
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$outputPath = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $OutputDirectory))
if (!$outputPath.StartsWith($repositoryRoot + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Baseline output must remain within the repository.'
}
if (Test-Path -LiteralPath $outputPath) { throw 'Choose a fresh baseline directory; existing evidence is never overwritten.' }
$null = New-Item -ItemType Directory -Path $outputPath

Push-Location $repositoryRoot
try {
    $status = @(& rtk proxy git status --porcelain=v1 --untracked-files=all)
    if ($LASTEXITCODE) { throw 'Cannot capture git status.' }
    $status | Set-Content -LiteralPath (Join-Path $outputPath 'git-status.txt') -Encoding utf8
    $workingDiffPath = Join-Path $outputPath 'working-tree.patch'
    & rtk proxy git diff --binary "--output=$workingDiffPath"
    if ($LASTEXITCODE) { throw 'Cannot capture working-tree diff.' }
    $indexDiffPath = Join-Path $outputPath 'index.patch'
    & rtk proxy git diff --cached --binary "--output=$indexDiffPath"
    if ($LASTEXITCODE) { throw 'Cannot capture staged diff.' }

    $changedPaths = @(& rtk proxy git diff --name-only) + @(& rtk proxy git diff --cached --name-only) + @(& rtk proxy git ls-files --others --exclude-standard)
    $hashes = @(foreach ($changedPath in ($changedPaths | Sort-Object -Unique)) {
        if (Test-Path -LiteralPath $changedPath -PathType Leaf) {
            [pscustomobject]@{ Path = $changedPath; Sha256 = (Get-FileHash -LiteralPath $changedPath -Algorithm SHA256).Hash }
        }
    })
    $hashes | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $outputPath 'changed-file-hashes.json') -Encoding utf8

    $manifest = Get-Content -LiteralPath 'eng/packages.json' -Raw | ConvertFrom-Json
    $projectPaths = @(& rtk proxy rg --files src tests samples benchmarks eng -g '*.csproj' -g '!**/bin/**' -g '!**/obj/**')
    $projects = @(foreach ($projectPath in ($projectPaths | Sort-Object)) {
        $normalized = $projectPath.Replace('\', '/')
        [xml]$project = Get-Content -LiteralPath $projectPath -Raw
        $entry = $manifest | Where-Object { $_.project -eq $normalized } | Select-Object -First 1
        $references = @($project.SelectNodes('//ProjectReference') | ForEach-Object {
            $resolved = [System.IO.Path]::GetFullPath((Join-Path (Split-Path $projectPath -Parent) $_.Include))
            [System.IO.Path]::GetRelativePath($repositoryRoot, $resolved).Replace('\', '/')
        })
        [pscustomobject]@{
            Path = $normalized
            PackageId = ($project.SelectNodes('//PackageId') | ForEach-Object InnerText) -join ';'
            Alias = $entry.alias
            Frameworks = ($project.SelectNodes('//TargetFramework|//TargetFrameworks') | ForEach-Object InnerText) -join ';'
            References = $references
            Packages = @($project.SelectNodes('//PackageReference') | ForEach-Object { $_.Include })
        }
    })
    $projects | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $outputPath 'projects.json') -Encoding utf8
    Copy-Item -LiteralPath 'eng/packages.json' -Destination (Join-Path $outputPath 'packages.json')
    $componentDeclarations = @(& rtk proxy rg -n 'public (sealed |abstract )?(partial )?class .*(: .*FlowNode|: .*IFlowNode|: .*FlowSource)' src/Components -g '*.cs' -g '!**/bin/**' -g '!**/obj/**')
    $componentDeclarations | Set-Content -LiteralPath (Join-Path $outputPath 'component-declarations.txt') -Encoding utf8
    $metadata = [ordered]@{
        CapturedUtc = [DateTimeOffset]::UtcNow.ToString('O')
        Commit = (& rtk proxy git rev-parse HEAD)
        Sdk = (& rtk proxy dotnet --version)
        SourceProjects = @($projects | Where-Object { $_.Path.StartsWith('src/') }).Count
        AllProjects = $projects.Count
        ManifestPackages = $manifest.Count
        ChangedFilesHashed = $hashes.Count
        Note = 'Project XML inventory, not evaluated MSBuild graph. Component declaration discovery is lexical, not an exhaustive public API manifest.'
    }
    $metadata | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $outputPath 'metadata.json') -Encoding utf8
    $metadata | ConvertTo-Json
}
finally { Pop-Location }
