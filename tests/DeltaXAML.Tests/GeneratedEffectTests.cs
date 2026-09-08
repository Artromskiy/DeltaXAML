using Delta;
using Delta.XAML;
using Delta.XAML.Contract;
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
        Assert.Equal(
            UiEffectCapabilities.Stroke | UiEffectCapabilities.OuterShadow | UiEffectCapabilities.InnerShadow |
            UiEffectCapabilities.OuterGlow | UiEffectCapabilities.InnerGlow,
            initial.Set.Capabilities,
            "generated bindings preserve the shared five-layer capability set");
        Assert.Equal(2f, initial.Parameters.Stroke.Width, "bound stroke width reaches typed effect parameters");
        Assert.Equal(6f, initial.Parameters.OuterGlow.BlurRadius, "bound outer-glow radius reaches typed effect parameters");

        var initialVersion = artifact.Resources.EffectVersion;
        var oneTimeColor = initial.Parameters.OuterGlow.Color;
        model.Accent = new(255, 96, 48);
        model.OuterGlowColor = new(255, 255, 255);
        model.OuterGlowRadius = 10;
        artifact.Document.Layout(new float2(160, 120), 1);

        Assert.True(artifact.Resources.EffectVersion > initialVersion, "reactive effect values advance the resource catalog version");
        Assert.True(artifact.Resources.TryResolveEffectResource(surface.EffectSet.Resource, out var updated), "updated effect keeps the same stable resource identity");
        Assert.Equal(10f, updated.Parameters.OuterShadow.BlurRadius, "outer-shadow binding reaches its typed layer slot");
        Assert.Equal(10f, updated.Parameters.InnerShadow.BlurRadius, "inner-shadow binding reaches its typed layer slot");
        Assert.Equal(10f, updated.Parameters.OuterGlow.BlurRadius, "one-way outer-glow binding updates during the generated binding stage");
        Assert.Equal(10f, updated.Parameters.InnerGlow.BlurRadius, "inner-glow binding reaches its typed layer slot");
        Assert.Equal(oneTimeColor, updated.Parameters.OuterGlow.Color, "one-time outer-glow binding is not reevaluated by another layer update");
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
