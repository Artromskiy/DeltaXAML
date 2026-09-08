# DeltaXAML TODO

## Cross-project integration

Cross-project ownership, integration and shared acceptance remain tracked in
[../CONTRACTS.md](../CONTRACTS.md). Add future project work here only
after it has been explicitly selected; keep research and unselected alternatives
in [IDEAS.md](IDEAS.md).

## UI Library Demo — selected feature implementation plan

План составлен 2026-09-07 по запросу пользователя. Это **план реализации**, а
не заявление о поддержке и не разрешение менять frozen-контракты. Исходная
точка: HEAD `55d7046` и сохранённый рабочий diff визуального порта
`samples/UI-Library-Demo`. Electron-оригинал не изменять.

Источники требований:

- [CODE_STYLE](../CODE_STYLE.md) — ownership, cost zones, форма API и кода;
- [INTERNAL](docs/INTERNAL.md) — descriptor/mixin runtime, shared text cache,
  fixed stages, compilation и ownership;
- [WORKFLOW](WORKFLOW.md) — architecture gate и проверки;
- [LIBRARY_CONTRACT](docs/LIBRARY_CONTRACT.md) и
  [CONTRACT](docs/CONTRACT.md) — неизменяемые в рамках этого плана границы;
- [VISUAL_GAPS](samples/UI-Library-Demo/VISUAL_GAPS.md) — наблюдавшиеся отличия;
- `/Users/rum/GitProjects/yage-ui-library-sample/styles.css` и `index.html` —
  конкретный визуал, но не требование реализовать CSS/browser runtime.

Цель: убрать необходимые визуальные приближения галереи и повторяющийся код
композиции штатными typed/generated средствами. Не создавать новый хост,
редактор, CSS engine, полный каталог контролов или общую систему анимации.
Наличие API, корректность producer payload и фактические пиксели — три разные
стадии готовности. Не закрывать весь пункт по одной из них.

### Архитектурные ограничения для каждого пункта

1. `UiElement`/конкретный `Ui*` — identity и accessors одного retained owner.
   `Internal/State/*State.cs` — данные; generic interface — capability shape;
   stateless `readonly struct` в `Internal/Mixins` — алгоритм;
   `Internal/Descriptors` — direct typed thunks. Не добавлять доменную логику
   в публичные controls или default interface methods. Имена новых классов и
   файлов совпадают; XAML-имя не содержит префикс `Ui`.
2. Один node store для logical/visual relations; один property resolver:
   `Default < Style/Trigger < Binding < Local < Handle < Animation`.
   Вложенные шаблоны не владеют вторым документом или копией logical content.
3. Сохранять порядок из `INTERNAL#DXAML-RUNTIME-2`: input по последней
   committed geometry → mutation → binding → style/resource → measure →
   arrange → focus repair; затем visual extraction. Не добавлять рекурсивные
   вызовы стадий или отдельный full-tree prepass ради новой фичи.
4. Source writes валидируются/coerce на mutation boundary; равное effective
   value не повторяет работу. Derived geometry, зависящая от bounds,
   обновляется при dirty arrange/extraction, не в каждом shader invocation.
   Не заставлять Render повторять producer-side нормализацию геометрии.
5. Внутри node/child/glyph loops — typed state по `ref`, arrays/spans и
   явные циклы. Один descriptor dispatch на element/stage допустим по
   архитектуре; discovery, LINQ, boxing, closures, `Type`, строковые property
   lookup и per-element подписки — нет. Reused buffers растут только при
   необходимости; у каждого owner/cached result указан lifetime.
6. Compiler cold path может иметь immutable plans и удобные коллекции.
   Генератор выпускает direct factories/setters/attachment/binding batches,
   не требует `partial` controls и не подставляет runtime loader fallback.
   Диагностика имеет стабильный код и точный `Delta.Diagnostics.SourceRange`.
7. Layout/clip/baseline остаются logical top-left, X right, Y down.
   `DpiScale` публикуется один раз; Device paint не масштабируется повторно.
   Text alignment опирается на advance/line metrics, а не ink bounds.
