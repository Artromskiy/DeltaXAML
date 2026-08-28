using Delta.Diagnostics;
using DeltaXAML.Compiler;

internal static partial class Program
{
    private static void UnsupportedXamlTests()
    {
        var registry = XamlSemanticRegistry.CreateBuiltIns();
        var source = new SourceId(new Guid("44444444-4444-4444-4444-444444444402"));
        AssertCode(source, registry, "<TextBlock Text=\"{x:Reference Other}\" />", "XAML008", "unsupported markup extension");
        AssertCode(source, registry, "<Button Click=\"OnClick\" />", "XAML003", "XAML event handler");
        AssertCode(source, registry, "<Button AutomationName=\"Save\" />", "XAML003", "automation markup");
        AssertCode(source, registry, "<TextBlock Grid.Row=\"1\" />", "XAML003", "attached property");
        AssertCode(source, registry, "<TextBlock Text=\"{Binding Path=Text, RelativeSource=Self}\" />", "XAML008", "relative binding");
        AssertCode(source, registry, "<TextBlock Text=\"{TemplateBinding Text}\" />", "XAML008", "template binding");
    }

    private static void AssertCode(
        SourceId source,
        XamlSemanticRegistry registry,
        string xaml,
        string code,
        string scenario)
    {
        var result = XamlCompiler.Compile(source, xaml, registry);
        for (var i = 0; i < result.Diagnostics.Length; i++)
        {
            if (result.Diagnostics[i].Code.Value == code)
            {
                return;
            }
        }

        throw new InvalidOperationException($"{scenario} did not produce stable diagnostic {code}.");
    }
}
