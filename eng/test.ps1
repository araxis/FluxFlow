[CmdletBinding()]
param(
    [Parameter()]
    [ValidateSet("Unit", "Acceptance", "Integration", "Smoke", "All")]
    [string] $Suite = "Unit",
    [Parameter()]
    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Debug",
    [Parameter()]
    [switch] $NoBuild
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
        "Category!=Acceptance&Category!=ContainerIntegration&Category!=ExternalSmoke"
    )
}

function Invoke-AcceptanceTests {
    Write-Host "No component acceptance suite is present on the core-only branch; use eng/verify-core-consumer.ps1 for package acceptance."
}

function Invoke-IntegrationTests {
    Write-Host "No integration modules are present in the minimum framework."
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
