# Building and releasing the SDK

.NET and Rust share the product version in root `VERSION`. The .NET packages read it via `Directory.Build.props`. Keep `version` in `rust/Cargo.toml` equal to `VERSION` — the release workflow fails the tag if they differ.

## Where users get the packages

- **NuGet**: `AiProxy.Mcp.Sdk` and `AiProxy.Mcp.Sdk.AspNetCore` on every release tag.
- **crates.io**: `aiproxy-mcp-sdk` on every release tag.
- **GitHub Actions artifacts** from CI are build output for maintainers (pull requests and `main`), not the primary user channel.

## Local build and test

From the repository root:

```sh
dotnet test aiproxy-mcp-sdk.slnx
dotnet test dotnet/AiProxy.Mcp.Sdk.Tests -p:SdkTargetFramework=netstandard2.0
cargo test --workspace --manifest-path rust/Cargo.toml
dotnet pack aiproxy-mcp-sdk.slnx -c Release -o nupkgs
cargo package --manifest-path rust/Cargo.toml --locked
```

## Releasing

1. Update `CHANGELOG.md`. If `git tag --list "v$(cat VERSION)"` already prints a tag, bump `VERSION` (and `rust/Cargo.toml` `version`) first and open a new changelog section.
2. Commit with matching `VERSION` and `rust/Cargo.toml` version `X.Y.Z`.
3. Push the tag `vX.Y.Z`. The **release** workflow publishes both NuGet packages and the crate.
4. GitHub environment `sdk-release`: variable `NUGET_USER` (nuget.org trusted publishing for this repository) and secret `CARGO_REGISTRY_TOKEN` (crates.io publish for `aiproxy-mcp-sdk`).
5. To retry a single registry, run the release workflow manually from the tag (*Run workflow*).

CI (`.github/workflows/ci.yml`) already runs tests, `dotnet pack`, a .NET Framework 4.7.2 consumer check, clippy and `cargo publish --dry-run` on push and pull requests.

The SDK is MIT licensed; NuGet and the crate ship license metadata. CI diffs root `LICENSE` against `rust/LICENSE`.
