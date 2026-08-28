using Delta.Diagnostics;
using DeltaXAML.Compiler;

internal static partial class Program
{
    private static void UnsupportedXamlTests()
    {
        var registry = XamlSemanticRegistry.CreateBuiltIns();
        var source = new SourceId(new Guid("44444444-4444-4444-4444-444444444402"));
        AssertDiagnostic(source, registry, "<TextBlock Text=\"{x:Reference Other}\" />", "XAML008", "ElementName");
        AssertDiagnostic(source, registry, "<Button Click=\"OnClick\" />", "XAML003", "Command");
        AssertDiagnostic(source, registry, "<Button NativeAutomationPeer=\"Save\" />", "XAML003", "AutomationName");
        AssertDiagnostic(source, registry, "<TextBlock Text=\"{Binding Path=Text, RelativeSource=Unknown}\" />", "XAML008", "RelativeSource");
        AssertDiagnostic(source, registry, "<Label />", "XAML002", "TextBlock");
        AssertDiagnostic(source, registry, "<Entry />", "XAML002", "TextBox");
        AssertDiagnostic(source, registry, "<FormattedString />", "XAML002", "RichTextBlock");
        AssertDiagnostic(source, registry, "<TapGestureRecognizer />", "XAML002", "Gestures");
        AssertDiagnostic(source, registry, "<TextBlock FormattedText=\"legacy\" />", "XAML003", "Span");
        AssertDiagnostic(source, registry, "<Button GestureRecognizers=\"legacy\" />", "XAML003", "Gestures");
    }

    private static void AssertDiagnostic(
        SourceId source,
        XamlSemanticRegistry registry,
        string xaml,
        string code,
        string messageFragment)
    {
        var result = XamlCompiler.Compile(source, xaml, registry);
        for (var i = 0; i < result.Diagnostics.Length; i++)
        {
            var diagnostic = result.Diagnostics[i];
            if (diagnostic.Code.Value == code &&
                diagnostic.Message.Contains(messageFragment, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        throw new InvalidOperationException(
            $"'{xaml}' did not produce diagnostic {code} containing '{messageFragment}'.");
    }
}
