# Modelos de resultado

Los resultados de busqueda se representan mediante una jerarquia de ViewModels. Cada tipo de resultado tiene su propio ViewModel con propiedades especificas para su presentacion y comportamiento.

---

## 1. Jerarquia de clases

```
BaseResultItemViewModel (abstracta)
  +-- ResultItemViewModel            (apps, ficheros, web search, system settings)
  |     +-- EmojiGridResultViewModel (grid de emojis con viewport y secciones)
  +-- CalculatorResultItemViewModel  (resultado de calculo simple)
  +-- ConversionResultItemViewModel  (conversion de unidades con 3 celdas)
  +-- DictionaryResultViewModel      (definicion de diccionario con acepciones)
```

---

## 2. BaseResultItemViewModel

Clase base abstracta. Contiene las propiedades comunes a todos los tipos de resultado.

### Propiedades de datos

| Propiedad | Tipo | Descripcion |
|---|---|---|
| `Score` | `double` | Puntuacion para ordenacion (mayor = mas relevante). Ver `docs/search-scoring.md` |
| `Title` | `string` | Texto principal del resultado |

### Highlighting y debug de scoring

Los resultados pueden mostrar caracteres resaltados cuando coinciden con la query, y pueden displayar informacion de scoring cuando el usuario presiona Alt.

| Propiedad | Tipo | Descripcion |
|---|---|---|
| `TitleRanges` | `IReadOnlyList<(int Start, int Length)>?` | Rangos de caracteres en `Title` que coinciden con la query; `null` cuando no es necesario resaltar |
| `SubtitleRanges` | `IReadOnlyList<(int Start, int Length)>?` | Idem para `Subtitle` |
| `ScoreReason` | `string?` | Explicacion legible del score asignado por la fuente (ej. "CamelHump inicio (×4)"). Establecido por cada fuente de busqueda en tiempo de creacion |
| `FrequencyBonus` | `double` | Bonus de frecuencia/recencia aportado por `LaunchHistory`. Establecido por `MainWindowViewModel.RefreshResults()` |
| `ScoreDisplayText` | `string` | Texto formateado para mostrar (ej. "2.40 +0.24"). Establecido por `RefreshResults()` |
| `ScoreTooltipText` | `string?` | Texto multi-linea del tooltip del score. Establecido por `RefreshResults()` |

**Contrato:** Las fuentes de busqueda establecen `TitleRanges`, `SubtitleRanges` y `ScoreReason` al crear el item. Estos valores no cambian durante la vida del resultado. `FrequencyBonus`, `ScoreDisplayText` y `ScoreTooltipText` son establecidos por `RefreshResults()` despues de que todos los resultados se han recolectado, permitiendo combinar el score base de cada fuente con el bonus de uso.

### Lista de acciones

| Propiedad | Tipo | Descripcion |
|---|---|---|
| `Actions` | `IReadOnlyList<ResultAction>` | Todas las acciones disponibles. El footer, el overlay (Tab) y los hotkeys se derivan de esta lista |

Cada `ResultAction` tiene:

| Campo | Tipo | Descripcion |
|---|---|---|
| `Label` | `string` | Texto mostrado en overlay y footer |
| `Hotkey` | `ActionHotkey?` | Atajo de teclado. Null = solo accesible via overlay |
| `ShowInFooter` | `bool` | Muestra hint en la barra inferior (solo si Hotkey != null) |
| `ShowInMenu` | `bool` | Incluye en el overlay de opciones (Tab) |
| `ClosesMenu` | `bool` | Cierra el overlay al ejecutar |
| `ClosesWindow` | `bool` | Oculta Yottacast al ejecutar |
| `PasteAfterClose` | `bool` | Simula Cmd+V tras cerrar (solo si ClosesWindow = true) |
| `RequiresRefresh` | `bool` | Llama a RefreshSearch() tras Execute(). Usado por EmojiSearch favorito |
| `HintProvider` | `Func<string?>?` | Mensaje en SearchHint tras ejecutar. Solo visible si no cierra la ventana |
| `Execute` | `Action` | Callback de la accion |

`ActionHotkey` usa `ActionModifiers.Meta` como modificador agnostico de plataforma (resuelve a Cmd en macOS y Ctrl en Windows).

### Navegacion interna

Callbacks opcionales que permiten al resultado capturar las teclas de flecha antes de que lleguen al TextBox o a la navegacion de lista.

