# Hardware-free acceptance check against an actual dual-target plugin package.
[CmdletBinding()]
param([Parameter(Mandatory)][string]$Package)
$ErrorActionPreference = 'Stop'
$archive = (Get-Item -LiteralPath $Package).FullName
$temp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar)
$work = [IO.Path]::GetFullPath((Join-Path $temp ('fluxflow-sources-' + [guid]::NewGuid().ToString('N'))))
if ([IO.Path]::GetDirectoryName($work) -ne $temp) { throw 'Unexpected test directory.' }
$store = Join-Path $work 'store'
$folder = Join-Path $work 'input/Module'
$zip = Join-Path $work 'module.zip'
$installer = Join-Path $PSScriptRoot 'fluxflow-plugin.ps1'
try {
    [IO.Compression.ZipFile]::ExtractToDirectory($archive,$folder)
    $id = (Get-Content -LiteralPath (Join-Path $folder 'fluxflow-plugin.json') -Raw | ConvertFrom-Json).id
    $asset = 'example[1].txt'
    [IO.File]::WriteAllText((Join-Path $folder $asset),'original')
    [IO.Compression.ZipFile]::CreateFromDirectory((Join-Path $work 'input'),$zip)
    foreach ($source in $folder,$zip,$archive) {
        & $installer Install -Source $source -Directory $store
        $entry = & $installer List -Directory $store
        $sourceLabel = if ($source -eq $folder) { 'Folder' } elseif ($source -eq $zip) { 'ZIP' } else { 'Local NuGet' }
        if ($entry.Id -cne $id -or $entry.Source -ne $sourceLabel) { throw "Incorrect identity or source after installing $source." }
        $installed = Join-Path $store "active/$id"
        foreach ($tfm in 'net8.0','net10.0') {
            $dll = "lib/$tfm/$id.dll"
            if ((Get-FileHash -LiteralPath (Join-Path $installed $dll)).Hash -ne (Get-FileHash -LiteralPath (Join-Path $folder $dll)).Hash) {
                throw "Installed assembly differs for $tfm."
            }
        }
        if ($source -ne $archive -and [IO.File]::ReadAllText((Join-Path $installed $asset)) -ne 'original') { throw 'Lost plugin asset.' }
        & $installer Remove -Id $id -Directory $store
        if ((Test-Path -LiteralPath $installed) -or (& $installer List -Directory $store)) { throw 'Plugin still active after removal.' }
    }
    $bad = Join-Path $work 'bad'
    [void][IO.Directory]::CreateDirectory($bad)
    Copy-Item -LiteralPath (Join-Path $folder 'fluxflow-plugin.json') -Destination $bad
    $rejected = $false
    try { & $installer Install -Source $bad -Directory $store } catch { $rejected = $true }
    if (!$rejected -or (& $installer List -Directory $store)) { throw 'An incomplete plugin was activated.' }
    $manifest = Join-Path $folder 'fluxflow-plugin.json'
    $data = Get-Content -LiteralPath $manifest -Raw | ConvertFrom-Json -AsHashtable
    $data['version'] = @{ value = 1 }
    $data | ConvertTo-Json | Set-Content -LiteralPath $manifest
    $rejected = $false
    try { & $installer Install -Source $folder -Directory $store -NextStart } catch { $rejected = $true }
    if (!$rejected -or (& $installer List -NextStart -Directory $store)) { throw 'A non-text version was staged.' }
    # Simulate a change already queued by an earlier installer. Studio must still be able to list and cancel it.
    $oldEntry = Join-Path $store "pending/add/$id"
    [void][IO.Directory]::CreateDirectory($oldEntry)
    Copy-Item -LiteralPath $manifest -Destination $oldEntry
    $rows = & $installer List -NextStart -AsJson -Directory $store | ConvertFrom-Json
    if ($rows.Id -ne $id -or $null -ne $rows.Version -or $rows.State -ne 'PendingInstall') { throw 'Cannot read old pending entry.' }
    & $installer Cancel -NextStart -Id $id -Directory $store
    if (& $installer List -NextStart -Directory $store) { throw 'Old pending entry was not cancelled.' }
    $bundle = Join-Path $work 'bundle'
    $fresh = Join-Path $work 'new-store'
    [void][IO.Directory]::CreateDirectory($bundle)
    Copy-Item -LiteralPath $archive -Destination (Join-Path $bundle 'one.nupkg')
    Copy-Item -LiteralPath $archive -Destination (Join-Path $bundle 'duplicate.nupkg')
    $rejected = $false
    try { & $installer Initialize -Source $bundle -Directory $fresh } catch { $rejected = $true }
    if (!$rejected -or (Test-Path -LiteralPath (Join-Path $fresh 'active'))) { throw 'Failed bundle left a partial installation.' }
    [IO.File]::Delete((Join-Path $bundle 'duplicate.nupkg'))
    & $installer Initialize -Source $bundle -Directory $fresh
    if ((& $installer List -Directory $fresh).Id -ne $id) { throw 'Fresh store did not load the default plugin.' }
    & $installer Remove -Id $id -Directory $fresh
    & $installer Initialize -Source $bundle -Directory $fresh
    if (& $installer List -Directory $fresh) { throw 'An explicitly removed default was reinstalled.' }
    Write-Output "Verified $id from folder, ZIP and NuGet, atomic defaults and removal across restart."
}
finally { if (Test-Path -LiteralPath $work) { Remove-Item -LiteralPath $work -Force -Recurse } }