8. `UiDisplayList` остаётся borrowed document-owned output с canonical
   `Order` и `Identities.Length == Order.Length`. При нескольких draws одного
   control каждый ordered item сохраняет самостоятельную lifetime identity
   и корректную payload version; индексы массива не становятся identity.
9. GPU resources/atlas/batching принадлежат Render, shaping/glyphs — Text,
   shader programs/typed packing — Shader. XAML не содержит Vulkan/std430,
   raw CLR upload или вручную написанного shader ABI.
10. Для одного owner использовать конкретный тип. Новый interface допустим
    только для реального capability/внешней границы, не ради будущего второго
    implementation. Не дробить файлы механически ради LOC/метрик.

### Очерёдность и границы

Все пункты ниже открыты. Названия предлагаемых новых properties — design
names, не текущий синтаксис. Внешние пункты — handoff requirements, а не
выданные коллегам задания и не разрешение менять их репозитории.

| Этап / ID | Результат | Владелец / зависимость |
| --- | --- | --- |
| A / UI-01 | Корректный generated binding для ненотифицирующих моделей | XAML |
| A / UI-02 | Viewport-aware ScrollViewer без `PageWidth` workaround | XAML |
| B / UI-03 | Gap, natural wrap и auto-column layout | XAML; UI-02 |
| B / UI-04 | Compiled style reuse и header/content projection | XAML |
| C / UI-05 | Реальные value controls с повторно используемым оформлением | XAML/sample; UI-04 |
| C / UI-06 | Tracking и явно заданный font fallback | XAML + Matt; часть требует Text API review |
| D / UI-07 | Gradient fill со stroke/radii без потери paint | XAML + Rend/Shad; resource-boundary review |
| D / UI-08 | Per-side borders, dashed stroke, inset shadow | XAML + Rend/Shad; paint-boundary review |
| D / UI-09 | Render transform с правильными clips/input | XAML + Rend/Shad; contract revision gate |
| E / UI-10 | Галерея на единственном production path, точные remaining gaps | XAML; предыдущие пункты |
| Параллельно / EXT-01 | Variable font и два исчезающих glyphs | Matt/Rend; причина glyph gap пока не установлена |

Сначала завершать A–C, не блокируя их ожиданием нового render contract.
Никакая будущая public signature из D не считается согласованной самим
наличием этого TODO. До ревизии оставить диагностируемое ограничение, не
маскировать его под успешно поддержанный `Custom`.

### UI-01 — generated bindings без обязательного INotifyPropertyChanged

**Факт:** `CSharpArtifactEmitter` безусловно генерирует pattern
`context is INotifyPropertyChanged`, если есть scalar bindings. Для sealed
несовместимого source это даёт CS8121. В sample добавлен notifying view model;
это не доказательство, что уведомления обязательны для любого binding.

- [ ] Передать из Roslyn adapter в typed binding plan проверенную форму
  source: notification capability, binding modes и способ refresh. Не
  определять совместимость по подстроке CLR-имени и не делать reflection.
- [ ] Генерировать подписку только при наличии наблюдаемых OneWay/TwoWay
  bindings. `OneTime` получает значение при создании, не подписывается даже
  на notifying source. Для ненотифицирующего source сохранить явный
  `RefreshBindings`; невозможный auto-update диагностировать, не обещать его.
- [ ] Использовать одну source-managed notification boundary и существующие
  typed batches. `Dispose` симметрично отсоединяет ровно созданные подписки.

**Точки:** `IncrementalGenerator.cs`, `Emission/CSharpArtifactEmitter.cs`,
`Internal/Compilation/SemanticModel.cs`, существующий compiled binding runtime.
**Приёмка:** sealed plain/notifying, незапечатанный source, nullable segments,
OneTime-only и mixed modes компилируются; refresh/TwoWay работают по выбранной
семантике; после Dispose callbacks отсутствуют. Нового binding engine нет.

### UI-02 — размер содержимого ScrollViewer относительно viewport

