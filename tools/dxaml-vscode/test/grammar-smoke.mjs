import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const directory = path.dirname(fileURLToPath(import.meta.url));
const extensionRoot = path.resolve(directory, '..');
const packageJson = JSON.parse(fs.readFileSync(path.join(extensionRoot, 'package.json'), 'utf8'));
const grammar = JSON.parse(fs.readFileSync(path.join(extensionRoot, 'syntaxes', 'dxaml.tmLanguage.json'), 'utf8'));
const languageConfiguration = JSON.parse(fs.readFileSync(path.join(extensionRoot, 'language-configuration.json'), 'utf8'));
const snippets = JSON.parse(fs.readFileSync(path.join(extensionRoot, 'snippets', 'dxaml.code-snippets'), 'utf8'));

assert.equal(packageJson.contributes.languages.length, 1);
assert.deepEqual(packageJson.contributes.languages[0].extensions, ['.dxaml']);
assert.equal(packageJson.contributes.grammars[0].scopeName, 'source.dxaml');
assert.equal(grammar.scopeName, 'source.dxaml');
assert.deepEqual(grammar.fileTypes, ['dxaml']);
assert.equal(repositoryScope(grammar, 'tag', '3'), 'entity.name.tag.xml.control.dxaml');
assert.equal(repositoryScope(grammar, 'tag', '4'), 'entity.name.tag.xml.markup.dxaml');
assert.equal(repositoryScope(grammar, 'tag', '5'), 'entity.name.tag.xml.custom.dxaml');
assert.equal(repositoryScope(grammar, 'property-element', '3'), 'entity.name.tag.xml.property-element.dxaml');
assert.equal(grammar.repository.color.name, 'constant.other.color.rgb-value.dxaml');
assert.ok(languageConfiguration.indentationRules);
assert.ok(Object.keys(snippets).length >= 4);

const repository = grammar.repository;
const tagBegin = new RegExp(repository.tag.begin);
const propertyElementBegin = new RegExp(repository['property-element'].begin);
const markupBegin = new RegExp(repository['markup-extension'].begin);

const controls = [
  'Border',
  'Button',
  'CollectionView',
  'ContentControl',
  'Grid',
  'Image',
  'ItemsControl',
  'Menu',
  'NumericEditor',
  'Overlay',
  'Panel',
  'Picker',
  'RichTextBlock',
  'ScrollViewer',
  'Slider',
  'StackPanel',
  'TabView',
  'TextBlock',
  'TextBox',
  'ToggleButton'
];

for (const control of controls) {
  const sample = `<${control}>`;
  assert.match(sample, tagBegin, `tag grammar does not accept ${sample}`);
}

assert.match('<local:CustomControl />', tagBegin);
assert.match('<Grid.RowDefinitions>', propertyElementBegin);
assert.match('<local:Editor.Text>', propertyElementBegin);
assert.match('{Binding Path=Title, Mode=OneWay}', markupBegin);
assert.match('{DynamicResource BoardCellColor}', markupBegin);
assert.match('&amp;', new RegExp(repository.entity.match));
assert.match('#80FF00AA', new RegExp(repository.color.match));
assert.match('320device', new RegExp(repository.number.match));
assert.match('CornerRadius="4"', new RegExp(repository['property-attribute'].begin));
assert.match('Grid.Row="1"', new RegExp(repository['attached-attribute'].begin));
assert.match('Clicked="OnClicked"', new RegExp(repository['event-attribute'].begin));
assert.match('x:Name="Title"', new RegExp(repository['directive-attribute'].begin));

console.log('DXAML grammar smoke: package, grammar, configuration and snippets are valid.');

function repositoryScope(source, rule, capture) {
  return source.repository[rule].beginCaptures[capture].name;
}
