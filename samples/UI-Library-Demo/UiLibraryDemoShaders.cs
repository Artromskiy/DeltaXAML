using Delta.Render;
using Delta.Shader.Contract;
using Delta.Shader.Text;
using Delta.Shader.UI;
using TextShaders = Delta.Shader.Text.Shaders;
using UiShaders = Delta.Shader.UI.Shaders;

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

    internal static GraphicsShaderProgram Text() => Create(
        TextShaders.Spv.TextShaders.SdfText.Vertex(),
        TextShaders.Spv.TextShaders.SdfText.Fragment(),
        TextShaders.Abi.TextShaders.SdfText.Vertex(),
        TextShaders.Abi.TextShaders.SdfText.Fragment());

    private static GraphicsShaderProgram Create(
        ReadOnlySpan<byte> vertexSpirv,
        ReadOnlySpan<byte> fragmentSpirv,
        ShaderAbi vertexAbi,
        ShaderAbi fragmentAbi) => new(
        new ShaderArtifact(vertexSpirv, "main", vertexAbi),
        new ShaderArtifact(fragmentSpirv, "main", fragmentAbi));
}