**Факт:** `ScrollViewerArrangeMixin` выдаёт ребёнку `DesiredSize`, а не
viewport-sized slot; есть отдельная ветка grid ItemsControl с обнулением
offset. В sample сейчас `PageWidth = viewport.Width - 26`. Не объявлять
причиной «бесконечный measure» без отдельного воспроизведения constraints.

- [ ] Зафиксировать в headless regression available/desired/arranged sizes
  для `ScrollViewer → StackPanel → Grid(*)` и для item grid. Разделить
  ось прокрутки и ось растяжения: отсутствие scrollbar не равно запрету scroll.
- [ ] Дать explicit axis policy в typed scroll state и public properties
  (предлагаемая семантика: разрешена/запрещена прокрутка каждой оси).
  На запрещённой оси measure bounded viewport; arrange получает viewport
  slot с учётом margin/alignment. На разрешённой оси сохраняется content
  extent и clamp offset; padding/margin не вычитаются дважды.
- [ ] Связать policy с существующими measure/arrange queues и descriptor
  setters. Убрать special case item grid лишь после эквивалентной реализации
  через эту policy; не отключать всем item grids прокрутку ради примера.
- [ ] Axis policy/viewport меняют measure/arrange/hit/visual; только offset —
  arrange/hit/visual без reshaping неизменного текста. Пустой child допустим.
- [ ] Перевести галерею на vertical-scroll/horizontal-stretch и удалить
  `PageWidth` из `DemoModel`/`UiLibraryDemoContent` и DXAML binding.

**Точки:** `UiScrollViewer.cs`, `ScrollViewerState.cs`, `ScrollViewerMixin.cs`,
`UiItemsScrollDescriptors.cs`, `Internal/Stages/UiLayoutQueues.cs`.
**Приёмка:** ширина по viewport без host arithmetic; resize, margin, explicit
width, обе оси scroll, shrink extent, nested scroll и пустой content. Child
clipping, offset и hit geometry совпадают с JSON; no-change не remeasure.

### UI-03 — gap, перенос элементов и адаптивные колонки

Реальные случаи: `flex-wrap + gap` у кнопок/статусов и
`repeat(auto-fit, minmax(280px, 1fr))` у карточек. Не импортировать CSS parser.

- [ ] Добавить typed `Spacing` для StackPanel и `RowSpacing`/`ColumnSpacing`
  для Grid. Gap занимает место только между участвующими children/tracks,
  не по внешнему периметру; margins остаются независимыми. Defaults — 0.
- [ ] Добавить `UiWrapPanel` / `WrapPanelState` / measure+arrange mixins и
  descriptor для natural-size последовательности. Line ranges/used sizes
  хранятся в reusable layout buffers, а не в списках новых line objects.
  Layout order равен source order; oversized child не порождает пустую строку.
  При unbounded main axis — одна естественная строка, не цикл remeasure.
- [ ] Для равномерных responsive cards расширить существующий Grid одним
  auto-column mode с минимальной шириной колонки, а не писать sample layout.
  При bounded width `W`, minimum `m > 0`, gap `g >= 0`:
  `columns = max(1, floor((W + g) / (m + g)))`,
  `slotWidth = max(0, (W - (columns - 1) * g) / columns)`.
  Full-row child начинает новую строку; remaining width распределяется
  star tracks. При indefinite width использовать natural one-column measure.
- [ ] Auto-placement/full-row — compact attached metadata. Конфликт режима
  с явными Columns/позициями диагностируется; правила не зависят от
  `AutomationName` или имени карточки. Общий track resolver переиспользуется.
- [ ] Generated item realization использует тот же layout над realized nodes,
  без второго repeater. Перенос/resize не пересоздаёт item identities. Не
  расширять это до variable-height virtualization без реального workload.
- [ ] Удалить sample `SetSlots`/перестановку card rows/columns там, где их
  заменяет layout; декларативно оставить full-row у Toolbar/Typography.

