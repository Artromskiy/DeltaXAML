using Delta;
using Delta.XAML;
using DeltaXaml.Generated;
using DeltaXaml.Tests;

internal static class GeneratedEffectTests
{
    internal static void Run()
    {
        var model = new EffectBindingModel();
        using var text = new EmptyTextService();
        using var artifact = new BoundEffectsArtifact(model, text);
        artifact.Document.Layout(new float2(160, 120), 1);

        var surface = FindSurface(artifact);
        Assert.True(surface.EffectSet.IsValid, "generated effect binding assigns one stable effect set");
        Assert.True(artifact.Resources.TryResolveEffectResource(surface.EffectSet.Resource, out var initial), "generated effect resource is registered in the artifact catalog");
        Assert.Equal(2f, initial.Parameters.StrokeOrOutline.Width, "bound stroke width reaches typed effect parameters");
        Assert.Equal(6f, initial.Parameters.Glow.BlurRadius, "bound glow radius reaches typed effect parameters");

        var initialVersion = artifact.Resources.EffectVersion;
        var oneTimeColor = initial.Parameters.Glow.Color;
        model.Accent = new(255, 96, 48);
        model.GlowColor = new(255, 255, 255);
        model.GlowRadius = 10;
        artifact.Document.Layout(new float2(160, 120), 1);

        Assert.True(artifact.Resources.EffectVersion > initialVersion, "reactive effect values advance the resource catalog version");
        Assert.True(artifact.Resources.TryResolveEffectResource(surface.EffectSet.Resource, out var updated), "updated effect keeps the same stable resource identity");
        Assert.Equal(10f, updated.Parameters.Glow.BlurRadius, "one-way effect binding updates during the generated binding stage");
        Assert.Equal(oneTimeColor, updated.Parameters.Glow.Color, "one-time effect binding is not reevaluated by another layer update");
    }

    private static UiBorder FindSurface(BoundEffectsArtifact artifact)
    {
        if (!artifact.TryFindName("Surface", out var element) || element is not UiBorder surface)
        {
            throw new InvalidOperationException("Generated effect surface was not found.");
        }

        return surface;
    }
}
