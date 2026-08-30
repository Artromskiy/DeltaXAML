#!/usr/bin/env python3
"""Render a DeltaXAML layout-diagnostics JSON snapshot as translucent SVG boxes."""

from __future__ import annotations

import html
import json
import sys
from pathlib import Path


PALETTE = ("#2563EB", "#059669", "#D97706", "#9333EA", "#DB2777", "#0891B2")


def number(value: float) -> str:
    return f"{value:.3f}".rstrip("0").rstrip(".")


def rect(value: dict[str, float]) -> tuple[float, float, float, float]:
    return (
        float(value["x"]),
        float(value["y"]),
        float(value["width"]),
        float(value["height"]),
    )


def emit_node(node: dict, depth: int, output: list[str]) -> None:
    color = PALETTE[depth % len(PALETTE)]
    x, y, width, height = rect(node["bounds"])
    clip_x, clip_y, clip_width, clip_height = rect(node["clip"])
    label = html.escape(f"{node['type']}  id={node['id']}  depth={depth}")
    output.append(
        f'  <g data-type="{html.escape(str(node["type"]))}" '
        f'data-id="{node["id"]}" data-generation="{node["generation"]}">'
    )
    output.append(
        f'    <title>{label} bounds={number(x)},{number(y)} '
        f'{number(width)}x{number(height)}</title>'
    )
    output.append(
        f'    <rect x="{number(x)}" y="{number(y)}" '
        f'width="{number(width)}" height="{number(height)}" '
        f'fill="{color}" fill-opacity="0.12" stroke="{color}" '
        f'stroke-opacity="0.9" stroke-width="1" />'
    )
    output.append(
        f'    <rect x="{number(clip_x)}" y="{number(clip_y)}" '
        f'width="{number(clip_width)}" height="{number(clip_height)}" '
        f'fill="none" stroke="#DC2626" stroke-opacity="0.7" '
        f'stroke-width="0.8" stroke-dasharray="4 3" />'
    )
    for index, child in enumerate(node.get("children", ())):
        emit_node(child, depth + 1, output)
    output.append("  </g>")


def render(source: Path, destination: Path) -> None:
    document = json.loads(source.read_text(encoding="utf-8"))
    viewport = document["viewport"]
    width = float(viewport["width"])
    height = float(viewport["height"])
    output = [
        f'<svg xmlns="http://www.w3.org/2000/svg" width="{number(width)}" '
        f'height="{number(height)}" viewBox="0 0 {number(width)} {number(height)}">',
        "  <rect width=\"100%\" height=\"100%\" fill=\"#F8FAFC\" />",
        "  <g id=\"layout\" shape-rendering=\"geometricPrecision\">",
    ]
    emit_node(document["root"], 0, output)
    output.extend(
        [
            "  </g>",
            '  <g id="legend" font-family="monospace" font-size="12" fill="#111827">',
            '    <text x="8" y="16">solid = bounds; red dashed = clip</text>',
            "  </g>",
            "</svg>",
        ]
    )
    destination.write_text("\n".join(output) + "\n", encoding="utf-8")


def main() -> int:
    if len(sys.argv) != 3:
        print(f"usage: {sys.argv[0]} layout.json layout.svg", file=sys.stderr)
        return 2

    render(Path(sys.argv[1]), Path(sys.argv[2]))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