**Точки:** `State/GridState.cs`, `Mixins/PanelLayoutMixin.cs`,
`Mixins/StackPanelMixin.cs`, `Descriptors/UiPanelGridDescriptors.cs`,
`UserApi/Controls`, registry и typed property metadata.
**Приёмка:** width около каждого breakpoint ±1; 0/1/many children; hidden,
margin, разные heights, empty/oversized child; no overlap/escape по JSON.
Gap count равен `max(0, n - 1)` для непустой последовательности. Dirty resize
меняет geometry, не keys/contents; no-change не создаёт layout allocations.

### UI-04 — меньше повторов: styles и typed content slots

**Факт:** styles/templates/TemplateBinding уже есть. Но `XamlStylePlan`
требует key, `UiTheme.FindStyle` ищет explicit style, а `ApplyTemplate`
применяет template только при отсутствии children. Поэтому нельзя просто
пообещать HeaderedContentControl поверх текущего empty-content ограничения.

- [ ] Добавить compile-time `BasedOn` reference: разрешить stable style IDs,
  проверить cycles, missing base и несовместимый target. Flatten typed setter
  ranges при компиляции: base сначала, derived overrides затем. Это одна
  Style/Trigger source layer, не новая precedence ступень и не inheritance
  control classes. DynamicResource slots остаются зависимостями, не literals.
- [ ] Для реально общих TextBlock/Border defaults разрешить implicit style
  по target type и resource scope. Построить compact type/scope→style table
  при construction/изменении scope; ближайший scope выигрывает, explicit
  StyleKey выбирает explicit style, объединение — только через BasedOn.
  Не искать стили строками/parent walk в layout или per-frame refresh.
- [ ] Ввести generated content projection для именованных Header/Content
  slots. Минимальный presenter — flat control/state+descriptor, не facade и
  не полноценная WPF template system. Generated attachment связывает
  logical content с visual slot того же owner/document; TemplateBinding
  читает уже имеющийся cached template-owner handle.
- [ ] Template application должно поддерживать owner с logical content;
  template nodes и projected content имеют разные правила владения.
  Замена template уничтожает его chrome, но не пользовательский content;
  повторное применение не клонирует content и не даёт второго visual parent.
- [ ] Сделать sample Card из одного такого template с Header/Content,
  используя generated custom-control registration. Отдельный публичный
  `UiCard`/`UiHeaderedContentControl` не нужен, если примитив покрывает задачу.
- [ ] Namescopes принадлежат template instances; source IDs и setters typed.
  Изменение resource/style будит только dependent slots; replacement
  template — affected tree/layout/input/visual, не весь документ.

**Точки:** `StyleApi.cs`, `Internal/Compilation/{SemanticModel,XamlCompiler}.cs`,
`CSharpArtifactEmitter.cs`, существующий template attachment в `UiElement`,
node-store visual relations и content layout descriptors.
**Приёмка:** BasedOn cycle diagnostic с range; resource replacement/clear;
explicit/implicit precedence; две Card instances с одинаковыми локальными
именами; template replace сохраняет content identity, bindings/focus и не
оставляет orphan nodes. В generated/runtime path нет string setter fallback.

### UI-05 — оформление существующих value controls

**Факт:** `UiSlider` уже имеет range/value/step/orientation и input mixins,
но `UiSliderGenerated.Descriptor` не имеет Visual capability. Picker,
ToggleButton, Overlay тоже существуют. Нужен skin/composition, не новые
slider/selection/input engines и не имитация интерактивности статичной картинкой.

- [ ] Собрать в generated DXAML shared skins: Slider track/fill/thumb,
  checkbox/switch через ToggleButton states, Picker closed face/overlay list,
  inspector/expander header/body. Icons/labels — обычные text children.
- [ ] Сначала использовать existing template-owner bindings, conditions и
  state setters. Если отсутствует один typed state/part geometry accessor,
  добавить его в существующий descriptor/mixin. Не помещать расчёт thumb
  в sample tick и не добавлять per-control event handlers/behavior objects.
