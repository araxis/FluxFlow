# Deploy Plan

Date: 2026-05-31

## Private repository

1. Initialize git in `D:\Projects\FluxFlow`.
2. Create `araxis/FluxFlow` as a private GitHub repository.
3. Add `origin` to the local repository.
4. Commit the package boundary and workflow files.
5. Push `main`.

The helper script is `eng\github-bootstrap.ps1`.

## GitHub workflows

- `.github\workflows\ci.yml` runs restore, build, and tests on pull requests and pushes to `main`.
- `.github\workflows\publish-nuget.yml` publishes packages when a `v*.*.*` tag is pushed or when manually run with a version.

## NuGet secret

Create a NuGet API key and store it as a repository secret:

```powershell
$env:NUGET_API_KEY = "<key>"
.\eng\github-bootstrap.ps1 -Repository araxis/FluxFlow
```

## Release flow

1. Update release notes.
2. Run `dotnet test FluxFlow.sln`.
3. Run `dotnet pack src\FluxFlow.Engine\FluxFlow.Engine.csproj --configuration Release --output artifacts\packages`.
4. Commit all changes.
5. Tag the release, for example `v0.1.0-alpha.1`.
6. Push `main` and the tag.
7. Confirm the publish workflow completes.
