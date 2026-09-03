using System.Globalization;
using Delta;
using Microsoft.Diagnostics.Tracing;

namespace DeltaXAML.TraceReport;

internal static class Program
{
    private static void Main(string[] args)
    {
        if (args.Length is < 1 or > 3)
        {
            throw new ArgumentException(
                "Usage: DeltaXAML.TraceReport <trace.nettrace> [startSeconds] [endSeconds]");
        }

        var startSeconds = args.Length >= 2
            ? double.Parse(args[1], CultureInfo.InvariantCulture)
            : double.NegativeInfinity;
        var endSeconds = args.Length >= 3
            ? double.Parse(args[2], CultureInfo.InvariantCulture)
            : double.PositiveInfinity;
        var allocations = new Dictionary<string, AllocationSummary>(StringComparer.Ordinal);
        var minTimestamp = double.PositiveInfinity;
        var maxTimestamp = double.NegativeInfinity;
        var source = new EventPipeEventSource(args[0]);

        source.Clr.GCAllocationTick += data =>
        {
            var timestampSeconds = data.TimeStampRelativeMSec / 1_000;
            minTimestamp = Maths.Min(minTimestamp, timestampSeconds);
            maxTimestamp = Maths.Max(maxTimestamp, timestampSeconds);
            if (timestampSeconds < startSeconds || timestampSeconds > endSeconds)
            {
                return;
            }

            var name = data.TypeName ?? "<unknown>";
            allocations.TryGetValue(name, out var value);
            allocations[name] = new AllocationSummary(
                checked(value.Bytes + (long)data.AllocationAmount64),
                value.Events + 1);
        };

        source.Process();
        Console.Error.WriteLine(
            FormattableString.Invariant(
                $"allocation event range: {minTimestamp:F6}..{maxTimestamp:F6} seconds"));

        foreach (var item in allocations
                     .OrderByDescending(static pair => pair.Value.Bytes)
                     .Take(40))
        {
            Console.WriteLine(
                FormattableString.Invariant(
                    $"{item.Value.Bytes,12} bytes {item.Value.Events,8} events {item.Key}"));
        }
    }

    private readonly record struct AllocationSummary(long Bytes, int Events);
}