- [ ] Slider нормализует range/value одним существующим validation path;
  части используют его effective value. Для `Minimum == Maximum` позиция
  определена; drag/keyboard/capture cancel остаются в существующем input.
- [ ] Picker overlay остаётся в одном node store, с существующим focus scope
  и capture policy. Visible/checked/selected flags и commands не создают
  дублирующую source model. Collapsed body исключён из layout/hit testing.
- [ ] Превратить текущие статичные preview parts в визуал реальных controls,
  подключив уже имеющийся input; не реализовывать файловый браузер, поиск,
  persist/undo приложения или новые editor commands в этой задаче.

**Точки:** `UiValueControlDescriptors.cs`, `ValueControlMixin.cs`,
`SliderState.cs`, `UiToggleButton`, `UiPicker`, `UiOverlay`, generated template
и condition code. Theme сначала остаётся source resource сэмпла, не новым NuGet.
**Приёмка:** visuals follow value/checked/selected/disabled; drag/keyboard
и open/close проходят одним Dispatch→Layout путём; no detached capture;
re-theme не создаёт новых input owners. Только данные/разметка повторяются,
не одна C# view class/подписка на каждый item.

### UI-06 — tracking и explicit font fallback

**Факт:** XAML cache передаёт `_singleFontFallback`; DeltaText уже принимает
`TextShapeRequest.FontFallback` chain. Но в `TextShapeRequest` нет tracking,
а `ShapedText` immutable с internal constructor: XAML не может безопасно
переписать glyph advances и выдать это за результат текущего контракта.

- [ ] Добавить пользовательский `CharacterSpacing` с явно указанной
  единицей — logical units между shaping-safe clusters, default 0; применять
  также к rich spans и editing path. Перевод CSS `.03em`/`.1em` — source
  conversion относительно FontSize, не новая CSS expression subsystem.
- [ ] С Matt согласовать минимальный provider-owned spacing/layout input и
  позиционированный output. До такой ревизии не исправлять spacing смещением
  каждого glyph в XAML, делением строки на chars или дополнительными draws.
  Уточнить clusters, ligatures, bidi и границы строк; после последнего cluster
  строки дополнительный tracking отсутствует.
- [ ] Typed setters обновляют text-layout key и measure/arrange/hit/visual;
  width/wrapping/caret/selection используют те же advances. Font/glyph image
  cache не сбрасывается из-за spacing; возможность reuse shaping определяет
  Text layout API, не предположение на стороне XAML.
- [ ] Для fallback расширить существующую font registration модель до одного
  typed family slot с ordered exact FontInstanceIds и version. Public
  library-frontdoor согласовать без второго resolver/service locator и без
  поломки текущего `IUiFontResolver`. Реализация fallback остаётся DeltaText.
- [ ] Разрешение FontKey/bytes/variations — cold, обновление chain — mutation,
  frame path использует cached immutable chain. Регистрация владеет входными
  данными до открытия fonts; cache освобождает только принадлежащие ему
  instances при replace/dispose. Не сканировать установленные system fonts.

**Точки:** `UserApi/TextApi.cs`, text properties/state/descriptors,
`Internal/Text/UiTextLayoutCache.cs`, generated literal/resource/binding plans.
**Приёмка:** Latin tracking, ligature, combining mark, RTL, rich spans,
fallback glyph из второго шрифта; measure/align/caret совпадают. Paint/position
не reshapes; chain/size/text изменения инвалидируют нужный cache. Сравнивать
line/advance alignment, не требовать прижатия actual ink к каждой границе.

### UI-07 — gradient paint целиком до renderer

**Факт:** `BrushApi.cs` уже содержит linear/radial resource types и
`UiKnownVisuals`. `UiVisualStage` для `HasCustomVisual` использует
`UiVisualPaint.Solid`, обходя border/radii/units extraction. Renderer registry
сейчас регистрирует visual programs и images; наличие Custom Resource GUID
само по себе не передаёт gradient stops программе.

