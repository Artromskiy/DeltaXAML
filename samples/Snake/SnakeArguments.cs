namespace DeltaXaml.Samples.Snake;

internal static class SnakeArguments
{
    internal static (int Columns, int Rows) ParseGrid(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        for (var index = 0; index + 1 < args.Length; index++)
        {
            if (!string.Equals(args[index], "--grid", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = args[index + 1];
            var separator = value.IndexOf('x');
            if (separator < 1 || separator == value.Length - 1 ||
                !int.TryParse(value.AsSpan(0, separator), out var columns) ||
                !int.TryParse(value.AsSpan(separator + 1), out var rows))
            {
                throw new ArgumentException("--grid must use the CxR form, for example 16x12.", nameof(args));
            }

            return (columns, rows);
        }

        return (SnakeGame.DefaultColumns, SnakeGame.DefaultRows);
    }
}
