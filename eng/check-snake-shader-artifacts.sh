#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
shader_root="${DELTA_SHADER_ROOT:-$repo_root/../DeltaShader/src/DeltaShader/CompiledShaders}"
snake_project="$repo_root/samples/2048-electron/DeltaXAML.Samples.Snake.csproj"

if [[ ! -f "$snake_project" ]]; then
    printf 'Snake shader check: missing project: %s\n' "$snake_project" >&2
    exit 1
fi

if rg -n '<Content Include="\.\./(RoundedRectangle\.Render|Game2048\.Render)/shaders/' "$snake_project"; then
    printf 'Snake shader check: stale sample shader link remains in %s\n' "$snake_project" >&2
    exit 1
fi

required_links=(
    'DeltaShader.UI.ClipAwareSolidRectangleVertex.vert.spv'
    'DeltaShader.UI.ClipAwareSolidRectangleFragment.frag.spv'
    'DeltaShader.UI.ClipAwareRoundedRectangleVertex.vert.spv'
    'DeltaShader.UI.ClipAwareRoundedRectangleFragment.frag.spv'
    'DeltaShader.UI.ClipAwareRoundedRectangleSliceVertex.vert.spv'
    'DeltaShader.UI.ClipAwareRoundedRectangleSliceFragment.frag.spv'
    'DeltaShader.Text.SdfTextVertex.vert.spv'
    'DeltaShader.Text.SdfTextFragment.frag.spv'
)
for artifact in "${required_links[@]}"; do
    if ! rg -q "CompiledShaders/${artifact}" "$snake_project"; then
        printf 'Snake shader check: project does not link canonical artifact: %s\n' "$artifact" >&2
        exit 1
    fi
done

require_spirv() {
    local artifact="$1"
    local path="$shader_root/$artifact"
    local manifest="${path%.spv}.shader.json"
    if [[ ! -f "$path" || ! -s "$path" ]]; then
        printf 'Snake shader check: missing or empty SPIR-V: %s\n' "$path" >&2
        exit 1
    fi
    if [[ ! -f "$manifest" ]]; then
        printf 'Snake shader check: missing ABI sidecar: %s\n' "$manifest" >&2
        exit 1
    fi
    local magic
    magic="$(od -An -t x4 -N 4 "$path" | tr -d '[:space:]')"
    if [[ "$magic" != '07230203' ]]; then
        printf 'Snake shader check: %s is not a SPIR-V module (magic=%s)\n' "$path" "$magic" >&2
        exit 1
    fi
    if command -v spirv-val >/dev/null 2>&1; then
        spirv-val --target-env vulkan1.2 "$path" >/dev/null
    fi
}

for artifact in "${required_links[@]}"; do
    require_spirv "$artifact"
done

solid_vertex_manifest="$shader_root/DeltaShader.UI.ClipAwareSolidRectangleVertex.vert.shader.json"
vertex_manifest="$shader_root/DeltaShader.UI.ClipAwareRoundedRectangleVertex.vert.shader.json"
slice_vertex_manifest="$shader_root/DeltaShader.UI.ClipAwareRoundedRectangleSliceVertex.vert.shader.json"
text_vertex_manifest="$shader_root/DeltaShader.Text.SdfTextVertex.vert.shader.json"
text_fragment_manifest="$shader_root/DeltaShader.Text.SdfTextFragment.frag.shader.json"

for required in '"Stage": 1' '"Name": "Instances"' '"Set": 0' '"Binding": 0' '"ArrayStride": 96' '"Name": "Resolution"'; do
    if ! rg -q "$required" "$vertex_manifest"; then
        printf 'Snake shader check: clip-aware rounded vertex ABI is missing %s\n' "$required" >&2
        exit 1
    fi
done

for required in '"Stage": 1' '"Name": "Instances"' '"Set": 0' '"Binding": 0' '"ArrayStride": 48' '"Name": "Resolution"'; do
    if ! rg -q "$required" "$solid_vertex_manifest"; then
        printf 'Snake shader check: clip-aware solid vertex ABI is missing %s\n' "$required" >&2
        exit 1
    fi
done

for required in '"Stage": 1' '"Name": "Instances"' '"Set": 0' '"Binding": 0' '"ArrayStride": 112' '"Name": "Resolution"'; do
    if ! rg -q "$required" "$slice_vertex_manifest"; then
        printf 'Snake shader check: clip-aware rounded slice vertex ABI is missing %s\n' "$required" >&2
        exit 1
    fi
done

for required in '"Stage": 1' '"Name": "Glyphs"' '"Set": 0' '"Binding": 0' '"ArrayStride": 48' '"Name": "Resolution"'; do
    if ! rg -q "$required" "$text_vertex_manifest"; then
        printf 'Snake shader check: text vertex ABI is missing %s\n' "$required" >&2
        exit 1
    fi
done

for required in '"Stage": 2' '"Name": "Atlas"' '"Set": 0' '"Binding": 3' '"Name": "OutlineColor"'; do
    if ! rg -q "$required" "$text_fragment_manifest"; then
        printf 'Snake shader check: text fragment ABI is missing %s\n' "$required" >&2
        exit 1
    fi
done

printf 'Snake shader check: canonical DeltaShader UI/text artifacts are present and valid\n'
