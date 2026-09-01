#!/usr/bin/env bash
set -euo pipefail

script_dir=$(cd "$(dirname "$0")" && pwd)
delta_xaml_root=$(cd "$script_dir/.." && pwd)
workspace_root=$(cd "$delta_xaml_root/.." && pwd)
project="$delta_xaml_root/samples/2048-electron/DeltaXAML.Samples.Snake.csproj"
frames=1000
skip=100
slots=16
raw_report=/tmp/delta-snake-profile.raw.tsv
summary_report=/tmp/delta-snake-profile.summary.json

usage() {
    cat <<'EOF'
Usage: ./eng/profile-snake-headless.sh [options]

Options:
  --frames N       Total frames to execute (default: 1000)
  --skip N         Warm-up frames excluded from raw report (default: 100)
  --slots N        Frames in flight (default: 16)
  --report PATH    Raw per-frame TSV path
  --summary PATH   Filtered JSON summary path
EOF
}

while (($# > 0)); do
    case "$1" in
        --frames)
            frames=$2
            shift 2
            ;;
        --skip)
            skip=$2
            shift 2
            ;;
        --slots)
            slots=$2
            shift 2
            ;;
        --report)
            raw_report=$2
            shift 2
            ;;
        --summary)
            summary_report=$2
            shift 2
            ;;
        --help|-h)
            usage
            exit 0
            ;;
        *)
            printf 'Unknown option: %s\n' "$1" >&2
            usage >&2
            exit 2
            ;;
    esac
done

if [[ ! "$frames" =~ ^[1-9][0-9]*$ || ! "$skip" =~ ^[0-9]+$ || ! "$slots" =~ ^[1-9][0-9]*$ ]]; then
    printf '%s\n' '--frames/--skip/--slots must be non-negative integers; frames and slots must be positive.' >&2
    exit 2
fi

absolute_path() {
    local value=$1
    local directory
    directory=$(dirname "$value")
    mkdir -p "$directory"
    printf '%s/%s' "$(cd "$directory" && pwd)" "$(basename "$value")"
}

raw_report=$(absolute_path "$raw_report")
summary_report=$(absolute_path "$summary_report")
run_log="${raw_report}.run.log"
license_file="${SixLaborsLicenseFile:-$workspace_root/Furnace/Licenses/SixLabors.lic}"

cd "$delta_xaml_root"
if ! SixLaborsLicenseFile="$license_file" dotnet run \
    --project "$project" \
    -c Release \
    -r osx-arm64 \
    --no-restore \
    -- \
    --headless \
    --frames "$frames" \
    --skip "$skip" \
    --slots "$slots" \
    --profile-report "$raw_report" >"$run_log" 2>&1; then
    cat "$run_log"
    exit 1
fi

cat "$run_log"
python3 "$delta_xaml_root/tools/analyze-snake-profile.py" "$raw_report" --output "$summary_report"
printf 'raw-report=%s\nsummary=%s\n' "$raw_report" "$summary_report"