| Callback | Tipo | Retorno |
|---|---|---|
| `OnLeft` | `Func<bool>?` | `true` = tecla consumida (TextBox no mueve cursor). `false` = pasar al siguiente handler |
| `OnRight` | `Func<bool>?` | Idem |
| `OnUp` | `Func<bool>?` | `true` = consumida. `false` = la lista navega al item anterior |
| `OnDown` | `Func<bool>?` | `true` = consumida. `false` = la lista navega al item siguiente |

### Flags de comportamiento

| Flag | Tipo | Default | Descripcion |
|---|---|---|---|
| `BypassLimit` | `bool` | `false` | Si `true`, el item no se descarta por `SearchSourceLimit`. Usado por WebSearch y Dictionary |

> **Verificar en:** `Yottacast.Core/ViewModels/BaseResultItemViewModel.cs`, `Yottacast.Core/ViewModels/ResultAction.cs`, `Yottacast.Core/ViewModels/ActionHotkey.cs`, `Yottacast.Core/ViewModels/MainWindowViewModel.cs` (`RefreshResults`)

---

## 3. ResultItemViewModel

Extiende `BaseResultItemViewModel`. Es el tipo mas comun, usado por apps, ficheros, web search y system settings.

| Propiedad | Tipo | Descripcion |
|---|---|---|
| `Icon` | `string` | Clave de icono (emoji o nombre de recurso embebido) |
| `IconBytes` | `byte[]?` | Bytes PNG del icono. Settable para carga asincrona |
| `BadgeIconBytes` | `byte[]?` | Icono superpuesto (app predeterminada del tipo de fichero). Solo para resultados de ficheros |
| `Subtitle` | `string` | Texto secundario (ruta del fichero, URL de busqueda, etc.) |
| `Category` | `string` | Etiqueta de categoria ("App", "File", "Web"). En modo debug (Alt pulsado), se reemplaza por el score numerico |
| `Shortcut` | `string` | Atajo de teclado asociado (solo para emojis: muestra Copy y Favorite shortcuts) |
| `RunningTag` | `string?` | Cuando no es null, muestra una pill verde con este texto después del título. Asignado por `ApplicationSearch` cuando la app está en la lista de procesos activos. |
| `InfoTag` | `string?` | Cuando no es null, muestra una pill azul con este texto después del título. Asignado por `ClipboardSearch` con el valor `"from clipboard"`. |

### Carga asincrona de iconos

`IconBytes` y `BadgeIconBytes` son propiedades `set`-able. Las fuentes los establecen tras la carga asincrona. En la capa de UI (Avalonia), `PathToAppIconConverter` convierte `byte[]` a `Bitmap` con un `ConditionalWeakTable` para evitar memory leaks.

> **Verificar en:** `Yottacast.Core/ViewModels/ResultItemViewModel.cs`, `Yottacast/Converters/PathToAppIconConverter.cs`

---

## 4. CalculatorResultItemViewModel

Resultado de una evaluacion matematica simple (ej. `2+3 = 5`).

| Propiedad | Tipo | Descripcion |
|---|---|---|
| `Icon` | `string` | Icono de calculadora |
| `TitleLong` | `string?` | Resultado con precision completa (visible en hover si difiere de `Title`) |
| `Subtitle` | `string` | Contexto (unidad, expresion evaluada) |
| `Category` | `string` | Siempre `"Calculator"` |

La accion por defecto copia el resultado al portapapeles via `ClipboardService`.

Auto-seleccion: si existe un resultado de calculadora y el usuario no ha navegado con flechas, se selecciona automaticamente.

> **Verificar en:** `Yottacast.Core/ViewModels/CalculatorResultItemViewModel.cs`, `Yottacast.Core/Search/Calculator/CalculatorSearch.cs`

---

## 5. ConversionResultItemViewModel

Resultado de conversion de unidades (ej. `10 km to miles`). Presenta un layout de hasta 3 celdas navegables horizontalmente.

### Celdas

```
[OrigFrom]  →  [NormFrom]  →  [To]
  "10 km"      "10000 m"     "6.21 mi"
```

| Celda | Descripcion | Visible siempre |
|---|---|---|
| `OrigFrom` | Valor original tal como lo escribio el usuario | No (solo via Copy, no navegable) |
| `NormFrom` | Valor normalizado por math.js (ej. 0.001 V → 1 mV) | Solo si `FromWasNormalized = true` |
| `To` | Resultado de la conversion | Si (celda seleccionada por defecto) |