- [ ] В XAML отделить resolved common paint от выбора kind. Для известных
  gradient brushes передать fill/tint, normalized radii, stroke и units
  через уже существующий `UiVisualPaint`; не изменять семантику произвольного
  custom visual молча и не добавлять его скрытую paint reinterpretation.
- [ ] С Rend определить один neutral resource payload/registration для
  linear stops/endpoints и version/lifetime. Сейчас эти resource declarations
  находятся в library API, а не frozen packet assembly: зафиксировать
  владельца перед переносом/публикацией. Не копировать `UiLinearGradient`
  в Render и не пропускать `object` resource graph в hot loop.
- [ ] Generated gradient declarations создают stable resource IDs, typed
  stop storage и direct setters. Stop arrays копируются/валидируются один
  раз; mutation version queues только dependents. Это resource update,
  не причина переписывать all nodes или reshaping text.
- [ ] Rend реализует resource binding/upload/cache, Shad — стандартный
  gradient shader и generated typed packing. Согласовать normalized brush
  coordinates, linear RGBA/interpolation, alpha и logical/device metrics.
  UI никогда не знает offsets/padding/std430. Нужны multistop linear gradients;
  radial API не переписывать и не считать renderer support автоматически.
- [ ] Заменить solid substitutes Primary/Active/Spectrum/акцентных полос и
  selected asset в sample; shader не должен быть уникальным для этой галереи.

**Приёмка:** producer data сохраняет все stops/radii/stroke/units/clip/order;
dynamic stop change не пересоздаёт identity. Renderer проверяет фактический
градиент на прозрачном/непрозрачном фоне и rounded clipping. Успешная
регистрация программы без binding stop resource не закрывает пункт.

### UI-08 — border sides, dash pattern и inset shadow

Это три отдельных paint возможности, не изменение общего box model.
Базовый `UiEffectSet` теперь является общим visual/text reference-каналом:
typed resource payload, `Units`, lowering convenience-свойств Border/Text и
передача в display list завершены в commit `9817509`. Это не закрывает
per-side widths, dash pattern или inset-shadow semantics.

- [ ] **Sides:** выбрать canonical four-side width value, переиспользуя
  существующий four-side value type/parser там, где семантика совпадает.
  Target authoring принимает uniform и четыре значения; порядок сторон
  документирован. Не менять float property type молча и не заводить два
  независимых source slots scalar/four-side. Breaking public migration
  требует отдельной ревизии с удалением старого пути.
- [ ] Сохранить текущую paint-only semantics: border рисуется внутрь bounds
  и сам не меняет DesiredSize/content inset. Для дополнительного inset есть
  Padding. Logical/Device относится к ширине; corner radii остаются logical.
  Producer проверяет finite/nonnegative и derived допустимость по bounds.
- [ ] Для простых прямых top/bottom линий достаточно существующих Border
  children — это уже доступная композиция. Не объявлять полную rounded
  per-side поддержку через четыре накладывающихся прямоугольника. С Rend/Shad
  согласовать нейтральные corner joins и encoding varying side widths.
- [ ] **Dash:** immutable normalized dash pattern resource + phase/units,
  deterministic traversal perimeter и corner continuity. Validate source
  once; отдельная семантика нулевого/пустого pattern. Не создавать UI child
  на каждый dash и не вычислять строковый pattern в shader/render loop.
- [ ] **Inset shadow:** ограниченный resource с color/offset/blur/spread и
  inner shape, не произвольная цепочка CSS filter effects. XAML задаёт смысл
  и порядок; Render выбирает raster/cache, Shader — coverage/blur algorithm.
  Нулевая стоимость дополнительных buffers для элементов без эффекта.
- [x] Минимальная neutral resource/paint ревизия для общего effect reference
  утверждена: `UiEffectSet` использует существующий `UiResourceId`, typed
  parameters имеют явные owner/consumer lifetime и `Units`; изменения
  зафиксированы в contract increment `0.0.15`. Не называть новые payload под
  `Custom` обходом contract review.
- [ ] Composite painter сохраняет fill → inner shadow → stroke → content,
  clipping и ordered item identities. Если возникают subdraws, переиспользовать
  существующий per-output identity механизм; никакого второго UI tree.

