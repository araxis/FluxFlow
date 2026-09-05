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
    Invoke-Test "tests/Acceptance/Workflows/MqttToHttp/FluxFlow.Workflow.Acceptance.Tests/FluxFlow.Workflow.Acceptance.Tests.csproj"
}

function Invoke-IntegrationTests {
    Invoke-Test "tests/Integration/Containers/Workflows/MqttToHttp/FluxFlow.Workflow.ContainerIntegrationTests/FluxFlow.Workflow.ContainerIntegrationTests.csproj"
    if ($IncludeLicensedDatabaseTests) {
        & "$repositoryRoot/tests/Integration/Containers/Databases/TSql/DurableInput/FluxFlow.Engine.DurableInput.TSql.IntegrationTests/run-integration.ps1" -AcceptLicense
        if ($LASTEXITCODE -ne 0) { throw "Durable-input database integration tests failed." }
        & "$repositoryRoot/tests/Integration/Containers/Databases/TSql/DurableOutput/FluxFlow.Engine.DurableOutput.TSql.IntegrationTests/run-integration.ps1" -AcceptLicense
        if ($LASTEXITCODE -ne 0) { throw "Durable-output database integration tests failed." }
    }
}

function Invoke-SmokeTests {
    Invoke-Test "tests/Smoke/External/Workflows/MqttToHttp/FluxFlow.Workflow.ExternalSmokeTests/FluxFlow.Workflow.ExternalSmokeTests.csproj"
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
