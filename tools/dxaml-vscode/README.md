# DeltaXAML VS Code language support

This local extension provides syntax highlighting, indentation, pairs and
snippets for the DeltaXAML `.dxaml` dialect. It keeps XML structure readable
with XML-compatible TextMate scopes while adding DeltaXAML-specific scopes for
the current controls (`Grid`, `Border`, `TextBlock`, `TextBox`, `ItemsControl`,
`CollectionView`, `RichTextBlock`, `TabView`, and others), resource/style
elements, property elements, attached properties, events, `x:`/namespace
directives, markup extensions, binding modes, enums, lengths, colors, XML
entities and text content.

Element names, attribute names and color literals use XML/TextMate scope
families, so the active VS Code theme can apply its XML/XAML contrast rules
instead of requiring a DeltaXAML-specific theme.

The workspace associates `.dxaml` with the `delta-xaml` language id. Install
this local extension once from VS Code's `Extensions: Install from Location...`
command and select this directory. For development, launch a separate
Extension Development Host with:

```bash
code --extensionDevelopmentPath="$PWD/tools/dxaml-vscode" "$PWD"
```

Without the extension, select XML manually for a `.dxaml` file. The grammar is
deliberately lexical; semantic completion, project type resolution and compiler
diagnostics belong to a future language service backed by the DeltaXAML
compiler, not to TextMate.

Run the dependency-free grammar smoke check from this directory with:

```bash
npm test
```

The extension follows the XML/TextMate approach used by common XAML tooling:
lexical scopes provide portable editor coloring, while semantic completion and
diagnostics remain a separate language-service concern.
