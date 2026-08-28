#!/usr/bin/env bash
set -euo pipefail

root_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root_dir"

# DeltaText brings SixLabors.Fonts into the headless harness. Prefer an
# explicitly supplied license and otherwise use the standard Furnace checkout
# location when it exists. CI can provide the same variable from its secret.
workspace_license="$root_dir/../Furnace/Licenses/SixLabors.lic"
if [[ -z "${SixLaborsLicenseFile:-}" && -f "$workspace_license" ]]; then
    export SixLaborsLicenseFile="$workspace_license"
fi

# Roslyn resolves ErrorLog once per project. Normalize a relative caller value
# so every project writes to one repository-level report.
error_log="${CODE_METRICS_ERROR_LOG:-artifacts/code-metrics/diagnostics.sarif}"
case "$error_log" in
    /*) ;;
    *) error_log="$root_dir/$error_log" ;;
esac
mkdir -p "$(dirname "$error_log")"

exec dotnet build DeltaXAML.slnx \
    -c Release \
    --no-restore \
    --disable-build-servers \
    -m:1 \
    /p:UseSharedCompilation=false \
    /p:AnalysisMode=AllEnabledByDefault \
    "/p:ErrorLog=$error_log,version=2.1" \
    "$@"
