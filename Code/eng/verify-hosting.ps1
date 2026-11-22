[CmdletBinding()]
param([switch] $WindowsService, [string] $WorkDirectory = '')
$ErrorActionPreference = 'Stop'
if ($WindowsService) {
    if ($env:OS -ne 'Windows_NT') { throw 'SCM verification requires Windows.' }
    $principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'SCM verification requires an elevated PowerShell session. Run without -WindowsService for normal-host and adapter-fallback verification.'
    }
}
if (-not $WorkDirectory) { $WorkDirectory = Join-Path ([IO.Path]::GetTempPath()) ('fluxflow-hosting-' + [Guid]::NewGuid().ToString('N')) }
$WorkDirectory = [IO.Path]::GetFullPath($WorkDirectory)
if (Test-Path -LiteralPath $WorkDirectory) { throw 'Use a fresh evidence directory.' }
New-Item -ItemType Directory -Path $WorkDirectory | Out-Null
$project = Join-Path $PSScriptRoot 'hosting-consumer/HostingConsumer.csproj'

function Wait-Evidence([string] $Path, [string] $FailurePath) {
    $deadline = [DateTime]::UtcNow.AddSeconds(45)
    while (-not (Test-Path -LiteralPath $Path)) {
        if (Test-Path -LiteralPath $FailurePath) { throw (Get-Content -LiteralPath $FailurePath -Raw) }
        if ([DateTime]::UtcNow -gt $deadline) { throw "Timed out waiting for $Path" }
        Start-Sleep -Milliseconds 100
    }
}

foreach ($framework in 'net8.0', 'net10.0') {
    $publish = Join-Path $WorkDirectory $framework
    & dotnet publish $project -c Release -f $framework --no-self-contained -o $publish
    if ($LASTEXITCODE) { throw "Publish failed: $framework" }
    $dll = Join-Path $publish 'FluxFlow.HostSmoke.dll'
    foreach ($mode in 'normal', 'windows-adapter') {
        $evidence = Join-Path $WorkDirectory "$framework-$mode.json"
        & dotnet $dll $mode $evidence
        if ($LASTEXITCODE) { throw "Host verification failed: $framework/$mode" }
        $result = Get-Content -LiteralPath $evidence -Raw | ConvertFrom-Json
        if (-not $result.Passed -or $result.ActualWindowsService -or $result.Lifetime -ne 'ConsoleLifetime') { throw 'Unexpected console-host evidence.' }
    }
    if ($WindowsService) {
        $serviceName = 'FluxFlow-HostSmoke-' + [Guid]::NewGuid().ToString('N')
        $executable = Join-Path $publish 'FluxFlow.HostSmoke.exe'
        $evidence = Join-Path $WorkDirectory "$framework-windows-service.json"
        if (Get-Service -Name $serviceName -ErrorAction SilentlyContinue) { throw 'Temporary service name already exists.' }
        $created = $false
        try {
            New-Service -Name $serviceName -BinaryPathName "`"$executable`" windows-service `"$evidence`" $serviceName" -StartupType Manual | Out-Null
            $created = $true
            Start-Service -Name $serviceName
            Wait-Evidence ($evidence + '.ready') ($evidence + '.failure.txt')
            Stop-Service -Name $serviceName
            Wait-Evidence $evidence ($evidence + '.failure.txt')
            $result = Get-Content -LiteralPath $evidence -Raw | ConvertFrom-Json
            if (-not $result.Passed -or -not $result.ActualWindowsService -or $result.Lifetime -ne 'WindowsServiceLifetime') { throw 'SCM execution was not verified.' }
        }
        finally {
            if ($created) {
                try {
                    $service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
                    if ($service -and $service.Status -ne 'Stopped') { Stop-Service -Name $serviceName }
                }
                finally {
                    & sc.exe delete $serviceName
                    if ($LASTEXITCODE) { throw "Failed to delete temporary service $serviceName" }
                }
            }
        }
    }
}
Write-Output "HOSTING_VERIFICATION_COMPLETE=True; ActualSCM=$([bool]$WindowsService); Evidence=$WorkDirectory"
