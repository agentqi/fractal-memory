# Releasing FractalMem

## One-time repository setup

1. Create a GitHub environment named `release` and require maintainer approval if desired.
2. Add a `NUGET_API_KEY` environment secret with push access to the `FractalMemory.Core`, `FractalMemory.Cli`, and `FractalMemory.McpServer` package IDs.
3. Merge the CI and CodeQL workflows to the default branch before enabling required checks.
4. Protect `main` and require pull requests, conversation resolution, a linear history, and these checks:
   - `Build and test (ubuntu-latest)`
   - `Build and test (windows-latest)`
   - `Build and test (macos-latest)`
   - `Coverage`
   - `Dependency audit`
   - `Package and MCP smoke test`
   - `Analyze (csharp)`

Do not enable a required check until it has completed once on `main`; GitHub cannot require a check it has not observed.

## Release procedure

1. Move the `Unreleased` changelog entries under a heading matching the release version.
2. Set `VersionPrefix` in `Directory.Build.props` and keep both plugin manifests and marketplace versions aligned.
3. Open and merge a release PR after every required check passes.
4. Create and push the matching annotated tag, for example `v0.1.0`.
5. Watch the `Release` workflow. It verifies the tag, rebuilds, retests, packs, installs both tools, exercises the MCP protocol, creates a draft GitHub release, publishes NuGet packages, and only then publishes the GitHub release.
6. Verify the package pages, install both tools from NuGet in a clean environment, and confirm the GitHub release assets.

If NuGet publication fails, the GitHub release remains a draft so the failure can be corrected without advertising a partial release.
