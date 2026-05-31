# Current State

Date: 2026-05-31

## Repositories

- `D:\Projects\FluxFlow` contains a standalone solution with one library and one test project.
- `D:\Projects\FluxFlow` is now a git repository on `main`.
- Private remote: `https://github.com/araxis/FluxFlow`.
- `D:\Projects\FluxMq` contains the source application and has uncommitted local changes, so it is reference-only for this extraction pass.

## FluxFlow solution

- Solution: `FluxFlow.sln`
- Library: `src\FluxFlow.Engine\FluxFlow.Engine.csproj`
- Tests: `tests\FluxFlow.Engine.Tests\FluxFlow.Engine.Tests.csproj`
- Target frameworks after setup: `net8.0` and `net10.0`
- Package id: `FluxFlow.Engine`

## Verification

- `dotnet test FluxFlow.sln` passed before changes with 4 tests.
- `dotnet test FluxFlow.sln --configuration Release` passed after changes with 4 tests.
- `dotnet pack src\FluxFlow.Engine\FluxFlow.Engine.csproj --configuration Release --output artifacts\packages -p:PackageVersion=0.1.0-alpha.1` created `.nupkg` and `.snupkg` packages.
