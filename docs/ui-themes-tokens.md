# UI: Referencia de tokens de tema

Este documento es la referencia canonica de los tokens de un fichero de tema: cada propiedad del JSON y el recurso Avalonia (`Theme.*`) al que se mapea. Para el comportamiento del sistema de temas (arranque, hot-swap, fallback, descubrimiento, deteccion dark/light, temas de usuario) ver `docs/ui-themes.md`.

---

## Formato de un fichero de tema

Cada tema es un fichero `.json`. La estructura agrupa propiedades por componente de UI: cada seccion contiene todos los atributos visuales (color, size, fontFamily, cornerRadius, opacity) de sus elementos.

| Seccion | Contenido |
|---|---|
| `id` | Identificador unico del tema (obligatorio; usado para deduplicacion y resolucion de ruta) |
| `name` | Nombre para mostrar en el picker |
| `variant` | `"light"` o `"dark"` (controla el `ThemeVariant` de Avalonia) |
| `window` | Fondo, ancho, cornerRadius y fontFamily de la ventana |
| `search` | Fondo, marco del input, texto, placeholder, caret, seleccion, hints (error e info) |
| `groups` | Mode chips (All / Files / Clipboard): altura, texto, padding, margin, borde y cornerRadius por estado (normal / seleccionado) |
| `divider` / `spinner` | Color del separador y del spinner de carga |
| `results` | Fondo del area, altura maxima, barra de seleccion lateral, titulo, subtitulo, categoria, icono, shortcut, seleccion, highlight, tags |
| `calculator` | Estilo unico de todos los resultados de calculo (calculadora, conversor, fechas, algebra): expresion, resultado, subtitulo, separador, celda |
| `emoji` | Celda, caracter, keywords, cabecera de seccion, favorito, contador de uso |
| `noResults` | Titulo y subtitulo cuando no hay resultados |
| `footer` | Fondo, borde superior y texto del pie |
| `optionsMenu` | Fondo, borde, cabecera y opciones del menu contextual de resultados |
| `updateBanner` | Fondo y texto del banner de actualizacion |
| `preview` | Anchura del panel lateral de preview/editor |
| `editor` | Cabecera, cuerpo y pie del panel de edicion de ficheros |

Si el valor de `variant` no es `"light"`, se asume `"dark"`.

**Secciones heterogeneas entre temas:** no todos los ficheros contienen todas las secciones. `editor` solo existe en `dark-default.json`; el resto de temas no lo definen y `ThemeService` cae a los valores hardcodeados de `ApplyBuiltinDefault()` para los tokens `Theme.Editor.*`. Cualquier seccion ausente se omite sin error y conserva el valor previo (ver "Comportamiento silencioso ante valores invalidos" en `docs/ui-themes.md`).

No existe ninguna seccion `escBadge` ni tokens `Theme.Esc.*`: el indicador de actividad durante la busqueda diferida es un spinner controlado por la seccion `spinner`, no un badge tematizable.

> **Verificar en:**
> - `ThemeService.Apply()` -- lectura del JSON y asignacion de tokens.
> - Cualquier fichero en `Yottacast/Themes/*.json` como referencia de estructura.

---

## Valores `fontFamily`: fuentes de sistema y embebidas

Cualquier token `*.fontFamily` acepta dos formas, intercambiables y combinables:

- **Cadena de familias de sistema**, separadas por coma, en orden de preferencia (ej. `"SF Pro Text, Lucida Grande, Segoe UI, Inter"`). Avalonia usa la primera que exista en el SO; el resto son fallback. Asi es como los temas resuelven el texto general (SF Pro en macOS, Inter como ultimo recurso fuera de macOS).
- **URI de fuente embebida**: `avares://Yottacast/Assets/Fonts#<Familia>`. El nombre tras `#` debe coincidir con el **nombre interno** de la familia (nombre del fichero es irrelevante). Tras el `#` se pueden anadir fallbacks de sistema por coma: `avares://Yottacast/Assets/Fonts#Geist Mono, SF Mono, Menlo, Consolas, monospace`.

