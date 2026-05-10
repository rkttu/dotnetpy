# Repository Guidelines

DotNetPy is a global open-source project. To keep history, releases, and PR
discussions accessible to every contributor, all artifacts that live in or get
published from this repository are written in **English**.

## Language

- **Commit messages**: English only. Use the imperative mood for the subject
  line ("Fix …", "Add …", "Refactor …"), keep it under ~72 characters, and put
  any longer explanation in the body separated by a blank line.
- **Pull request titles and descriptions**: English.
- **Release notes / GitHub Releases**: English.
- **Tag annotations** (`git tag -a -m`): English.
- **Code comments, identifiers, and XML doc comments**: English.

User-facing strings and localized resources are exempt — translations belong in
the resource files designed for them (e.g. `*.resx`).

## Versioning & Releases

- The shared package version is defined once in
  [`src/Directory.Build.props`](src/Directory.Build.props). Bump it there.
- Tags follow `vX.Y.Z` (e.g. `v0.5.1`). The release workflow extracts the
  version from `github.event.release.tag_name` and strips the leading `v`.
- Publishing to NuGet.org and attaching `.nupkg` / `.snupkg` to the GitHub
  Release is automated by `.github/workflows/release.yml`, triggered by
  `release: published`. Creating the GitHub Release is therefore the action
  that ships the package — make sure the tag/commit are correct first.

## Build & Test

- Build: `dotnet build --configuration Release`
- Analyzer tests: `dotnet test src/DotNetPy.Analyzers.Tests/DotNetPy.Analyzers.Tests.csproj`
- Unit tests: `dotnet test src/DotNetPy.UnitTest/DotNetPy.UnitTest.csproj`
