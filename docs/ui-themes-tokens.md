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
| `divider` / `spinner` | Color del separador y del spinner de carga |
| `results` | Fondo del area, altura maxima, barra de seleccion lateral, titulo, subtitulo, categoria, icono, shortcut, seleccion, highlight, tags |
| `calculator` | Expresion, resultado, subtitulo, separador, celda |
| `converter` | Valor, subtitulo, flecha, celda |
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
| `results.matchHighlight.style` | `Theme.Results.MatchHighlight.Style` |
| `results.matchHighlight.color` | `Theme.Results.MatchHighlight.Color` |
| `results.matchHighlight.backgroundOpacity` | `Theme.Results.MatchHighlight.BackgroundOpacity` |

`results.maxHeight` fija la altura maxima del area de resultados (en pixeles) y ademas alimenta el calculo del numero de filas visibles del grid de emojis (ver "Emoji" mas abajo).

#### Detalle: Match Highlight

El token `matchHighlight` controla como se resaltan los caracteres de titulo y subtitulo que coinciden con la query del usuario. Los autores de temas pueden elegir entre tres estilos visuales:

| Estilo | Descripcion |
|---|---|
| `foreground` | Los caracteres coincidentes adoptan un color distinto y peso medio. El texto no-coincidente mantiene su estilo. Utiles para queries con pocos caracteres donde el cambio de color es sutil |
| `background` | Los caracteres coincidentes reciben un relleno semi-transparente de fondo usando el color del tema y la opacidad especificada. El color del texto permanece igual. Proporciona un contraste visual mas fuerte |
| `underline` | Los caracteres coincidentes se muestran en negrita con un subrayado en el color accent del tema. Util para temas donde el cambio de color podria resultar en contraste insuficiente |

El campo `color` se interpreta segun el `style`:
- En `foreground`: es el color del texto de los caracteres coincidentes
- En `background`: es el color del relleno de fondo (aplicado con la opacidad especificada)
- En `underline`: se ignora (siempre usa el accent color del tema)

El campo `backgroundOpacity` (0.0–1.0) solo se usa en estilo `background` y controla la transparencia del relleno. Valores tipicos: 0.15 para un highlight sutil, 0.4 para un highlight fuerte.

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

### Converter

| JSON path | Recurso Avalonia |
|---|---|
| `converter.fontFamily` | `Theme.Conv.FontFamily` |
| `converter.value.color` | `Theme.Conv.Value.Color` |
| `converter.value.size` | `Theme.Conv.Value.Size` |
| `converter.subtitle.color` | `Theme.Conv.Subtitle.Color` |
| `converter.subtitle.size` | `Theme.Conv.Subtitle.Size` |
| `converter.subtitle.opacity` | `Theme.Conv.Subtitle.Opacity` |
| `converter.arrow.color` | `Theme.Conv.Arrow.Color` |
| `converter.cell.cornerRadius` | `Theme.Conv.Cell.CornerRadius` |

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