Las fuentes embebidas viven en `Yottacast/Assets/Fonts/` y se compilan dentro del ensamblado por el glob `<AvaloniaResource Include="Assets\**" />` del `.csproj` (no requiere registro extra). Estan embebidas:

| Fichero | Familia (`#`) | Uso |
|---|---|---|
| `GeistMono-Variable.ttf` | `Geist Mono` | Fuente de los resultados de calculo (`calculator.fontFamily` en los 5 temas y el fallback `Theme.Calc.FontFamily`). Reservada tambien para futuros sources de tipo dev (UUID, hash, etc.). |
| `JetBrainsMono-Variable.ttf` | `JetBrains Mono` | Embebida y disponible; no asignada a ningun token por defecto. |

Inter sigue disponible como ultimo fallback de las cadenas de sistema via el paquete `Avalonia.Fonts.Inter` (registrado con `.WithInterFont()` en `Program.cs`); no se referencia por URI.

Ambas monos son fuentes variables (un solo `.ttf` cubre todos los pesos); el `fontWeight` del tema selecciona el peso sobre el eje `wght`. Las licencias OFL se envian junto a los `.ttf` (`Geist-OFL.txt`, `JetBrainsMono-OFL.txt`).

> **Verificar en:**
> - `ThemeService.SetFontFamily()` -- construccion del `FontFamily` desde la cadena del JSON.
> - `Yottacast/Assets/Fonts/` -- ficheros embebidos y licencias.
> - `Yottacast/Yottacast.csproj` -- glob `AvaloniaResource Include="Assets\**"`.
> - `Program.cs` -- `.WithInterFont()`.

---

## Tokens de tema disponibles

### Window

| JSON path | Recurso Avalonia |
|---|---|
| `window.background` | `Theme.Window.Background` |
| `window.width` | `Theme.Window.Width` |
| `window.cornerRadius` | `Theme.Window.CornerRadius` |
| `window.fontFamily` | `Theme.Window.FontFamily` |

### Search

| JSON path | Recurso Avalonia |
|---|---|
| `search.background` | `Theme.Search.Background` |
| `search.input.cornerRadius` | `Theme.Search.Input.CornerRadius` |
| `search.input.margin` | `Theme.Search.Input.Margin` |
| `search.input.padding` | `Theme.Search.Input.Padding` |
| `search.input.border.color` | `Theme.Search.Input.BorderColor` |
| `search.input.border.thickness` | `Theme.Search.Input.BorderThickness` |
| `search.text.color` | `Theme.Search.Color` |
| `search.text.size` | `Theme.Search.Size` |
| `search.text.fontFamily` | `Theme.Search.FontFamily` |
| `search.placeholder.color` | `Theme.Search.Placeholder` |
| `search.caret.color` | `Theme.Search.Caret` |
| `search.selection.color` | `Theme.Search.Selection` |

El grupo `search.input.*` define el marco visual del campo de texto: esquinas, margen exterior, padding interior y un borde opcional (color y grosor). El borde se declara como objeto anidado `border` con `color` y `thickness`.

#### Hints (error e info)

El bloque `search.hint` contiene dos sub-bloques, `error` e `info`, con la misma estructura. Cada uno define el estilo completo del hint que aparece bajo el campo de busqueda (el de error cuando una expresion no es valida, el de info para sugerencias). Los tokens se aplican via `SetHintStyle()` con el prefijo `Theme.Search.Hint.Error.*` o `Theme.Search.Hint.Info.*`.

