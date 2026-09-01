namespace DeltaXaml.Samples.Snake;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (!HasFlag(args, "--headless"))
        {
            return await SnakeWindowRunner.RunAsync(args).ConfigureAwait(false);
        }

        return await SnakeHeadlessRenderRunner.RunAsync(args).ConfigureAwait(false);
    }

    private static bool HasFlag(string[] args, string option)
    {
        for (var index = 0; index < args.Length; index++)
        {
            if (string.Equals(args[index], option, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
