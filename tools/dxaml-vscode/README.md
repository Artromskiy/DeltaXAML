# DeltaXAML VS Code language support

This local extension provides syntax highlighting and editor pairs for the
DeltaXAML `.dxaml` dialect. It keeps XML structure readable while adding
DeltaXAML-specific scopes for controls, resource/style elements, properties,
attached properties, events, `x:`/namespace directives, markup extensions,
binding modes, enums, lengths, colors and text content.

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
