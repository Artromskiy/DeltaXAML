# API review pointer

This historical review is no longer an API source of truth. The selected
user-facing shape is [LIBRARY_CONTRACT.md](LIBRARY_CONTRACT.md), the frozen
cross-project boundary is [CONTRACT.md](CONTRACT.md), and
implementation details belong in [INTERNAL.md](INTERNAL.md).

New work must not extend the temporary implementation surface or copy its
draw-list model into another public contract.