| JSON path (por hint) | Recurso Avalonia (con `<Kind>` = `Error` / `Info`) |
|---|---|
| `search.hint.<kind>.color` | `Theme.Search.Hint.<Kind>.Color` |
| `search.hint.<kind>.size` | `Theme.Search.Hint.<Kind>.Size` |
| `search.hint.<kind>.fontFamily` | `Theme.Search.Hint.<Kind>.FontFamily` |
| `search.hint.<kind>.padding` | `Theme.Search.Hint.<Kind>.Padding` |
| `search.hint.<kind>.background` | `Theme.Search.Hint.<Kind>.Background` |
| `search.hint.<kind>.cornerRadius` | `Theme.Search.Hint.<Kind>.CornerRadius` |
| `search.hint.<kind>.margin` | `Theme.Search.Hint.<Kind>.Margin` |
| `search.hint.<kind>.horizontalAlignment` | `Theme.Search.Hint.<Kind>.HorizontalAlignment` |
| `search.hint.<kind>.textAlignment` | `Theme.Search.Hint.<Kind>.TextAlignment` |

`horizontalAlignment` acepta `left` / `center` / `right` (cualquier otro valor cae a `stretch`). `textAlignment` acepta `center` / `right` (cualquier otro valor cae a `left`).

### Groups

Estilo de los mode chips bajo la barra de busqueda (las pills **All / Files / Clipboard**, clase `mode-chip` en `MainWindow.axaml`). `text.size` y `text.fontFamily` son compartidos por ambos estados; el color del texto, el borde y el cornerRadius se definen por separado en `normal` y `selected`. La diferenciacion visual entre estados se controla solo con color de texto y borde (sin opacity).

| JSON path | Recurso Avalonia |
|---|---|
| `groups.height` | `Theme.Groups.Height` |
| `groups.padding` | `Theme.Groups.Padding` |
| `groups.margin` | `Theme.Groups.Margin` |
| `groups.text.size` | `Theme.Groups.Size` |
| `groups.text.fontFamily` | `Theme.Groups.FontFamily` |
| `groups.normal.text.color` | `Theme.Groups.Normal.Color` |
| `groups.normal.cornerRadius` | `Theme.Groups.Normal.CornerRadius` |
| `groups.normal.border.color` | `Theme.Groups.Normal.BorderColor` |
| `groups.normal.border.thickness` | `Theme.Groups.Normal.BorderThickness` |
| `groups.selected.text.color` | `Theme.Groups.Selected.Color` |
| `groups.selected.cornerRadius` | `Theme.Groups.Selected.CornerRadius` |
| `groups.selected.border.color` | `Theme.Groups.Selected.BorderColor` |
| `groups.selected.border.thickness` | `Theme.Groups.Selected.BorderThickness` |

`margin` se aplica a cada chip individual (el espaciado entre chips). El chip seleccionado anade ademas `FontWeight="Medium"` (no tematizable, fijo en el AXAML).

### Divider / Spinner

| JSON path | Recurso Avalonia |
|---|---|
| `divider.color` | `Theme.Divider.Color` |
| `spinner.color` | `Theme.Spinner.Color` |

### Results

| JSON path | Recurso Avalonia |
|---|---|
| `results.background` | `Theme.Results.Background` |
| `results.maxHeight` | `Theme.Results.MaxHeight` |
| `results.padding` | `Theme.Results.Padding` |
| `results.selectionBar.color` | `Theme.Results.SelectionBar.Color` |
| `results.selectionBar.width` | `Theme.Results.SelectionBar.Thickness` |
| `results.cornerRadius` | `Theme.Results.CornerRadius` |
| `results.title.color` | `Theme.Results.Title.Color` |
| `results.title.size` | `Theme.Results.Title.Size` |
| `results.subtitle.color` | `Theme.Results.Subtitle.Color` |
| `results.subtitle.size` | `Theme.Results.Subtitle.Size` |
| `results.category.color` | `Theme.Results.Category.Color` |
| `results.category.size` | `Theme.Results.Category.Size` |
| `results.icon.cornerRadius` | `Theme.Results.Icon.CornerRadius` |
| `results.shortcut.color` | `Theme.Results.Shortcut.Color` |
| `results.shortcut.background` | `Theme.Results.Shortcut.Background` |
| `results.shortcut.size` | `Theme.Results.Shortcut.Size` |
| `results.shortcut.cornerRadius` | `Theme.Results.Shortcut.CornerRadius` |
| `results.selection.background` | `Theme.Results.Selection.Background` |
| `results.selection.color` | `Theme.Results.Selection.Color` |
| `results.selection.subtitleColor` | `Theme.Results.Selection.SubtitleColor` |
| `results.matchHighlight.color` | `Theme.Results.MatchHighlight.Color` |
| `results.matchHighlight.background` | `Theme.Results.MatchHighlight.Background` |

