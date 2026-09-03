#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

# DeltaMaths is the provider-owned sibling library and is intentionally not
# scanned here. Every C# path in this repository is checked; only generated
# output and build artifacts are excluded because they are not source inputs.
scope=(src tests samples probes playground benchmarks tools)
matches="$(rg -n \
    --glob '*.cs' \
    --glob '!**/bin/**' \
    --glob '!**/obj/**' \
    --glob '!**/generated/**' \
    --glob '!**/Generated/**' \
    '(System\.)?MathF?\.[A-Za-z_]' \
    "${scope[@]}" || true)"

if [[ -n "$matches" ]]; then
    printf '%s\n' 'DeltaMaths boundary check failed: direct System.Math/MathF usage remains:' >&2
    printf '%s\n' "$matches" >&2
    exit 1
fi

printf '%s\n' 'DeltaMaths boundary: PASS (no direct System.Math/MathF in scoped source).'