**Точки XAML:** common visual state/properties, `UiVisualStage`, compiler
typed literals/resources; shader/renderer implementation остаётся коллегам.
**Приёмка:** только top/bottom, zero sides, rounded uneven widths, DPI 1/2
Device hairline, translucent corners, phase changes и pattern replacement.
Inset не вылезает за inner shape. Не заменять stroke/AA semantics pixel hacks.

### UI-09 — render transforms без подмены layout

Нужен поворот 22×22 preview на 18° вокруг центра; механизм должен корректно
работать также для text/children/clip, а не только для одного shader quad.

- [ ] Спроектировать небольшой typed 2D affine value на DeltaMaths и
  normalized origin, identity по умолчанию. `RenderTransform` влияет на
  отрисовку/hit testing, не DesiredSize. Layout transform в этот slice не входит.
- [ ] Composite state хранит local value; cached world/inverse transform
  и conservative hit bounds обновляются при изменении ancestor geometry или
  transform в существующем traversal. Не добавлять transform tree, per-frame
  parent walks, callback chain или новую таблицу identity.
- [ ] Hit testing использует inverse transform и shape/clip в локальном
  пространстве; AABB — только broad phase. Singular matrix: документированное
  отсутствие hit, без NaN; captured pointer/focus сохраняют lifetime policy.
- [ ] Source transform → arrange/hit/visual dirtiness affected subtree,
  без measure/reshape неизменного текста. Cached parent stamp даёт ранний
  выход для unchanged branch. CornerRadius остаётся в local geometry.
- [ ] **Обязательный contract gate:** `UiVisualDraw.Bounds`,
  `UiTextDraw.BaselineOrigin` и axis-aligned `UiClipRegion.Bounds` не кодируют
  произвольный affine transform и transformed clip. Согласовать один neutral
  transform-reference механизм для обоих payload kinds и clips; `Order` и
  `Identities` не менять по смыслу. Не выдавать transformed AABB за реальную
  повернутую геометрию и не прятать text transform в paint Effect.
- [ ] После утверждения ABI Rend/Shad применяют тот же transform к geometry,
  glyphs и clipping. XAML не делает backend Y-flip или shader packing.

**Приёмка:** вложенные transforms, origin, text+border, partially clipped
child, hit/capture до/после rotate, resize и parent removal. JSON показывает
local layout и transformed bounds отдельно. Идентичная identity transform
не меняет результат/стоимость текущего straight-through path существенно.

### EXT-01 — наблюдавшиеся text defects и точный handoff

- [ ] Matt: минимальный repro с исходными Inter WOFF2, variations `wght=500`
  и `DeltaTextService.Shape`, фиксировать точную версию local package/fork.
  Наблюдавшийся `EndOfStreamException` в
  `GlyphVariationData.DecodePackedDeltas` — defect provider path, не повод
  добавлять font parser в XAML. Static TTF остаётся честным workaround до fix.
- [ ] Matt/Rend: для `play_arrow` U+E037 и `layers` U+E53B отдельно проверить
  source cmap/outline → shaped glyph IDs/advances → CPU glyph image → atlas
  upload/UV → canonical draw → pixel. Наличие contours не доказывает правильные
  rendered glyphs. Подключать Shad только при локализации проблемы в shader.
- [ ] XAML передаёт exact font instance/text/paint/clip/identity из минимальной
  fixture, не меняет glyph IDs, не подменяет иконки procedural масками.
  Закрыть workaround только после producer и consumer evidence.

### UI-10 — завершение sample migration и acceptance

- [ ] Сохранить reference mapping всех 16 исходных секций. Каждый снятый
  visual substitute заменить штатным generated DXAML, styles/templates и
  typed bindings; текущий незакоммиченный sample diff не откатывать.
- [ ] Удалить `PageWidth`, layout-by-AutomationName, ручные slot assignments
  и static slider parts ровно после замены соответствующим production path.
  Generated square background grid остаётся data-driven с шагом 32 logical;
  preview — 16. Ни сотен вручную выписанных children, ни sample-built UI tree.
