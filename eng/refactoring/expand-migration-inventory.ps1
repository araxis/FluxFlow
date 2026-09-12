param(
    [string]$BaselineDirectory = 'artifacts/core-refactoring/baseline-start'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$baselineRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot $BaselineDirectory))
if (-not $baselineRoot.StartsWith($repositoryRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The baseline directory must be inside the repository.'
}
$inventoryPath = Join-Path $baselineRoot 'migration-inventory.json'
$tablePath = Join-Path $baselineRoot 'migration-inventory.md'
if ((Test-Path -LiteralPath $inventoryPath) -or (Test-Path -LiteralPath $tablePath)) {
    throw 'Migration inventory already exists; use a fresh capture directory.'
}

$projects = @(Get-Content -LiteralPath (Join-Path $baselineRoot 'projects.json') -Raw | ConvertFrom-Json)
$destinations = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'package-destinations.json') -Raw | ConvertFrom-Json -AsHashtable
$sourceProjects = @($projects | Where-Object Path -Like 'src/*')
if ($sourceProjects.Count -ne $destinations.Count) {
    throw 'Every source project must have exactly one explicit package destination entry.'
}

$inventory = foreach ($project in $sourceProjects) {
    if (-not $destinations.ContainsKey($project.PackageId)) {
        throw "Missing destination: $($project.PackageId)"
    }
    $decision = $destinations[$project.PackageId]
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $frontier = [Collections.Generic.Queue[string]]::new()
    $frontier.Enqueue($project.Path)
    while ($frontier.Count -gt 0) {
        $dependency = $frontier.Dequeue()
        foreach ($consumer in $projects) {
            if (($consumer.References -contains $dependency) -and $seen.Add($consumer.Path)) {
                $frontier.Enqueue($consumer.Path)
            }
        }
    }
    $downstream = @($seen | Sort-Object)
    [ordered]@{
        PackageId = $project.PackageId
        Alias = $project.Alias
        CurrentProject = $project.Path
        Frameworks = $project.Frameworks
        Action = $decision.action
        Targets = @($decision.targets)
        Responsibility = $decision.responsibility
        Status = 'pending'
        ProjectReferences = @($project.References)
        PackageReferences = @($project.Packages)
        DirectConsumers = @($projects | Where-Object { $_.References -contains $project.Path } | ForEach-Object Path)
        TransitiveConsumers = $downstream
        AffectedTests = @($downstream | Where-Object { $_ -like 'tests/*' })
        AffectedSamples = @($downstream | Where-Object { $_ -like 'samples/*' })
    }
}
$inventory | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $inventoryPath -Encoding utf8

$lines = [Collections.Generic.List[string]]::new()
$lines.Add('# Source package migration inventory')
$lines.Add('')
$lines.Add('Generated from the captured project XML and explicit destination decisions. All rows are pending migration and verification. Framework targets are preserved. JSON includes current project paths, direct references, transitive consumers, affected tests and samples; package-only consumers are separately recorded in projects.json and must be exercised by package acceptance.')
$lines.Add('')
$lines.Add('| Current package | Manifest alias | Frameworks | Action | Destination | Responsibility | Status |')
$lines.Add('|---|---|---|---|---|---|---|')
foreach ($entry in $inventory) {
    $lines.Add("| $($entry.PackageId) | $($entry.Alias) | $($entry.Frameworks) | $($entry.Action) | $($entry.Targets -join ', ') | $($entry.Responsibility) | pending |")
}
$lines | Set-Content -LiteralPath $tablePath -Encoding utf8
Write-Output "Mapped $($inventory.Count) source packages. All dispositions remain pending implementation and verification."