`results.maxHeight` fija la altura maxima del area de resultados (en pixeles) y ademas alimenta el calculo del numero de filas visibles del grid de emojis (ver "Emoji" mas abajo).

#### Detalle: Match Highlight

El token `matchHighlight` controla como se resaltan los caracteres de titulo y subtitulo que coinciden con la query del usuario. Tiene exactamente dos campos, ambos colores:

| Campo | Descripcion |
|---|---|
| `color` | Color del texto de los caracteres coincidentes (foreground del run resaltado) |
| `background` | Color de relleno de fondo de los caracteres coincidentes |

Ambos colores se aplican siempre a los caracteres que coinciden: el texto toma `color` y el fondo toma `background`. Para conseguir un chip semi-transparente, usar un color de fondo con canal alfa en formato `#AARRGGBB` (alfa primero; ej. `#662C5AF0` es azul al ~40%). El texto no-coincidente conserva su estilo normal.

No existe ningun campo `style`: no hay variantes `foreground` / `background` / `underline` ni subrayado. Tampoco existe `backgroundOpacity` (la transparencia se expresa en el propio `background` via canal alfa).

**Nota:** El highlight es parte del contrato de tema, permitiendo que autores de temas personalizados creen estilos visuales coherentes con su diseno.

#### Tags (pills inline en el titulo)

Cada tag tiene tres variantes de proposito (`running`, `info`, `error`), todas con la misma estructura. Para cada una, `background` es el fondo en estado normal y `backgroundSelected` el fondo cuando la fila esta seleccionada.

| JSON path | Recurso Avalonia |
|---|---|
| `results.tags.cornerRadius` | `Theme.Results.Tag.CornerRadius` |
| `results.tags.running.color` | `Theme.Results.Tag.Running.Color` |
| `results.tags.running.background` | `Theme.Results.Tag.Running.Background` |
| `results.tags.running.backgroundSelected` | `Theme.Results.Tag.Running.Background.Selected` |
| `results.tags.running.borderColor` | `Theme.Results.Tag.Running.BorderColor` |
| `results.tags.info.color` | `Theme.Results.Tag.Info.Color` |
| `results.tags.info.background` | `Theme.Results.Tag.Info.Background` |
| `results.tags.info.backgroundSelected` | `Theme.Results.Tag.Info.Background.Selected` |
| `results.tags.info.borderColor` | `Theme.Results.Tag.Info.BorderColor` |
| `results.tags.error.color` | `Theme.Results.Tag.Error.Color` |
| `results.tags.error.background` | `Theme.Results.Tag.Error.Background` |
| `results.tags.error.backgroundSelected` | `Theme.Results.Tag.Error.Background.Selected` |
| `results.tags.error.borderColor` | `Theme.Results.Tag.Error.BorderColor` |

El estilo filled (fondo tintado, borde transparente) u outline (fondo transparente, borde con color) se controla combinando `background` y `borderColor`: filled pone `background` con alpha y `borderColor: "Transparent"`; outline hace lo contrario. `dark-default` usa filled; `dark-macos` usa outline.

### Calculator

La seccion `calculator` es el estilo unico compartido por todos los resultados de tipo calculo: la calculadora simple, la conversion de unidades/divisas, el algebra simbolica y la busqueda de fechas. No existe una seccion `converter` separada; el conversor, las fechas y el algebra leen estos mismos tokens `Theme.Calc.*`.

