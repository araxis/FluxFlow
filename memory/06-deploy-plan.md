# Deploy Plan

Date: 2026-05-31

## Private repository

1. Initialize git in `D:\Projects\FluxFlow`. Done.
2. Create `araxis/FluxFlow` as a private GitHub repository. Done.
3. Add `origin` to the local repository. Done.
4. Commit the package boundary and workflow files. Done.
5. Push `main`. Done.

The helper script is `eng\github-bootstrap.ps1`.

## GitHub workflows

- `.github\workflows\ci.yml` runs restore, build, and tests on pull requests and pushes to `main`.
- `.github\workflows\publish-nuget.yml` is the release workflow.
  It runs on `v*.*.*` tags or manual dispatch with a version.
  It restores, builds, tests, packs, uploads workflow artifacts, creates or
  updates the matching GitHub Release with `.nupkg` and `.snupkg` assets, then
  publishes NuGet packages.

## NuGet secret

The repository has `NUGET_API_KEY` stored for the publish workflow.

Current status: `FluxFlow.Engine` versions `0.1.0-alpha.1`,
`0.2.0-alpha.1`, `0.3.0-alpha.1`, and `0.4.0-alpha.1` were
published successfully.

To rotate it later, create a new NuGet key and store it as a repository secret:

```powershell
$env:NUGET_API_KEY = "<key>"
.\eng\github-bootstrap.ps1 -Repository araxis/FluxFlow
```

## Release flow

1. Update `CHANGELOG.md`.
2. Run `dotnet build FluxFlow.sln --configuration Release --no-restore`.
3. Run `dotnet test FluxFlow.sln --configuration Release --no-build`.
4. Run `dotnet pack src\FluxFlow.Engine\FluxFlow.Engine.csproj --configuration Release --no-build --output artifacts\packages`.
5. Commit all changes.
6. Tag the release, for example `v0.1.0-alpha.1`.
7. Push `main` and the tag.
8. Confirm the GitHub Release has package assets.
9. Confirm the release workflow completes and the NuGet package is available.
