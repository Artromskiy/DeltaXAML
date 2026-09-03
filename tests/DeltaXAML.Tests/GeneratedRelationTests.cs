using Delta;
using Delta.XAML;
using DeltaXaml.Generated;

internal static class GeneratedRelationTests
{
    internal static void Run()
    {
        using var text = new EmptyTextService();
        using var artifact = new GeneratedRelationsArtifact(text);
        artifact.Document.Layout(new float2(240, 120), 1);

        if (!artifact.TryFindName("Source", out var sourceElement) || sourceElement is not TextBlock source)
        {
            throw new InvalidOperationException("generated namescope source did not resolve");
        }

        if (!artifact.TryFindName("Mirror", out var mirrorElement) || mirrorElement is not TextBlock mirror)
        {
            throw new InvalidOperationException("generated relation target did not resolve");
        }
        Assert.Equal("alpha", mirror.Text, "ElementName relation reads through a generated typed plan");
        source.Text = "beta";
        artifact.Document.Layout(new float2(240, 120), 1);
        Assert.Equal("beta", mirror.Text, "changed named source refreshes in the same fixed binding stage");
        if (!artifact.TryFindName("Combined", out var combinedElement) || combinedElement is not TextBlock combined)
        {
            throw new InvalidOperationException("generated multi-binding target did not resolve");
        }

        Assert.Equal("beta:right", combined.Text, "generated multi-source function reads direct typed inputs");
        source.Text = "gamma";
        artifact.Document.Layout(new float2(240, 120), 1);
        Assert.Equal("gamma:right", combined.Text, "multi-source binding updates only after an input changes");
        if (!artifact.TryFindName("Toggle", out var toggleElement) || toggleElement is not UiToggleButton toggle ||
            !artifact.TryFindName("TriggerTarget", out var triggerElement) || triggerElement is not TextBlock triggerTarget)
        {
            throw new InvalidOperationException("generated trigger elements did not resolve");
        }

        toggle.IsSelected = true;
        artifact.Document.Layout(new float2(240, 160), 1);
        Assert.Equal(new UiColor(204, 34, 0), triggerTarget.Foreground, "compiled property condition writes the canonical trigger source");
        Assert.Equal(1, artifact.Document.SemanticCommands.Length, "trigger transition publishes one deferred semantic action");
        artifact.Document.Layout(new float2(240, 160), 1);
        Assert.Equal(0, artifact.Document.SemanticCommands.Length, "unchanged trigger does not republish its action");

        if (!artifact.TryFindName("AncestorValue", out var ancestorElement))
        {
            throw new InvalidOperationException("ancestor target did not resolve");
        }

        Assert.Equal(new UiColor(17, 34, 51), ancestorElement.Background, "ancestor relation caches and reads the generated descriptor source");
        if (!artifact.TryFindName("AncestorMulti", out var ancestorMultiElement))
        {
            throw new InvalidOperationException("ancestor multi-binding target did not resolve");
        }

        Assert.Equal(new UiColor(17, 34, 51), ancestorMultiElement.Background, "multi-source plans reuse generation-cached ancestor relations");
    }
}