Mapeo por componente: las celdas de entrada del conversor (`From` / `NormFrom`) usan `Theme.Calc.Expression.*`; el resultado del conversor (`To`) y las celdas de fecha/algebra usan `Theme.Calc.Result.*`; las flechas `→` entre celdas usan `Theme.Calc.Separator.Color` (el mismo token que el `=` de la calculadora); los subtitulos contextuales usan `Theme.Calc.Subtitle.*` y las celdas `Theme.Calc.Cell.CornerRadius`. Como `expression` y `result` suelen definirse identicos en los temas incluidos, la distincion solo se nota en temas que los diferencien.

| JSON path | Recurso Avalonia |
|---|---|
| `calculator.fontFamily` | `Theme.Calc.FontFamily` |
| `calculator.expression.color` | `Theme.Calc.Expression.Color` |
| `calculator.expression.size` | `Theme.Calc.Expression.Size` |
| `calculator.expression.fontWeight` | `Theme.Calc.Expression.FontWeight` |
| `calculator.result.color` | `Theme.Calc.Result.Color` |
| `calculator.result.size` | `Theme.Calc.Result.Size` |
| `calculator.result.fontWeight` | `Theme.Calc.Result.FontWeight` |
| `calculator.subtitle.color` | `Theme.Calc.Subtitle.Color` |
| `calculator.subtitle.size` | `Theme.Calc.Subtitle.Size` |
| `calculator.subtitle.opacity` | `Theme.Calc.Subtitle.Opacity` |
| `calculator.separator.color` | `Theme.Calc.Separator.Color` |
| `calculator.cell.cornerRadius` | `Theme.Calc.Cell.CornerRadius` |

### Emoji

| JSON path | Recurso Avalonia |
|---|---|
| `emoji.cell.size` | `Theme.Emoji.Cell.Size` |
| `emoji.cell.margin` | `Theme.Emoji.Cell.Margin` |
| `emoji.cell.cornerRadius` | `Theme.Emoji.Cell.CornerRadius` |
| `emoji.char.size` | `Theme.Emoji.Char.Size` |
| `emoji.char.fontFamily` | `Theme.Emoji.Char.FontFamily` |
| `emoji.keywords.color` | `Theme.Emoji.Keywords.Color` |
| `emoji.keywords.size` | `Theme.Emoji.Keywords.Size` |
| `emoji.keywords.opacity` | `Theme.Emoji.Keywords.Opacity` |
| `emoji.sectionHeader.color` | `Theme.Emoji.SectionHeader.Color` |
| `emoji.sectionHeader.size` | `Theme.Emoji.SectionHeader.Size` |
| `emoji.sectionHeader.opacity` | `Theme.Emoji.SectionHeader.Opacity` |
| `emoji.favorite.color` | `Theme.Emoji.Favorite.Color` |
| `emoji.favorite.size` | `Theme.Emoji.Favorite.Size` |
| `emoji.favorite.opacity` | `Theme.Emoji.Favorite.Opacity` |
| `emoji.usageCount.color` | `Theme.Emoji.UsageCount.Color` |
| `emoji.usageCount.size` | `Theme.Emoji.UsageCount.Size` |
| `emoji.usageCount.opacity` | `Theme.Emoji.UsageCount.Opacity` |

**Las columnas del grid de emojis NO se leen del JSON.** El recurso `Theme.Emoji.Columns` (y el numero de filas visibles) se calcula en runtime en `CalculateEmojiLayout()` a partir del ancho de ventana (`window.width`), la altura maxima de resultados (`results.maxHeight`), el padding de resultados (`results.padding`), el tamano de celda (`emoji.cell.size`), el margen de celda (`emoji.cell.margin`) y el tamano de la cabecera de seccion (`emoji.sectionHeader.size`). Si un fichero incluye un campo `emoji.columns`, ese valor es config muerta: nunca se lee.

### No Results

| JSON path | Recurso Avalonia |
|---|---|
| `noResults.title.color` | `Theme.NoResults.Title.Color` |
| `noResults.title.size` | `Theme.NoResults.Title.Size` |
| `noResults.subtitle.color` | `Theme.NoResults.Subtitle.Color` |
| `noResults.subtitle.size` | `Theme.NoResults.Subtitle.Size` |

