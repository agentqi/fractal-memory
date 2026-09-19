# Contributing

Thanks for improving FractalMem.

## Development

Requirements:

- .NET SDK 10.0.100 or later

Build:

```bash
dotnet build FractalMemory.sln
```

Test:

```bash
dotnet test --solution FractalMemory.sln
```

Verify distributable packages:

```bash
dotnet pack FractalMemory.sln --configuration Release --output artifacts
version=$(sed -n 's/.*<VersionPrefix>\([^<]*\)<.*/\1/p' Directory.Build.props)
bash scripts/smoke-packages.sh artifacts "$version"
```

## Guidelines

- Keep core behavior local-first and filesystem-backed.
- Keep CLI and MCP wrappers thin over `FractalMemory.Core`.
- Add focused tests for changes to search, validation, indexing, export, or repository layout.
- Treat `.fractal-memory/` as a strict filesystem boundary. Add traversal and symbolic-link tests for every new path-bearing feature.
- Do not commit generated outputs, local memory stores, package caches, or benchmark run artifacts.

## Release Checklist

- Update `CHANGELOG.md` and `VersionPrefix`.
- Ensure CI, CodeQL, dependency audit, package smoke tests, and plugin validation pass.
- Push a matching `v<VersionPrefix>` tag only after the `NUGET_API_KEY` release secret and release environment are configured.
- Verify the release workflow publishes all three NuGet packages and the GitHub release.

See `docs/releasing.md` for the complete release and branch-protection setup.
