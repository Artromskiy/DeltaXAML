# DeltaXAML trace report

This is a cold-path diagnostic tool for `.nettrace` files collected with
`dotnet-trace` and the `gc-verbose` profile. It aggregates sampled
`GCAllocationTick` events by managed `TypeName`, reporting sampled bytes and
event counts. Optional start and end times filter the report in seconds.

The report is complementary to the persistent text-mutation probe in
`benchmarks/DeltaXAML.TextMutation`. The probe's
`GC.GetAllocatedBytesForCurrentThread` counter is the source for exact
per-operation allocation comparisons; this tool identifies the managed type
families visible in a trace. It does not inspect application text, reference
DeltaXAML or DeltaText, or require a renderer.

Capture a trace from the DeltaXAML text probe:

```sh
dotnet tool install --tool-path /tmp/dotnet-trace-tools dotnet-trace
dotnet-trace collect --profile gc-verbose \
  --output /tmp/deltaxaml-text-gc.nettrace -- \
  /usr/local/share/dotnet/dotnet \
  benchmarks/DeltaXAML.TextMutation/bin/Release/net8.0/DeltaXAML.TextMutation.dll
```

Build and run the report from the DeltaXAML project root:

```sh
dotnet restore tools/DeltaXAML.TraceReport/DeltaXAML.TraceReport.csproj
dotnet run --project tools/DeltaXAML.TraceReport/DeltaXAML.TraceReport.csproj \
  -c Release --no-restore -- /tmp/deltaxaml-text-gc.nettrace
```

To report only an interval, append start and end seconds:

```sh
dotnet run --project tools/DeltaXAML.TraceReport/DeltaXAML.TraceReport.csproj \
  -c Release --no-restore -- /tmp/deltaxaml-text-gc.nettrace 0.10 0.40
```

`GCAllocationTick` is sampled by the runtime, so its totals are not a complete
per-operation allocation count. Use the bounded probe for allocation deltas
and this report for attribution evidence.