### Footer

| JSON path | Recurso Avalonia |
|---|---|
| `footer.background` | `Theme.Footer.Background` |
| `footer.border` | `Theme.Footer.Border` |
| `footer.text.color` | `Theme.Footer.Color` |
| `footer.text.size` | `Theme.Footer.Size` |

### Options Menu

| JSON path | Recurso Avalonia |
|---|---|
| `optionsMenu.background` | `Theme.Menu.Background` |
| `optionsMenu.border.color` | `Theme.Menu.Border.Color` |
| `optionsMenu.border.radius` | `Theme.Menu.Border.Radius` |
| `optionsMenu.header.color` | `Theme.Menu.Header.Color` |
| `optionsMenu.header.size` | `Theme.Menu.Header.Size` |
| `optionsMenu.header.background` | `Theme.Menu.Header.Background` |
| `optionsMenu.header.padding` | `Theme.Menu.Header.Padding` |
| `optionsMenu.header.margin` | `Theme.Menu.Header.Margin` |
| `optionsMenu.option.color` | `Theme.Menu.Option.Color` |
| `optionsMenu.option.size` | `Theme.Menu.Option.Size` |
| `optionsMenu.option.padding` | `Theme.Menu.Option.Padding` |
| `optionsMenu.option.cornerRadius` | `Theme.Menu.Option.CornerRadius` |
| `optionsMenu.optionSelected.background` | `Theme.Menu.OptionSelected.Background` |
| `optionsMenu.optionSelected.color` | `Theme.Menu.OptionSelected.Color` |

### Editor

Presente solo en `dark-default.json`; el resto de temas dejan que estos tokens tomen su valor de `ApplyBuiltinDefault()`.

| JSON path | Recurso Avalonia |
|---|---|
| `editor.header.background` | `Theme.Editor.Header.Background` |
| `editor.header.color` | `Theme.Editor.Header.Color` |
| `editor.header.size` | `Theme.Editor.Header.Size` |
| `editor.header.padding` | `Theme.Editor.Header.Padding` |
| `editor.header.margin` | `Theme.Editor.Header.Margin` |
| `editor.header.fontFamily` | `Theme.Editor.Header.FontFamily` |
| `editor.body.background` | `Theme.Editor.Body.Background` |
| `editor.footer.background` | `Theme.Editor.Footer.Background` |
| `editor.footer.border` | `Theme.Editor.Footer.Border` |
| `editor.footer.color` | `Theme.Editor.Footer.Color` |
| `editor.footer.size` | `Theme.Editor.Footer.Size` |
| `editor.footer.padding` | `Theme.Editor.Footer.Padding` |

### Update Banner

| JSON path | Recurso Avalonia |
|---|---|
| `updateBanner.background` | `Theme.Update.Background` |
| `updateBanner.text.color` | `Theme.Update.Color` |
| `updateBanner.text.size` | `Theme.Update.Size` |

### Preview Panel

| JSON path | Recurso Avalonia |
|---|---|
| `preview.width` | `Theme.Preview.Width` |

Anchura en pixeles del panel lateral de preview/editor (columna derecha). Si el campo esta ausente en el JSON, se usa el valor de `AppDefaults.EditorWidth`.

> **Verificar en:**
> - `ThemeService.Apply()` -- mapeo token JSON a recurso Avalonia.
> - `ThemeService.SetHintStyle()` -- sub-tokens de los hints de error e info.
> - `ThemeService.CalculateEmojiLayout()` -- calculo de `Theme.Emoji.Columns` y filas visibles.
> - `ThemeService.ApplyBuiltinDefault()` -- valores hardcodeados de cada token.

---

## Temas incluidos

| Fichero | Variante |
|---|---|
| `dark-default.json` | dark |
| `dark-raycast.json` | dark |
| `dark-macos.json` | dark |
| `light-blue.json` | light |
| `light-gray.json` | light |
