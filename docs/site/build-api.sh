#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/../.."
(
  cd docs/site
  dotnet tool restore
  dotnet tool run docfx dotnet/docfx.json
)
cargo doc --manifest-path rust/Cargo.toml --locked --no-deps --target-dir build/docs-rust
