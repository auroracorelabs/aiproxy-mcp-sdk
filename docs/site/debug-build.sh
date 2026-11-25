#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/../.."
npm ci --prefix docs/site
bash docs/site/build-api.sh
npm run build --prefix docs/site
