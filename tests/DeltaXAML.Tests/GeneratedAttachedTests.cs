using Delta.XAML;
using DeltaXaml.Generated;
using DeltaXaml.Tests;

internal static class GeneratedAttachedTests
{
    internal static void Run()
    {
        using var text = new EmptyTextService();
        using var artifact = new GeneratedAttachedArtifact(text);
        if (!artifact.TryFindName("Target", out var target))
        {
            throw new InvalidOperationException("generated attached-property target did not resolve");
        }

        Assert.Equal(8, target.GetAttachedValue(GeneratedLayout.Priority), "custom attached metadata emits a direct typed descriptor write");
    }
}