### Propiedades por celda

Cada celda tiene una forma corta y una forma larga:

| Propiedad | Ejemplo corto | Ejemplo largo |
|---|---|---|
| `FromShort` / `FromLong` | `"0.001 V"` | `"0.001 volts"` |
| `NormFromShort` / `NormFromLong` | `"1 mV"` | `"1 millivolt"` |
| `ToShort` / `ToLong` | `"6.21 mi"` | `"6.21 miles"` |

### Navegacion de celdas

| Propiedad / Metodo | Descripcion |
|---|---|
| `SelectedCell` | `enum ConversionCell { To, NormFrom, OrigFrom }`. Default: `To` |
| `MoveCellLeft()` | `To → NormFrom`. Retorna `true` si se consumio. Solo funciona si `FromWasNormalized` |
| `MoveCellRight()` | `NormFrom → To`. Retorna `true` si se consumio |
| `IsOrigFromHighlighted` | Derivado de `SelectedCell` |
| `IsNormFromHighlighted` | Derivado de `SelectedCell` AND `FromWasNormalized` |
| `IsToHighlighted` | Derivado de `SelectedCell` |

Los metodos `MoveCellLeft/Right` se conectan a `OnLeft/OnRight` en `BaseResultItemViewModel`, permitiendo que las flechas izquierda/derecha naveguen entre celdas en lugar de mover el cursor del TextBox.

### Flag adicional

`RatesAreStale: bool` — `true` cuando las tasas de cambio nunca se descargaron o estan obsoletas. Permite a la UI mostrar un indicador visual.

Auto-seleccion: igual que `CalculatorResultItemViewModel`, se auto-selecciona si el usuario no ha navegado.

> **Verificar en:** `Yottacast.Core/ViewModels/ConversionResultItemViewModel.cs`, `Yottacast.Core/Search/Calculator/CalculatorSearch.cs`

---

## 6. EmojiGridResultViewModel

Extiende `ResultItemViewModel`. Presenta un grid navegable con viewport y secciones.

### Estructura

| Propiedad | Tipo | Descripcion |
|---|---|---|
| `Cells` | `IReadOnlyList<EmojiCellViewModel>` | Lista plana de todas las celdas (pinned + default) |
| `HasPinnedSection` | `bool` | Si hay favoritos o most-used |
| `PinnedSectionHeader` | `string` | Header de la seccion pinned (ej. "Favorites & recently used") |
| `SelectedEmojiIndex` | `int` | Indice de la celda seleccionada en `Cells` |
| `SelectedEmoji` | `EmojiCellViewModel?` | Celda actualmente seleccionada |
| `Columns` | `int` (const) | `AppDefaults.EmojiColumns` (10) |

### Secciones

Los emojis se agrupan en secciones por categoria Unicode. La seccion pinned (favoritos + most-used) siempre se muestra completa al inicio. Las secciones Default se scrollean independientemente via `_viewportStartCell`.

`VisibleSections` retorna la lista de secciones visibles, cada una con padding de celdas `Placeholder` para completar filas parciales (garantiza que cada seccion empieza en columna 0).

### Viewport y scrolling

El viewport muestra `AppDefaults.EmojiViewportRows` filas (8 por defecto). Las filas pinned se restan del total, dejando las restantes para la seccion Default.

`EnsureVisible(cell)` ajusta `_viewportStartCell` cuando la celda seleccionada sale del viewport:
- **Scroll UP**: alinea al inicio de la fila de seccion que contiene la celda
- **Scroll DOWN**: estima un inicio que coloque la celda cerca del fondo, alineado a seccion

El alineamiento es **section-row-aligned**, no flat-row-aligned. Esto significa que `(viewportStart - sectionStart) % Columns == 0` siempre se cumple. Ver `docs/emoji-grid-gotchas.md` para detalles.

### Navegacion del grid

| Metodo | Comportamiento |
|---|---|
| `SelectNext()` | Circular: avanza 1 celda, wraps al inicio |
| `SelectPrevious()` | Circular: retrocede 1 celda, wraps al final |
| `SelectDown()` | Baja una fila. Cruza secciones si es la ultima fila de una seccion. Retorna `false` si ya esta en la ultima fila de la ultima seccion (delega a navegacion de lista) |
| `SelectUp()` | Sube una fila. Cruza secciones si es la primera fila. Retorna `false` si ya esta en la primera fila de la primera seccion |

