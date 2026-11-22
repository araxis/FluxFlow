[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('Install','Remove','List','Cancel','ApplyPending','Initialize')][string]$Action,
    [ValidatePattern('\A[A-Za-z0-9][A-Za-z0-9._-]*\z')][string]$Id,
    [ValidatePattern('\A[0-9]+(\.[0-9]+){2}(-[A-Za-z0-9.-]+)?\z')][string]$Version,
    [string]$Feed,
    [string]$Directory = $env:FLUXFLOW_PLUGIN_DIRECTORY,
    [string]$Source,
    [ValidateSet('Folder','NuGet feed')][string]$SourceKind,
    [switch]$BoundedArchive,
    [switch]$NextStart,
    [switch]$AsJson
)
$ErrorActionPreference = 'Stop'
if (!$Directory) { $Directory = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'FluxFlow/Plugins' }
$root = [IO.Directory]::CreateDirectory($Directory).FullName
$active = Join-Path $root 'active'

# Only mutate direct children of a known store directory; never follow directory links.
function ChildPath($parent, $name) {
    $path = [IO.Path]::GetFullPath((Join-Path $parent $name))
    if ([IO.Path]::GetDirectoryName($path) -ne $parent) { throw "Invalid plugin path: $name" }
    return $path
}
function RejectLink($item) {
    if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Linked paths are unsupported: $($item.FullName)" }
}
function ReadManifest($folder) {
    $m = Get-Content -LiteralPath (Join-Path $folder 'fluxflow-plugin.json') -Raw | ConvertFrom-Json
    if (@('contract','id','entryType').Where({ $m.PSObject.Properties.Name -cnotcontains $_ }).Count -or
        ($m.contract -isnot [int] -and $m.contract -isnot [long]) -or $m.contract -ne 1 -or
        $m.id -isnot [string] -or $m.id -notmatch '\A[A-Za-z0-9][A-Za-z0-9._-]*\z' -or
        $m.entryType -isnot [string] -or [string]::IsNullOrWhiteSpace($m.entryType)) { throw "Invalid plugin manifest in $folder." }
    return $m
}
function PackageMetadata($folder) {
    $specs = @(Get-ChildItem -LiteralPath $folder -Filter '*.nuspec' -File)
    if ($specs.Count -eq 1) { return ([xml](Get-Content -LiteralPath $specs[0].FullName -Raw)).package.metadata }
}
function CopyPayload($folder, $to) {
    foreach ($item in Get-ChildItem -LiteralPath $folder -Force) {
        RejectLink $item
        $dest = Join-Path $to $item.Name
        if ($item.PSIsContainer) {
            [void][IO.Directory]::CreateDirectory($dest)
            CopyPayload $item.FullName $dest
        } else { [IO.File]::Copy($item.FullName,$dest) }
    }
}
$pending = Join-Path $root 'pending'
$adds = Join-Path $pending 'add'
$removes = Join-Path $pending 'remove'
foreach ($path in $active,$pending,$adds,$removes) {
    if (Test-Path -LiteralPath $path) { RejectLink (Get-Item -LiteralPath $path) }
}
function Entries($folder, $state) {
    if ([IO.Directory]::Exists($folder)) {
        foreach ($item in Get-ChildItem -LiteralPath $folder -Directory) {
            $m = ReadManifest $item.FullName
            $listedVersion = $m.version
            if (!$listedVersion) { $listedVersion = (PackageMetadata $item.FullName).version }
            # Older installers accepted non-text versions. Keep these entries removable.
            if ($listedVersion -isnot [string]) { $listedVersion = $null }
            $originFile = Join-Path $item.FullName '.install-source'
            $origin = if ([IO.File]::Exists($originFile)) { [IO.File]::ReadAllText($originFile) }
            [pscustomobject]@{ Id = $m.id; Version = $listedVersion; State = $state; Source = $origin }
        }
    }
}
function IsInstalled($name) {
    return [IO.Directory]::Exists((ChildPath $active $name)) -or [IO.File]::Exists((ChildPath $root "$name.installed"))
}
function Detach($parent,$name) {
    $target = ChildPath $parent $name
    RejectLink (Get-Item -LiteralPath $target)
    if ((ReadManifest $target).id -ine $name) { throw 'Unexpected plugin identity.' }
    $discard = ChildPath $root ('.remove-' + [guid]::NewGuid().ToString('N'))
    [IO.Directory]::Move($target,$discard)
    Remove-Item -LiteralPath $discard -Force -Recurse
}
function RemoveInstalled($name) {
    if ([IO.Directory]::Exists((ChildPath $active $name))) { Detach $active $name }
    else { [IO.File]::Delete((ChildPath $root "$name.installed")) }
}
$wait = [Diagnostics.Stopwatch]::StartNew()
while ($true) {
    try { $changes = [IO.File]::Open((Join-Path $root '.changes.lock'),'OpenOrCreate','ReadWrite','None'); break }
    catch [IO.IOException] {
        if (($_.Exception.GetBaseException().HResult -band 0xffff) -notin 11,32,33) { throw }
        if ($wait.Elapsed.TotalSeconds -ge 10) { throw 'Another plugin change is in progress. Try again when it finishes.' }
        Start-Sleep -Milliseconds 50
    }
}
$lock = $null
try {
    if ($Action -eq 'List') {
        $entries = @(Entries $active 'Active')
        foreach ($receipt in Get-ChildItem -LiteralPath $root -Filter '*.installed' -File) {
            $entries += [pscustomobject]@{ Id = $receipt.BaseName; Version = [IO.File]::ReadAllText($receipt.FullName).Trim(); State = 'Active' }
        }
        if ($NextStart) {
            foreach ($row in $entries) {
                if ([IO.File]::Exists((ChildPath $removes $row.Id))) { $row.State = 'PendingRemoval' }
            }
            $entries += @(Entries $adds 'PendingInstall')
        }
        if ($AsJson) { ConvertTo-Json -InputObject $entries -Compress } else { $entries }
        return
    }
    if ($Action -eq 'Initialize') {
        # The active directory survives removal of its last package. Existing and legacy stores belong to the user.
        if ([IO.Directory]::Exists($active) -or [IO.Directory]::Exists($pending) -or
            @(Get-ChildItem -LiteralPath $root -Filter '*.installed' -File).Count) { return }
    }
    if (!$NextStart -or $Action -eq 'ApplyPending') {
        try { $lock = [IO.File]::Open((Join-Path $root '.store.lock'),'OpenOrCreate','ReadWrite','None') }
        catch [IO.IOException] {
            if ($Action -in 'ApplyPending','Initialize' -and ($_.Exception.GetBaseException().HResult -band 0xffff) -in 11,32,33) { return }
            throw
        }
    }
    if ($Action -eq 'Initialize') {
        $bundle = Get-Item -LiteralPath $Source
        RejectLink $bundle
        $packages = @(Get-ChildItem -LiteralPath $bundle.FullName -Filter '*.nupkg' -File)
        if (!$packages.Count) { throw 'The bundled plugin directory contains no packages.' }
        # Install into a private store with the normal validation and layout, then expose the complete set at once.
        $seed = ChildPath $root ('.seed-' + [guid]::NewGuid().ToString('N'))
        try {
            foreach ($package in $packages) {
                & $PSCommandPath -Action Install -Source $package.FullName -Directory $seed
            }
            [IO.Directory]::Move((Join-Path $seed 'active'), $active)
        } finally {
            if ([IO.Directory]::Exists($seed)) { Remove-Item -LiteralPath $seed -Recurse -Force }
        }
        return
    }
    if ($Action -eq 'ApplyPending') {
        if ([IO.Directory]::Exists($removes)) {
            foreach ($marker in Get-ChildItem -LiteralPath $removes -File) {
                RemoveInstalled $marker.Name
                [IO.File]::Delete($marker.FullName)
            }
        }
        if ([IO.Directory]::Exists($adds)) {
            [void][IO.Directory]::CreateDirectory($active)
            foreach ($entry in Get-ChildItem -LiteralPath $adds -Directory) {
                [IO.Directory]::Move((ChildPath $adds $entry.Name),(ChildPath $active $entry.Name))
            }
        }
        return
    }
    if ($Action -eq 'Cancel') {
        if (!$Id) { throw 'Choose a pending change.' }
        if ([IO.Directory]::Exists((ChildPath $adds $Id))) { Detach $adds $Id }
        elseif ([IO.File]::Exists((ChildPath $removes $Id))) { [IO.File]::Delete((ChildPath $removes $Id)) }
        else { throw 'This change is no longer pending.' }
        return
    }
    if ($Action -eq 'Remove') {
        if (!$Id) { throw 'Remove requires the plugin ID.' }
        if (!(IsInstalled $Id)) { throw "$Id is not installed." }
        $marker = ChildPath $removes $Id
        if ([IO.File]::Exists($marker)) { throw 'Removal is already pending.' }
        if ($NextStart) {
            [void][IO.Directory]::CreateDirectory($removes)
            [IO.File]::WriteAllText($marker,'')
        } else { RemoveInstalled $Id }
        return
    }
    if ($Source -and $Feed) { throw 'Specify either -Source or -Feed.' }
    if (!$Source) {
        if (!$Feed -or !$Id -or !$Version) { throw 'Specify -Source, or a local -Feed with -Id and -Version.' }
        $feedRoot = (Get-Item -LiteralPath $Feed).FullName
        $name = "$Id.$Version.nupkg"
        $Source = Join-Path $feedRoot $name
        if (![IO.File]::Exists($Source)) {
            $Source = Join-Path $feedRoot ($Id.ToLowerInvariant()+'/'+$Version.ToLowerInvariant()+'/'+$name.ToLowerInvariant())
        }
    }
    $inputItem = Get-Item -LiteralPath $Source
    RejectLink $inputItem
    if ($inputItem.PSIsContainer) {
        $relative = [IO.Path]::GetRelativePath($inputItem.FullName,$root)
        if ($relative -eq '.' -or (! [IO.Path]::IsPathRooted($relative) -and ($relative -split '[/\\]')[0] -ne '..')) {
            throw 'The destination store must not be inside the source folder.'
        }
    } elseif ($inputItem.Extension -notin '.zip','.nupkg') { throw 'Use a plugin folder, .zip or .nupkg.' }

    $stage = ChildPath $root ('.install-' + [guid]::NewGuid().ToString('N'))
    [void][IO.Directory]::CreateDirectory($stage)
    try {
        if ($inputItem.PSIsContainer) { CopyPayload $inputItem.FullName $stage }
        else {
            $zip = [IO.Compression.ZipFile]::OpenRead($inputItem.FullName)
            try {
                if ($BoundedArchive -and ($zip.Entries.Count -gt 4096 -or ($zip.Entries | Measure-Object Length -Sum).Sum -gt 256MB)) {
                    throw 'Plugin archive exceeds the extraction limit (4,096 entries or 256 MiB).'
                }
                [IO.Compression.ZipFileExtensions]::ExtractToDirectory($zip,$stage)
            } finally { $zip.Dispose() }
        }
        $payload = $stage
        if (!$inputItem.PSIsContainer -and $inputItem.Extension -eq '.zip' -and
            ![IO.File]::Exists((Join-Path $stage 'fluxflow-plugin.json'))) {
            $children = @(Get-ChildItem -LiteralPath $stage -Force)
            if ($children.Count -eq 1 -and $children[0].PSIsContainer) {
                $payload = ChildPath $stage $children[0].Name
            }
        }
        $m = ReadManifest $payload
        if ($null -ne $m.version -and $m.version -isnot [string]) { throw 'Plugin manifest version must be text.' }
        if ($Id -and $Id -ine $m.id) { throw 'Source manifest does not match the requested plugin ID.' }
        $sourceVersion = $m.version
        if (!$inputItem.PSIsContainer -and $inputItem.Extension -eq '.nupkg') {
            $nu = PackageMetadata $payload
            if (!$nu -or !$nu.version -or $nu.id -ine $m.id -or ($sourceVersion -and $sourceVersion -ne $nu.version)) {
                throw 'Package identity does not match the manifest.'
            }
            $sourceVersion = $nu.version
        }
        if ($Version -and $Version -ne $sourceVersion) { throw 'Source version does not match the requested version.' }
        foreach ($tfm in 'net8.0','net10.0') {
            if (![IO.File]::Exists((Join-Path $payload "lib/$tfm/$($m.id).dll"))) { throw "Missing $tfm plugin entry." }
        }
        if ((IsInstalled $m.id) -or [IO.Directory]::Exists((ChildPath $adds $m.id))) {
            throw "$($m.id) is already installed or pending."
        }
        $origin = if ($SourceKind) { $SourceKind } elseif ($inputItem.PSIsContainer) { 'Folder' } elseif ($inputItem.Extension -eq '.zip') { 'ZIP' } else { 'Local NuGet' }
        [IO.File]::WriteAllText((Join-Path $payload '.install-source'), $origin)
        $destination = if ($NextStart) { $adds } else { $active }
        [void][IO.Directory]::CreateDirectory($destination)
        $target = ChildPath $destination $m.id
        [IO.Directory]::Move($payload,$target)
    }
    finally { if ([IO.Directory]::Exists($stage)) { Remove-Item -LiteralPath $stage -Force -Recurse } }
}
finally { if ($lock) { $lock.Dispose() }; $changes.Dispose() }
