#!/usr/bin/env python3
"""Analyze a raw Snake headless profile TSV and report robust medians."""

from __future__ import annotations

import argparse
import csv
import json
import math
from pathlib import Path
from statistics import median, pstdev


TIME_FIELDS = (
    "build_ns",
    "acquire_ns",
    "record_ns",
    "submit_present_ns",
    "fence_wait_ns",
    "layout_shaping_ns",
    "pass_cpu_ns",
    "pass_gpu_ns",
)
COUNTER_FIELDS = (
    "pass_count",
    "resource_count",
    "draw_count",
    "descriptor_bind_count",
    "upload_bytes",
)
REQUIRED_FIELDS = ("frame", *TIME_FIELDS, *COUNTER_FIELDS, "gpu_timestamps")


def percentile(values: list[float], fraction: float) -> float:
    ordered = sorted(values)
    position = (len(ordered) - 1) * fraction
    lower = math.floor(position)
    upper = math.ceil(position)
    if lower == upper:
        return ordered[lower]
    weight = position - lower
    return ordered[lower] + (ordered[upper] - ordered[lower]) * weight


def filter_spikes(values: list[float], mad_multiplier: float) -> tuple[list[float], str, float, float]:
    center = median(values)
    deviations = [abs(value - center) for value in values]
    mad = median(deviations)
    if mad > 0:
        radius = mad_multiplier * 1.4826 * mad
        lower = center - radius
        upper = center + radius
        method = f"MAD x {mad_multiplier:g}"
    else:
        first_quartile = percentile(values, 0.25)
        third_quartile = percentile(values, 0.75)
        interquartile_range = third_quartile - first_quartile
        if interquartile_range > 0:
            lower = first_quartile - 1.5 * interquartile_range
            upper = third_quartile + 1.5 * interquartile_range
            method = "IQR x 1.5"
        else:
            lower = center
            upper = center
            method = "exact median"

    kept = [value for value in values if lower <= value <= upper]
    if not kept:
        kept = [center]
    return kept, method, lower, upper


def summarize(values: list[float], mad_multiplier: float) -> dict[str, float | int | str]:
    kept, method, lower, upper = filter_spikes(values, mad_multiplier)
    filtered_median = median(kept)
    deviations = [abs(value - filtered_median) for value in kept]
    mad = median(deviations)
    standard_deviation = pstdev(kept) if len(kept) > 1 else 0.0
    median_error = 1.2533141373 * 1.4826 * mad / math.sqrt(len(kept))
    raw_median = median(values)
    return {
        "raw_count": len(values),
        "retained_count": len(kept),
        "removed_count": len(values) - len(kept),
        "raw_median": raw_median,
        "median": filtered_median,
        "median_error": median_error,
        "median_error_percent": abs(median_error / filtered_median) * 100 if filtered_median else 0.0,
        "median_shift": filtered_median - raw_median,
        "median_shift_percent": abs((filtered_median - raw_median) / raw_median) * 100 if raw_median else 0.0,
        "mad": mad,
        "mad_percent": abs(mad / filtered_median) * 100 if filtered_median else 0.0,
        "standard_deviation": standard_deviation,
        "filter": method,
        "lower_bound": lower,
        "upper_bound": upper,
    }


def read_rows(path: Path) -> list[dict[str, float | int | bool]]:
    with path.open("r", encoding="utf-8", newline="") as stream:
        reader = csv.DictReader(stream, delimiter="\t")
        fields = reader.fieldnames or []
        missing = [field for field in REQUIRED_FIELDS if field not in fields]
        if missing:
            raise ValueError(f"raw profile is missing fields: {', '.join(missing)}")

        rows: list[dict[str, float | int | bool]] = []
        for line_number, row in enumerate(reader, start=2):
            try:
                parsed: dict[str, float | int | bool] = {
                    "frame": int(row["frame"]),
                    "gpu_timestamps": row["gpu_timestamps"].lower() == "true",
                }
                for field in (*TIME_FIELDS, *COUNTER_FIELDS):
                    parsed[field] = float(row[field])
                rows.append(parsed)
            except (TypeError, ValueError) as error:
                raise ValueError(f"invalid raw profile row {line_number}: {error}") from error
    if not rows:
        raise ValueError("raw profile contains no completed frames")
    return rows


def format_nanoseconds(value: float) -> str:
    if value >= 1_000_000_000:
        return f"{value / 1_000_000_000:.3g}s"
    if value >= 1_000_000:
        return f"{value / 1_000_000:.3g}ms"
    if value >= 1_000:
        return f"{value / 1_000:.3g}us"
    return f"{value:.3g}ns"


def print_summary(summary: dict) -> None:
    print(f"Snake profile: frames={summary['sample_count']}, spike-filter={summary['spike_filter']}")
    print("metric                 median       raw-median   median-error  MAD          kept/removed")
    for field in TIME_FIELDS:
        item = summary["timings"][field]
        print(
            f"{field:<22} {format_nanoseconds(item['median']):>10} "
            f"{format_nanoseconds(item['raw_median']):>12} "
            f"{format_nanoseconds(item['median_error']):>12} "
            f"{format_nanoseconds(item['mad']):>10} "
            f"{item['retained_count']}/{item['removed_count']}"
        )
    for field in COUNTER_FIELDS:
        item = summary["counters"][field]
        print(
            f"{field:<22} {item['median']:>10.3f} "
            f"{item['raw_median']:>12.3f} {item['median_error']:>12.3f} "
            f"{item['mad']:>10.3f} {item['retained_count']}/{item['removed_count']}"
        )
    print(f"gpu-timestamps={summary['gpu_timestamps_all_true']}")
    print("median-error=robust standard error estimate; deviation=MAD")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("report", type=Path)
    parser.add_argument("--output", type=Path)
    parser.add_argument("--mad-multiplier", type=float, default=6.0)
    args = parser.parse_args()
    if args.mad_multiplier <= 0:
        parser.error("--mad-multiplier must be positive")

    rows = read_rows(args.report)
    summary = {
        "schema": 1,
        "source": str(args.report),
        "sample_count": len(rows),
        "frame_first": rows[0]["frame"],
        "frame_last": rows[-1]["frame"],
        "spike_filter": f"MAD x {args.mad_multiplier:g}, IQR x 1.5 fallback",
        "timings": {
            field: summarize([float(row[field]) for row in rows], args.mad_multiplier)
            for field in TIME_FIELDS
        },
        "counters": {
            field: summarize([float(row[field]) for row in rows], args.mad_multiplier)
            for field in COUNTER_FIELDS
        },
        "gpu_timestamps_all_true": all(bool(row["gpu_timestamps"]) for row in rows),
    }
    if args.output is not None:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        with args.output.open("w", encoding="utf-8") as stream:
            json.dump(summary, stream, indent=2, sort_keys=True)
            stream.write("\n")
    print_summary(summary)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
