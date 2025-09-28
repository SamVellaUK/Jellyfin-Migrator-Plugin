# Repository Guidelines

## Project Structure & Module Organization
- Source: `Jellyfin.Plugin.Template/` (C# .NET 8). Entry point: `Plugin.cs`.
- Configuration UI: `Jellyfin.Plugin.Template/Configuration/configPage.html`.
- Plugin settings model: `Configuration/PluginConfiguration.cs`.
- Solution/props: `Jellyfin.Plugin.Template.sln`, `Directory.Build.props`.
- Quality/config: `.editorconfig`, `jellyfin.ruleset`.
- CI: `.github/workflows/` (build, test, publish), manifest: `build.yaml`.
- Legacy references: `Old POC Code/` (Python POC scripts; not part of the build).

## Build, Test, and Development Commands
- Restore: `dotnet restore Jellyfin.Plugin.Template.sln`.
- Build (Release): `dotnet build Jellyfin.Plugin.Template.sln -c Release`.
- Publish (copies artifacts to bin): `dotnet publish Jellyfin.Plugin.Template/Jellyfin.Plugin.Template.csproj -c Release`.
- Local install (manual): copy `bin/Release/net8.0/Jellyfin.Plugin.Template.dll` to your Jellyfin data `plugins/<PluginName>/` directory, then restart Jellyfin.

## Coding Style & Naming Conventions
- Indentation: spaces, 4 for code, 2 for YAML/XML (enforced via `.editorconfig`).
- C#: `nullable enable`, warnings-as-errors; StyleCop and analyzers are enabled.
- Naming: PascalCase for types/members; camelCase with `_` prefix for fields; constants PascalCase.
- Sort `using` directives with System first; prefer `var` where type is apparent.

## Testing Guidelines
- No test project yet. If adding tests:
  - Create `Jellyfin.Plugin.Template.Tests` (xUnit recommended): `dotnet new xunit -n Jellyfin.Plugin.Template.Tests` and add to the solution.
  - Run tests: `dotnet test`.
  - Name tests after behavior: `MethodName_ShouldDoThing_WhenCondition`.

## Commit & Pull Request Guidelines
- Commits: imperative mood, concise subject; reference issues/PRs when relevant (e.g., "Fix build for .NET 8", "Update Jellyfin.Controller to 10.9.11").
- PRs: clear description, rationale, and scope; link issues; include screenshots for UI/config changes; update `README.md` and `build.yaml` when plugin metadata changes.
- CI must pass (build/test/CodeQL). Avoid unrelated changes.

## Security & Configuration Tips
- Do not commit secrets or environment-specific paths.
- Target framework/ABI: `net8.0` and `targetAbi` in `build.yaml`. Keep versions consistent with `Directory.Build.props` and the `.csproj`.
- Generate a new plugin GUID and update both code and manifest if forking as a new plugin.

