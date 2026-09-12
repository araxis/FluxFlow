[CmdletBinding(SupportsShouldProcess)]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$coreProject = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'src/Core/FluxFlow.Core/FluxFlow.Core.csproj'))
$retiredProjects = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
[void]$retiredProjects.Add([IO.Path]::GetFullPath((Join-Path $repositoryRoot 'src/Core/Foundation/FluxFlow.Nodes/FluxFlow.Nodes.csproj')))
[void]$retiredProjects.Add([IO.Path]::GetFullPath((Join-Path $repositoryRoot 'src/Core/Execution/FluxFlow.Coordination/FluxFlow.Coordination.csproj')))
$changes = [Collections.Generic.List[object]]::new()
$projectFiles = & rtk proxy rg --files src tests samples benchmarks eng -g '*.csproj'
if ($LASTEXITCODE -ne 0) { throw 'Project discovery failed.' }

foreach ($relative in $projectFiles) {
    $projectPath = [IO.Path]::GetFullPath((Join-Path $repositoryRoot $relative))
    if (-not $projectPath.StartsWith($repositoryRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Project escaped the workspace: $projectPath"
    }
    if ($retiredProjects.Contains($projectPath) -or $projectPath -eq $coreProject) { continue }
    $text = [IO.File]::ReadAllText($projectPath)
    $directory = [IO.Path]::GetDirectoryName($projectPath)
    $seenCore = $false
    $edits = [Collections.Generic.List[object]]::new()
    $references = [regex]::Matches($text, '(?m)^[\t ]*<ProjectReference\s+Include="(?<path>[^"]+)"\s*/>\r?\n?')
    foreach ($reference in $references) {
        $target = [IO.Path]::GetFullPath((Join-Path $directory $reference.Groups['path'].Value))
        if (-not $retiredProjects.Contains($target) -and $target -ne $coreProject) { continue }
        $replacement = ''
        if (-not $seenCore) {
            $replacementPath = [IO.Path]::GetRelativePath($directory, $coreProject)
            $replacement = $reference.Value.Replace($reference.Groups['path'].Value, $replacementPath)
            $seenCore = $true
        }
        $edits.Add([pscustomobject]@{ Index = $reference.Index; Length = $reference.Length; Replacement = $replacement })
    }
    $updated = $text
    foreach ($edit in ($edits | Sort-Object Index -Descending)) {
        $updated = $updated.Remove($edit.Index, $edit.Length).Insert($edit.Index, $edit.Replacement)
    }
    [xml]$parsed = $updated
    foreach ($reference in $parsed.SelectNodes('//ProjectReference')) {
        $target = [IO.Path]::GetFullPath((Join-Path $directory $reference.Include))
        if ($retiredProjects.Contains($target)) {
            throw "Reference with additional metadata needs explicit review: $relative -> $($reference.Include)"
        }
    }
    if ($updated -ne $text) { $changes.Add([pscustomobject]@{ Path = $projectPath; Content = $updated }) }
}

# Every replacement was validated before any file is changed. This is a mechanical
# project-reference rewrite; existing project properties and unrelated references remain intact.
foreach ($change in $changes) {
    if ($PSCmdlet.ShouldProcess($change.Path, 'Consolidate foundation references into Core')) {
        [IO.File]::WriteAllText($change.Path, $change.Content, [Text.UTF8Encoding]::new($false))
    }
}
[pscustomobject]@{ Projects = $projectFiles.Count; ChangedProjects = $changes.Count } | ConvertTo-Json
