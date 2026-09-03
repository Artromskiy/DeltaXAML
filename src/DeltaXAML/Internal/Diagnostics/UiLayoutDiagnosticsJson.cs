using System.Buffers;
using System.Text;
using System.Text.Json;
using Delta;

namespace DeltaXAML.Internal;

/// <summary>Serializes the authoritative retained tree for cold layout diagnostics.</summary>
internal static class UiLayoutDiagnosticsJson
{
    private const int SchemaVersion = 2;

    internal static string Write(
        UiElement root,
        float2 viewport,
        float dpiScale,
        bool layoutCompleted,
        bool indented)
    {
        ArgumentNullException.ThrowIfNull(root);
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = indented }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", SchemaVersion);
            writer.WriteBoolean("layoutCompleted", layoutCompleted);
            WriteSize(writer, "viewport", new(viewport.x, viewport.y));
            writer.WriteNumber("dpiScale", dpiScale);
            writer.WritePropertyName("root");
            WriteNode(writer, root, -1);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static void WriteNode(Utf8JsonWriter writer, UiElement element, int childIndex)
    {
        writer.WriteStartObject();
        if (childIndex >= 0)
        {
            writer.WriteNumber("childIndex", childIndex);
        }

        writer.WriteString("type", element.TypeName);
        writer.WriteNumber("id", element.Id.Value);
        writer.WriteNumber("generation", element.Generation);
        if (element is TextBlock textBlock)
        {
            writer.WriteString("text", textBlock.Text);
        }

        writer.WriteString("visibility", VisibilityName(element.Visibility));
        writer.WriteString("participation", element.Participation.ToString());
        WriteRect(writer, "bounds", element.Bounds);
        WriteRect(writer, "clip", element.Clip);
        WriteSize(writer, "desiredSize", element.DesiredSize);
        WriteRequestedSize(writer, element.Width, element.Height);
        WriteThickness(writer, "margin", element.Margin);
        WriteThickness(writer, "padding", element.Padding);

        writer.WriteStartArray("children");
        for (var index = 0; index < element.Children.Count; index++)
        {
            if (element.Children[index] is not UiElement child)
            {
                throw new InvalidOperationException("The retained tree contains a non-element child.");
            }

            WriteNode(writer, child, index);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteRect(Utf8JsonWriter writer, string name, UiRect value)
    {
        writer.WriteStartObject(name);
        writer.WriteNumber("x", value.X);
        writer.WriteNumber("y", value.Y);
        writer.WriteNumber("width", value.Width);
        writer.WriteNumber("height", value.Height);
        writer.WriteEndObject();
    }

    private static void WriteSize(Utf8JsonWriter writer, string name, UiSize value)
    {
        writer.WriteStartObject(name);
        writer.WriteNumber("width", value.Width);
        writer.WriteNumber("height", value.Height);
        writer.WriteEndObject();
    }

    private static void WriteRequestedSize(Utf8JsonWriter writer, float width, float height)
    {
        writer.WriteStartObject("requestedSize");
        WriteOptionalNumber(writer, "width", width);
        WriteOptionalNumber(writer, "height", height);
        writer.WriteEndObject();
    }

    private static void WriteThickness(Utf8JsonWriter writer, string name, UiThickness value)
    {
        writer.WriteStartObject(name);
        writer.WriteNumber("left", value.Left);
        writer.WriteNumber("top", value.Top);
        writer.WriteNumber("right", value.Right);
        writer.WriteNumber("bottom", value.Bottom);
        writer.WriteEndObject();
    }

    private static void WriteOptionalNumber(Utf8JsonWriter writer, string name, float value)
    {
        if (float.IsFinite(value))
        {
            writer.WriteNumber(name, value);
        }
        else
        {
            writer.WriteNull(name);
        }
    }

    private static string VisibilityName(UiVisibility visibility) => visibility switch
    {
        UiVisibility.Visible => "Visible",
        UiVisibility.Hidden => "Hidden",
        UiVisibility.Collapsed => "Collapsed",
        _ => "Unknown",
    };
}
