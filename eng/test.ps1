[CmdletBinding()]
param(
    [Parameter()]
    [ValidateSet("Unit", "Acceptance", "Integration", "Smoke", "All")]
    [string] $Suite = "Unit",
    [Parameter()]
    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Debug",
    [Parameter()]
    [switch] $NoBuild,
    [Parameter()]
    [switch] $IncludeLicensedDatabaseTests
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path $PSScriptRoot

function Invoke-Test([string] $target, [string[]] $additionalArguments = @()) {
    $arguments = @("test", $target, "--configuration", $Configuration)
    if ($NoBuild) { $arguments += "--no-build" }
    $arguments += $additionalArguments
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw "Test suite failed: $target" }
}

function Invoke-UnitTests {
    Invoke-Test "FluxFlow.sln" @(
        "--filter",
        "Category!=Acceptance&Category!=ContainerIntegration&Category!=ExternalSmoke&FullyQualifiedName!~TSql.IntegrationTests"
    )
}

function Invoke-AcceptanceTests {
    Write-Host "No component acceptance suite is present on the core-only branch; package acceptance remains available."
}

function Invoke-IntegrationTests {
    Write-Host "Component container workflows are removed; database integration remains opt-in."
    if ($IncludeLicensedDatabaseTests) {
        & "$repositoryRoot/tests/Integration/Containers/Databases/TSql/DurableInput/FluxFlow.Engine.DurableInput.TSql.IntegrationTests/run-integration.ps1" -AcceptLicense
        if ($LASTEXITCODE -ne 0) { throw "Durable-input database integration tests failed." }
        & "$repositoryRoot/tests/Integration/Containers/Databases/TSql/DurableOutput/FluxFlow.Engine.DurableOutput.TSql.IntegrationTests/run-integration.ps1" -AcceptLicense
        if ($LASTEXITCODE -ne 0) { throw "Durable-output database integration tests failed." }
    }
}

function Invoke-SmokeTests {
    Write-Host "No component external smoke suite is present on the core-only branch."
}

Push-Location $repositoryRoot
try {
    switch ($Suite) {
        "Unit" { Invoke-UnitTests }
        "Acceptance" { Invoke-AcceptanceTests }
        "Integration" { Invoke-IntegrationTests }
        "Smoke" { Invoke-SmokeTests }
        "All" {
            Invoke-UnitTests
            Invoke-AcceptanceTests
            Invoke-IntegrationTests
        }
    }
}
finally {
    Pop-Location
}
