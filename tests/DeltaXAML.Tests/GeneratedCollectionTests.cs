using Delta.Maths;
using Delta.XAML;
using DeltaXaml.Generated;
using DeltaXaml.Tests;

internal static class GeneratedCollectionTests
{
    internal static void Run()
    {
        var model = new CollectionModel();
        using var text = new EmptyTextService();
        using var artifact = new GeneratedCollectionArtifact(model, text);
        artifact.Document.Layout(new float2(200, 80), 1);
        var collection = (UiCollectionView)artifact.Document.Root.Children[0];
        Assert.Equal(2, collection.ItemsHost.Children.Count, "generated ItemsSource realizes the typed visible range");
        Assert.Equal("One:False", ((TextBlock)collection.ItemsHost.Children[0]).Text, "generated item plan performs a direct typed multi-source read");
        Assert.True(collection.ItemsHost.Children[1] is UiBorder, "generated selector chooses the declared alternate template without a selector object");
        Assert.Equal("Two", ((TextBlock)collection.ItemsHost.Children[1].Children[0]).Text, "generated selector template preserves typed item binding");
    }
}
