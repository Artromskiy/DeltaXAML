using Delta.Render;
using UiShaders = Delta.Render.UIShaders.Shaders;
using TextShaders = Delta.Render.Text.Shaders;
using Delta.Shader.Contract;

namespace DeltaXaml.Samples.UiLibraryDemo;

internal static class UiLibraryDemoShaders
{
    internal static GraphicsShaderProgram SolidRectangle() => Create(
        UiShaders.Spv.UiRectangleShaders.SolidRectangle.Vertex(),
        UiShaders.Spv.UiRectangleShaders.SolidRectangle.Fragment(),
        UiShaders.Abi.UiRectangleShaders.SolidRectangle.Vertex(),
        UiShaders.Abi.UiRectangleShaders.SolidRectangle.Fragment());

    internal static GraphicsShaderProgram RoundedRectangle() => Create(
        UiShaders.Spv.UiRectangleShaders.RoundedRectangle.Vertex(),
        UiShaders.Spv.UiRectangleShaders.RoundedRectangle.Fragment(),
        UiShaders.Abi.UiRectangleShaders.RoundedRectangle.Vertex(),
        UiShaders.Abi.UiRectangleShaders.RoundedRectangle.Fragment());

    internal static GraphicsShaderProgram SolidStrokeRectangle() => Create(
        UiShaders.Spv.UiRectangleShaders.SolidStroke.Vertex(),
        UiShaders.Spv.UiRectangleShaders.SolidStroke.Fragment(),
        UiShaders.Abi.UiRectangleShaders.SolidStroke.Vertex(),
        UiShaders.Abi.UiRectangleShaders.SolidStroke.Fragment());

    internal static GraphicsShaderProgram RoundedStrokeRectangle() => Create(
        UiShaders.Spv.UiRectangleShaders.RoundedStroke.Vertex(),
        UiShaders.Spv.UiRectangleShaders.RoundedStroke.Fragment(),
        UiShaders.Abi.UiRectangleShaders.RoundedStroke.Vertex(),
        UiShaders.Abi.UiRectangleShaders.RoundedStroke.Fragment());

    internal static GraphicsShaderProgram RoundedInnerEffect() => Create(
        UiShaders.Spv.UiRectangleShaders.RoundedInnerShadow.Vertex(),
        UiShaders.Spv.UiRectangleShaders.RoundedInnerShadow.Fragment(),
        UiShaders.Abi.UiRectangleShaders.RoundedInnerShadow.Vertex(),
        UiShaders.Abi.UiRectangleShaders.RoundedInnerShadow.Fragment());

    internal static GraphicsShaderProgram LinearGradient() => Create(
        UiShaders.Spv.UiResourceShaders.SolidLinearGradient.Vertex(),
        UiShaders.Spv.UiResourceShaders.SolidLinearGradient.Fragment(),
        UiShaders.Abi.UiResourceShaders.SolidLinearGradient.Vertex(),
        UiShaders.Abi.UiResourceShaders.SolidLinearGradient.Fragment());

    internal static GraphicsShaderProgram Text() => Create(
        TextShaders.Spv.TextShaders.SdfText.Vertex(),
        TextShaders.Spv.TextShaders.SdfText.Fragment(),
        TextShaders.Abi.TextShaders.SdfText.Vertex(),
        TextShaders.Abi.TextShaders.SdfText.Fragment());

    internal static GraphicsShaderProgram TextStroke() => Create(
        TextShaders.Spv.TextShaders.SdfTextStroke.Vertex(),
        TextShaders.Spv.TextShaders.SdfTextStroke.Fragment(),
        TextShaders.Abi.TextShaders.SdfTextStroke.Vertex(),
        TextShaders.Abi.TextShaders.SdfTextStroke.Fragment());

    internal static GraphicsShaderProgram TextOuterShadow() => Create(
        TextShaders.Spv.TextShaders.SdfTextOuterShadow.Vertex(),
        TextShaders.Spv.TextShaders.SdfTextOuterShadow.Fragment(),
        TextShaders.Abi.TextShaders.SdfTextOuterShadow.Vertex(),
        TextShaders.Abi.TextShaders.SdfTextOuterShadow.Fragment());

    internal static GraphicsShaderProgram TextOuterGlowOnly() => Create(
        TextShaders.Spv.TextShaders.SdfTextOuterGlowOnly.Vertex(),
        TextShaders.Spv.TextShaders.SdfTextOuterGlowOnly.Fragment(),
        TextShaders.Abi.TextShaders.SdfTextOuterGlowOnly.Vertex(),
        TextShaders.Abi.TextShaders.SdfTextOuterGlowOnly.Fragment());

    internal static GraphicsShaderProgram TextInnerShadow() => Create(
        TextShaders.Spv.TextShaders.SdfTextInnerShadow.Vertex(),
        TextShaders.Spv.TextShaders.SdfTextInnerShadow.Fragment(),
        TextShaders.Abi.TextShaders.SdfTextInnerShadow.Vertex(),
        TextShaders.Abi.TextShaders.SdfTextInnerShadow.Fragment());

    internal static GraphicsShaderProgram TextInnerGlowOnly() => Create(
        TextShaders.Spv.TextShaders.SdfTextInnerGlowOnly.Vertex(),
        TextShaders.Spv.TextShaders.SdfTextInnerGlowOnly.Fragment(),
        TextShaders.Abi.TextShaders.SdfTextInnerGlowOnly.Vertex(),
        TextShaders.Abi.TextShaders.SdfTextInnerGlowOnly.Fragment());

    private static GraphicsShaderProgram Create(
        ReadOnlySpan<byte> vertexSpirv,
        ReadOnlySpan<byte> fragmentSpirv,
        ShaderAbi vertexAbi,
        ShaderAbi fragmentAbi) => new(
        new ShaderArtifact(vertexSpirv, "main", vertexAbi),
        new ShaderArtifact(fragmentSpirv, "main", fragmentAbi));
}