La navegacion vertical es consciente de las secciones: al cruzar de una seccion a otra, intenta mantener la misma columna.

### EmojiCellViewModel

Cada celda del grid tiene:

| Propiedad | Tipo | Descripcion |
|---|---|---|
| `Char` | `string` | Caracter emoji (ej. "😀") |
| `Name` | `string` | Nombre del emoji |
| `Category` | `string` | Categoria Unicode |
| `Keywords` | `string[]` | Palabras clave para busqueda |
| `Section` | `EmojiSection` | `Favorite`, `MostUsed` o `Default` |
| `IsFavorite` | `bool` | Observable (INotifyPropertyChanged) |
| `IsSelected` | `bool` | Observable |
| `ShowUsage` | `bool` | Muestra contador de uso (modo debug) |
| `IsPlaceholder` | `bool` | Celda invisible de padding (opacity=0, no isVisible=false por gotcha de Avalonia) |

`EmojiCellViewModel.Placeholder` es un sentinel estatico para las celdas de relleno.

> **Verificar en:** `Yottacast.Core/ViewModels/EmojiGridResultViewModel.cs`, `Yottacast.Core/ViewModels/EmojiCellViewModel.cs`, `Yottacast.Core/Search/Emoji/EmojiSearch.cs`

---

## 7. DictionaryResultViewModel

Resultado de una busqueda de diccionario. Muestra la palabra, idioma y lista de definiciones.

| Propiedad | Tipo | Descripcion |
|---|---|---|
| `IconBytes` | `byte[]?` | Icono de bandera o idioma |
| `Word` | `string` | Palabra buscada |
| `Language` | `string?` | Nombre del idioma. `null` cuando solo hay un idioma configurado |
| `Definitions` | `IReadOnlyList<DictionaryDefinitionEntry>` | Lista de definiciones (max `AppDefaults.DictionaryMaxDefinitionsPerItem` = 5) |

### DictionaryDefinitionEntry

| Campo | Tipo | Descripcion |
|---|---|---|
| `PartOfSpeech` | `string` | Categoria gramatical (noun, verb, adjective...) |
| `Definition` | `string` | Texto de la definicion |
| `Example` | `string?` | Ejemplo de uso (puede incluir HTML limpiado) |
| `ExampleTranslation` | `string?` | Traduccion del ejemplo (para idiomas no-ingleses) |

La accion por defecto abre la pagina de Wiktionary en el navegador configurado.

> **Verificar en:** `Yottacast.Core/ViewModels/DictionaryResultViewModel.cs`, `Yottacast.Core/Search/Dictionary/DictionarySource.cs`

---

## 8. Flujo de datos: fuente → ViewModel → UI

```
SearchSource.Search(query)
  → crea XxxResultItemViewModel con propiedades + callbacks
  → GlobalSearch.SearchInstant/SearchDeferredAsync merge por Score
  → MainWindowViewModel.RefreshResults()
  → ObservableCollection<BaseResultItemViewModel> Results
  → MainWindow.axaml DataTemplates selecciona la vista segun el tipo de VM
```

### Resolucion de DataTemplate

El `ViewLocator` no interviene aqui. La `MainWindow.axaml` define `DataTemplate` explicitos para cada tipo de ViewModel:
- `ResultItemViewModel` → template estandar con icono + titulo + subtitulo + categoria/shortcut
- `CalculatorResultItemViewModel` → template con titulo (resultado) + subtitulo + badge
- `ConversionResultItemViewModel` → template con 3 celdas seleccionables
- `EmojiGridResultViewModel` → template con grid (ItemsRepeater + secciones)
- `DictionaryResultViewModel` → template con palabra + pills + definiciones

### Invariantes

- Todos los ViewModels son inmutables excepto las propiedades observables (`IconBytes`, `BadgeIconBytes`, `SelectedCell`, `SelectedEmojiIndex`, `IsFavorite`, `IsSelected`).
- `Actions` se establece en el constructor por la fuente y no cambia durante la vida del resultado.
- `Score` se establece una vez al crear el item. No se modifica durante la vida del resultado.
- La UI nunca crea ViewModels directamente: siempre los recibe de las fuentes via `GlobalSearch`.

> **Verificar en:** `Yottacast/Views/MainWindow.axaml` (DataTemplates), `Yottacast/ViewModels/MainWindowViewModel.cs` (`RefreshResults`), `Yottacast.Core/Search/GlobalSearch.cs` (`SearchInstant`, `SearchDeferredAsync`)