- [ ] Program остаётся явным initialization/update/render loop. Он открывает
  ресурсы, передаёт viewport/input, вызывает Layout/BuildDisplayList и renderer;
  он не вычисляет card/slider/text geometry. UI Host не возвращать.
- [ ] Обновить USER_API/INTERNAL и sample VISUAL_GAPS только подтверждённым
  поведением. Готовые пункты убрать из TODO; источник результатов — tests,
  commit и artifacts, а не вечные checked roadmaps. Неподдерживаемое оставить
  с точным owner/contract blocker, не заявлять полную browser parity.

### Проверки и completion gate

Для implementation slices обязательны bounded Release/headless проверки из
[WORKFLOW](WORKFLOW.md), включая architecture gate; formatter/metrics из
[REVIEW_PLAYBOOK](../REVIEW_PLAYBOOK.md). Это требования к будущей реализации,
не результаты текущего documentation-only изменения.

- [ ] Compiler tests: deterministic generated C#, точные diagnostics/ranges,
  typed calls без reflection/Activator/string fallback, source scopes и
  precedence. Gate охватывает новые State/Mixins/Descriptors/Controls.
- [ ] Runtime tests: cold load и generated construction сходятся в тот же
  state/node store; exact invalidation, clear/resource replacement,
  stale handles, template lifetime, no-change subtree/cache reuse.
- [ ] Sample layout JSON на desktop 1280×1200, medium 900×1700, narrow
  720×2300, DPI 1/2 и около breakpoints. Проверять counts всех секций,
  отсутствие случайных overlap/escape, gap/scroll extent и square grid pitch;
  намеренные overlay/intersections классифицировать отдельно.
- [ ] После Render/Text изменений — bounded headless Vulkan readback
  актуального generated sample: gradients/alpha/strokes/clips/glyphs должны
  менять ожидаемые пиксели. SVG/JSON — geometry evidence, не доказательство GPU.
  Окна и full BenchmarkDotNet для этих проверок не нужны.
- [ ] Отдельно record unchanged, resize, style/paint mutation, text mutation,
  collection delta: touched-node/shape counts и allocations после warmup.
  Не обещать speedup без измерения; append/remove не делает full rebuild.
- [ ] По каждому slice: изменённые файлы, удалённые substitutes/legacy,
  оставшиеся blockers, formatter/metrics baseline delta, Release/harness
  результат, `git diff --check`, git status. Commit/publish/tag — только по
  отдельной команде пользователя, не следствие появления этого плана.

Dependency preparation — только
[`./eng/nuget-workspace.sh dev`](../docs/NUGET_WORKFLOW.md) из Furnace root:
он пакует текущие first-party checkouts и сохраняет floating package edges.
Не следовать историческому описанию source-only пакетов в начале INTERNAL,
не закреплять временные версии и не подставлять ProjectReference на соседний
репозиторий. Не запускать конкурентный restore над теми же assets. Полный dev
цикл выполняется как dependency/integration gate, не повторяется ради каждой
правки Markdown. Локальные focused checks после подготовки зависимостей:

```sh
cd /Users/rum/GitProjects/TheFurnace/DeltaXAML
dotnet build tests/DeltaXAML.Tests/DeltaXAML.Tests.csproj -c Release --no-restore \
  --disable-build-servers -m:1 /p:UseSharedCompilation=false -v:minimal
dotnet run --project tests/DeltaXAML.Tests/DeltaXAML.Tests.csproj \
  -c Release --no-build --no-restore
../eng/format.sh "$PWD"
FORMAT_CHECK=1 ../eng/format.sh "$PWD"
../eng/code-metrics.sh "$PWD" -v:q
git diff --check
```

Если окружению нужен SixLabors license/fork, использовать настройки root
NuGet workflow, не записывать секреты в репозиторий. NU1900 классифицируется
отдельно как infrastructure advisory; diagnostics/architecture gates не
отключать ради зелёного вывода.
