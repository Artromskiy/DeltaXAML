using Delta.Maths;
using Delta.Text.Contract;
using Delta.XAML;
using DeltaXaml.Generated;
using DeltaXaml.Tests;

internal static class GeneratedSampleParityTests
{
    internal static void Run()
    {
        var fontPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "NotoSans-Regular.ttf");
        var fonts = new UiFontCatalog();
        fonts.Register(
            "default",
            new FontSourceId(new Guid("F5406C68-665D-4C7C-8D2B-EDEBA052EAB2")),
            File.ReadAllBytes(fontPath));
        using var text = new CountingTextService();

        using (var artifact = new SampleBehaviorsArtifact(new FullCapabilityModel(), text, fonts)) { Validate(artifact.Document, "Behaviors"); }
        using (var artifact = new SampleBrushesArtifact(text, fonts)) { Validate(artifact.Document, "Brushes"); }
        using (var artifact = new SampleControlGalleryArtifact(text, fonts)) { Validate(artifact.Document, "ControlGallery"); }
        using (var artifact = new SampleControlTemplatesArtifact(text, fonts)) { Validate(artifact.Document, "ControlTemplates"); }
        using (var artifact = new SampleDataBindingArtifact(new BindingModel { Name = "Delta" }, text, fonts))
        {
            Validate(artifact.Document, "DataBinding");
            Assert.Equal("Hello Delta", ((TextBlock)artifact.Document.Root.Children[2]).Text, "generated StringFormat uses its explicit culture");
            Assert.Equal("Delta / Delta", ((TextBlock)artifact.Document.Root.Children[3]).Text, "generated multi-source StringFormat uses typed context inputs");
        }
        using (var artifact = new SampleDataTemplatesArtifact(new CollectionModel(), text, fonts)) { Validate(artifact.Document, "DataTemplates"); }
        using (var artifact = new SampleHyperlinkArtifact(text, fonts)) { Validate(artifact.Document, "Hyperlink"); }
        using (var artifact = new SampleModalNavigationArtifact(text, fonts)) { Validate(artifact.Document, "ModalNavigation"); }
        using (var artifact = new SamplePassingDataArtifact(text, fonts)) { Validate(artifact.Document, "PassingData"); }
        using (var artifact = new SamplePickerArtifact(new CollectionModel(), text, fonts)) { Validate(artifact.Document, "Picker"); }
        using (var artifact = new SamplePopupsArtifact(text, fonts)) { Validate(artifact.Document, "Popups"); }
        using (var artifact = new SampleTabbedPageArtifact(text, fonts)) { Validate(artifact.Document, "TabbedPage"); }
        using (var artifact = new SampleThemingArtifact(text, fonts)) { Validate(artifact.Document, "Theming"); }
        using (var artifact = new SampleTipCalcArtifact(text, fonts)) { Validate(artifact.Document, "TipCalc"); }
        using (var artifact = new SampleTriggersArtifact(new FullCapabilityModel { Armed = true }, text, fonts)) { Validate(artifact.Document, "Triggers"); }
        using (var artifact = new SampleTwoPaneViewArtifact(text, fonts)) { Validate(artifact.Document, "TwoPaneView"); }
        using (var artifact = new SampleWorkingWithColorsArtifact(text, fonts)) { Validate(artifact.Document, "WorkingWithColors"); }
        using (var artifact = new SampleWorkingWithFilesArtifact(text, fonts)) { Validate(artifact.Document, "WorkingWithFiles"); }
        using (var artifact = new SampleWorkingWithFontsArtifact(text, fonts)) { Validate(artifact.Document, "WorkingWithFonts"); }
        using (var artifact = new SampleXamlFundamentalsArtifact(text, fonts)) { Validate(artifact.Document, "XamlFundamentals"); }
    }

    private static void Validate(UiDocument document, string name)
    {
        document.Layout(new float2(800, 600), 1);
        var display = document.BuildDisplayList();
        Assert.True(display.Visuals.Length + display.Text.Length != 0, $"generated sample '{name}' emits canonical display content");
    }
}
