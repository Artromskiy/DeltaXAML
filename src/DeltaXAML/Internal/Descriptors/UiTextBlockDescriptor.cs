namespace DeltaXAML.Internal;

/// <summary>Compact generated type identity used by the retained descriptor path.</summary>
internal readonly record struct UiRuntimeTypeIndex(ushort Value);

internal readonly record struct UiTypeDescriptor(UiRuntimeTypeIndex Index);

/// <summary>Typed companion for <see cref="TextBlock"/>; the generated path owns no instance state.</summary>
internal static class UiTextBlockGenerated
{
    internal static readonly UiTypeDescriptor Descriptor = new(new(1));

    internal static TextBlock Create() => new();

    internal static void Measure(ref TextBlockState state, in UiTextMeasureContext context) =>
        TextBlockMeasureMixin.Measure(ref state, in context);

    internal static UiTextRun EmitVisual(ref TextBlockState state, in UiTextVisualContext context) =>
        TextBlockVisualMixin.EmitVisual(ref state, in context);
}
