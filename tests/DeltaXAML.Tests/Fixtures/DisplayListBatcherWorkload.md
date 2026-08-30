# UI display-list batcher workload

`DisplayListBatcherWorkloadTests.cs` is a renderer-neutral producer fixture for
validating a contiguous UI batcher. It depends only on the existing
`DeltaXAML.Contract` and the already-present test text service; it has no
DeltaRender, Vulkan, SDL, ECS or shader dependency.

`UiDisplayList.Order` is the canonical mixed command stream. Every entry is a
`UiDrawRef` selecting an item from `Visuals` or `Text`; the order stream is not
reconstructed from payload arrays by the consumer.

The fixture allocates its maximum payload arrays once (`5,300` entries and
`256` clips) and rewrites them in place. The returned `UiDisplayList` is
borrowed until the next workload build, matching the contract lifetime.

## Frame transitions

| Frame | Visuals | Text | Clips | Order | Input change from base |
| --- | ---: | ---: | ---: | ---: | --- |
| `Base` | 3,500 | 1,500 | 256 | 5,000 | initial add of 5,000 entries |
| `Paint` | 3,500 | 1,500 | 256 | 5,000 | paint/material changes for 715 entries (500 visual, 215 text) |
| `Clip` | 3,500 | 1,500 | 256 | 5,000 | 52 clip regions change; command order and text versions stay stable |
| `Reorder` | 3,500 | 1,500 | 256 | 5,000 | deterministic permutation moves 4,999 entries and repacks payload spans |
| `Churn` | 3,527 | 1,509 | 256 | 5,036 | remove 264 entries (183 visual, 81 text), add 300 (210 visual, 90 text) |

`Paint` increments `UiTextDraw.Version` for exactly 215 text entries. `Clip`
does not increment text versions because clipping is separate draw data.
Added entries use text generation `2`; retained base entries use generation `1`.

## Expected checksums

Checksums use fixed FNV-1a over primitive contract fields, GUID bytes and
IEEE-754 float bits. They do not hash object references. `OrderChecksum`
isolates the ordered command stream; `PayloadChecksum` covers visual, clip and
text payloads; `FullChecksum` covers payloads followed by `Order`.

| Frame | OrderChecksum | PayloadChecksum | FullChecksum |
| --- | ---: | ---: | ---: |
| `Base` | `4322472588939357607` | `7525632923540591110` | `9031151385499634758` |
| `Paint` | `4322472588939357607` | `13327287353690654130` | `2277488697916662658` |
| `Clip` | `4322472588939357607` | `4837542823663243782` | `5327905869668322374` |
| `Reorder` | `7877403130303307063` | `3064124748223052344` | `4165849485099287768` |
| `Churn` | `15779854436547583369` | `7775226777097878523` | `15413119183328370041` |

The checksum is a fixture guard, not a renderer ABI. A Render-side batcher can
consume the borrowed list, compare the canonical `Order`, and use the counts
and transition table to validate add/remove/reorder and bounded payload
updates.
