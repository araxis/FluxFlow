[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [switch] $AcceptLicense,

    [string] $Image = "mcr.microsoft.com/mssql/server:2022-latest",

    [switch] $UseExternalConnectionString,

    [switch] $KeepContainer,

    [string] $TestFilter,

    [ValidateRange(0, 300)]
    [int] $BlameHangTimeoutSeconds = 0,

    [ValidateRange(10, 300)]
    [int] $ReadinessTimeoutSeconds = 90
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if (-not $AcceptLicense)
{
    throw "Run with -AcceptLicense to confirm acceptance of the SQL Server container image license."
}

$connectionVariable = "FLUXFLOW_TSQL_INTEGRATION_CONNECTION_STRING"
$originalConnectionString = [Environment]::GetEnvironmentVariable($connectionVariable, "Process")
$containerName = $null
$resultsDirectory = Join-Path `
    ([IO.Path]::GetTempPath()) `
    ("FluxFlowTSqlInputIntegration_" + [Guid]::NewGuid().ToString("N"))
[IO.Directory]::CreateDirectory($resultsDirectory) | Out-Null

try
{
    if ($UseExternalConnectionString)
    {
        if ([string]::IsNullOrWhiteSpace($originalConnectionString))
        {
            throw "$connectionVariable must be set when -UseExternalConnectionString is supplied."
        }

        $connectionString = $originalConnectionString
        Write-Host "Using the externally managed integration server."
    }
    else
    {
        & docker version --format "{{.Server.Version}}" | Out-Null
        if ($LASTEXITCODE -ne 0)
        {
            throw "Docker is required unless -UseExternalConnectionString is supplied."
        }

        $runId = [Guid]::NewGuid().ToString("N")
        $containerName = "fluxflow-tsql-input-tests-$runId"
        $password = "Ff!${runId}9aA"
        $containerId = & docker run `
            --detach `
            --name $containerName `
            --env "ACCEPT_EULA=Y" `
            --env "MSSQL_SA_PASSWORD=$password" `
            --publish "127.0.0.1::1433" `
            $Image
        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($containerId))
        {
            throw "The disposable SQL Server container could not be started."
        }

        $portBinding = (& docker port $containerName "1433/tcp").Trim()
        if ($LASTEXITCODE -ne 0)
        {
            throw "The disposable SQL Server container port could not be resolved."
        }

        $portMatch = [regex]::Match($portBinding, ":(?<port>[0-9]+)$")
        if (-not $portMatch.Success)
        {
            throw "The disposable SQL Server container port could not be resolved."
        }

        $port = $portMatch.Groups["port"].Value
        $deadline = [DateTimeOffset]::UtcNow.AddSeconds($ReadinessTimeoutSeconds)
        $ready = $false
        while ([DateTimeOffset]::UtcNow -lt $deadline)
        {
            $savedErrorActionPreference = $ErrorActionPreference
            try
            {
                $ErrorActionPreference = "Continue"
                & docker exec `
                    --env "SQLCMDPASSWORD=$password" `
                    $containerName `
                    /opt/mssql-tools18/bin/sqlcmd `
                    -S localhost `
                    -U sa `
                    -C `
                    -l 2 `
                    -Q "SELECT 1" 2>$null | Out-Null
                $readinessExitCode = $LASTEXITCODE
            }
            finally
            {
                $ErrorActionPreference = $savedErrorActionPreference
            }

            if ($readinessExitCode -eq 0)
            {
                $ready = $true
                break
            }

            Start-Sleep -Milliseconds 500
        }

        if (-not $ready)
        {
            throw "The disposable SQL Server container did not become ready within $ReadinessTimeoutSeconds seconds."
        }

        $digest = (& docker image inspect $Image --format "{{index .RepoDigests 0}}").Trim()
        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($digest))
        {
            throw "The tested container image digest could not be captured."
        }

        Write-Host "Tested image tag: $Image"
        Write-Host "Tested image digest: $digest"
        $connectionString = "Server=127.0.0.1,$port;Initial Catalog=master;User ID=sa;Password=$password;Encrypt=True;TrustServerCertificate=True;Connect Timeout=5"
    }

    [Environment]::SetEnvironmentVariable($connectionVariable, $connectionString, "Process")
    $project = Join-Path $PSScriptRoot "FluxFlow.Engine.DurableInput.TSql.IntegrationTests.csproj"
    $testArguments = @(
        "test",
        $project,
        "--configuration", "Release",
        "--nologo",
        "--logger", "trx;LogFileName=integration.trx",
        "--results-directory", $resultsDirectory)
    if (-not [string]::IsNullOrWhiteSpace($TestFilter))
    {
        $testArguments += @("--filter", $TestFilter)
    }
    if ($BlameHangTimeoutSeconds -gt 0)
    {
        $testArguments += @(
            "--blame-hang",
            "--blame-hang-timeout", "$($BlameHangTimeoutSeconds)s")
    }
    & dotnet @testArguments
    $testExitCode = $LASTEXITCODE

    $resultFile = Join-Path $resultsDirectory "integration.trx"
    if (-not [IO.File]::Exists($resultFile))
    {
        throw "The integration run did not produce its expected test result file."
    }

    [xml] $resultDocument = [IO.File]::ReadAllText($resultFile)
    $results = @($resultDocument.SelectNodes("//*[local-name()='UnitTestResult']"))
    if ($results.Count -eq 0)
    {
        throw "The integration project executed zero tests."
    }

    $passed = @($results | Where-Object { $_.outcome -eq "Passed" }).Count
    $failed = @($results | Where-Object { $_.outcome -eq "Failed" }).Count
    $skipped = @($results | Where-Object { $_.outcome -notin @("Passed", "Failed") }).Count
    Write-Host "Integration result: total=$($results.Count), passed=$passed, failed=$failed, skipped=$skipped."

    if ($skipped -ne 0)
    {
        throw "The integration suite must run with zero skipped tests."
    }
    if ($testExitCode -ne 0 -or $failed -ne 0)
    {
        throw "The integration suite failed."
    }
}
finally
{
    [Environment]::SetEnvironmentVariable(
        $connectionVariable,
        $originalConnectionString,
        "Process")

    if ($null -ne $containerName)
    {
        if ($KeepContainer)
        {
            Write-Host "Retained diagnostic container: $containerName"
        }
        else
        {
            & docker rm --force $containerName 2>$null | Out-Null
        }
    }

    if ([IO.Directory]::Exists($resultsDirectory))
    {
        [IO.Directory]::Delete($resultsDirectory, $true)
    }
}
