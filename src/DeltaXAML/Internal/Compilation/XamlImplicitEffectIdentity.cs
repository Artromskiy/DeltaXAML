using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Delta.Diagnostics;
using Delta.XAML.Contract;

namespace DeltaXAML.Compiler;

internal static class XamlImplicitEffectIdentity
{
    internal static UiResourceId Create(SourceId source, XamlObjectPlan node, string target = "visual")
    {
        var identity = string.Concat(
            "DeltaXAML.ImplicitEffect/",
            source.Value.ToString("D"), "/",
            target, "/",
            node.Name.Namespace, "/",
            node.Name.LocalName, "/",
            node.Range.Start.Offset.ToString(CultureInfo.InvariantCulture), "/",
            node.Range.End.Offset.ToString(CultureInfo.InvariantCulture));
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
        return new UiResourceId(new Guid(bytes.AsSpan(0, 16)));
    }
}
