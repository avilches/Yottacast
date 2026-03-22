# Búsqueda de emojis

`EmojiSearch` es un `IInstantSearchSource` que se activa cuando la query empieza por `:`. Devuelve un único `EmojiGridResultViewModel` que agrupa todos los emojis candidatos en un grid navegable con ←/→/↑/↓ (ver `EmojiGridResultViewModel.Columns` para el número de columnas). Al activarlo copia el emoji seleccionado al portapapeles y lo pega automáticamente en la app de destino.

## Datos de origen

La fuente de datos es [iamcal/emoji-data](https://github.com/iamcal/emoji-data), un JSON de ~1.25 MB con más de 1600 entradas. El fichero `Resources/emoji-data.json` se descarga en tiempo de compilación si no existe (target `DownloadEmojiData` en el `.csproj`) y se embute en el ensamblado como `EmbeddedResource`. Para actualizar a una versión más reciente, basta con eliminar `Resources/emoji-data.json` y recompilar. No hay descarga en runtime. De los muchos campos del JSON original, solo se conservan los necesarios para mostrar y buscar emojis usando la representación nativa del SO (no sprites PNG). Ver `EmojiDataLoader.ParseRawJson` para los campos exactos que se extraen.

## EmojiDataLoader

Clase instanciable (registrada en DI) que gestiona el ciclo parseo → caché. `EmojiSearch` la llama desde `Start()`.

### Caché en disco

La caché se guarda en `AppData/Yottacast/emoji-cache.json` (misma carpeta que `settings.json`). El formato es un array de arrays de 5 elementos `[char, name, [keywords], category, sortOrder]`, mucho más compacto que el JSON original (~100-150 KB). Se escribe de forma atómica (fichero temporal + `File.Move`) para evitar corromperse ante un cierre inesperado.

El nombre del fichero está definido como constante en `EmojiDataLoader`. No hay TTL: la caché es válida indefinidamente. Para forzar una regeneración, basta con eliminar el fichero de caché.

### Flujo de carga

```
LoadAsync(cacheDir)
  ├─ ¿existe emoji-cache.json en disco (cacheDir)?  →  ParseCompactCache  →  return entries
  ├─ ¿embedded emoji-cache.json?  →  ParseCompactCache  →  return entries
  └─ else
       ├─ lee EmbeddedResource (emoji-data.json compilado en el ensamblado)
       ├─ ParseRawJson  →  WriteCompactCache(disco)  →  return entries
       └─ si falla el parseo  →  return []  (sin crash; emojis simplemente no aparecen)
```

El `emoji-cache.json` embebido se genera en desarrollo y se incluye en el repo; ver `docs/release-workflow.md` para el ciclo completo.

### EmojiEntry y pre-tokenización

`EmojiEntry` almacena los cinco campos del JSON (`Char`, `Name`, `Keywords`, `Category`, `SortOrder`) más una propiedad `NameTokens` inicializada en construcción con un simple space-split del `Name`. Los nombres de emoji son siempre minúsculas separadas por espacios, por lo que space-split es suficiente para obtener los mismos tokens que `NameMatcher.SplitTokens`. Esto evita re-tokenizar en cada búsqueda.

### Parseo del JSON raw

Por cada entrada del JSON:
- Se descarta si `obsoleted_by` no es null (emojis reemplazados por versiones gendered o más recientes).
- El campo `unified` (hexadecimales separados por `-`) se convierte a carácter usando `char.ConvertFromUtf32` por cada segmento.
- El `name` (en mayúsculas en el JSON) se normaliza a minúsculas.
- Los `short_names` (identifiers tipo `:thumbsup:`) y los `texts` (ASCII como `:D`) se combinan en un único array `Keywords`.

## ViewModels del grid

### EmojiCellViewModel

Representa una celda individual: `Char` (el carácter emoji), `Name`, `Category`, `Keywords` e `IsSelected` (con INPC manual). Expone además `KeywordsText`, una propiedad calculada que une `Keywords` con `", "` para su uso directo en bindings. El template AXAML aplica la clase CSS `emoji-selected` al `Border` cuando `IsSelected` es true.

### EmojiGridResultViewModel

Hereda de `ResultItemViewModel`. Contiene la lista de `EmojiCellViewModel` (propiedad `Cells`) y gestiona el índice seleccionado (`SelectedEmojiIndex`). Al cambiar el índice, actualiza `IsSelected` en las celdas afectadas y notifica `SelectedEmoji` (la celda activa). Expone `SelectNext()` y `SelectPrevious()` con wrap circular.

El `Icon` y el `Title` del `EmojiGridResultViewModel` se inicializan con el `Char` y el `Name` de la primera celda del grid; `Category` se fija siempre a `"Emoji"` y `Score` a `3.5`.

### Template AXAML del grid

El grid usa un `UniformGrid` con 8 columnas (valor constante `EmojiGridResultViewModel.Columns = 8`). Cada celda es un `Border` de 40×40 px con `CornerRadius=8`. Debajo del grid, un `StackPanel` centrado muestra tres líneas: `SelectedEmoji.Name` (en `Theme.ItemTitle`), `SelectedEmoji.Category` (en `Theme.ItemCategory`) y `SelectedEmoji.KeywordsText` (en `Theme.ItemSubtitle`, con `TextWrapping="Wrap"` y `MaxWidth=400`).

## EmojiSearch

Implementa `IInstantSearchSource` — sus resultados van por la pipeline instant de `GlobalSearch`, no por la deferred.

### Ciclo de vida

- `Start()` lanza `EmojiDataLoader.LoadAsync` en un `Task.Run` y encadena un `ContinueWith` que puebla `_entries` cuando termina. Si la Task falla, `_entries` permanece vacío (sin propagación de excepciones).
- `WhenReady()` expone esa Task. Si `Start()` no se llamó, devuelve `Task.CompletedTask`. `GlobalSearch` la espera antes de hacer queries.
- `Stop()` es no-op.
- `_entries` se marca `volatile` — se escribe una sola vez desde el `ContinueWith` y luego solo se lee.

### Resultados

`Search` devuelve siempre una lista de un único elemento: un `EmojiGridResultViewModel` construido por `MakeGrid`.

- Al escribir solo `:` (sin término): grid con los 20 emojis de menor `sort_order` positivo (se excluyen los que tienen `sort_order == 0`) según el orden Unicode CLDR.
- Al escribir `:smile` (o cualquier término): grid con todos los emojis que coincidan, ordenados por score descendente, hasta el límite de la query. El término se extrae con `query[1..].Trim().ToLowerInvariant()`, por lo que espacios tras los dos puntos (`: smile`) se ignoran. `EmojiSearch.MatchScore` prioriza nombre exacto > nombre con `NameMatcher.Score` (usando `NameTokens` pre-computados) > keyword con `NameMatcher.Score`. El rango de scores garantiza que cualquier match por nombre supera a cualquier match por keyword. Para el matching de keywords se usa la sobrecarga `NameMatcher.Score(string, string)`, que aplica la misma cadena de tiers (prefix, camelHump, initials, multi-word abbreviation, internal substring ≥3 chars) que el matching de nombre.

Si la caché no está lista o la carga falló, se devuelve lista vacía (sin error visible al usuario).

### Activación y navegación

`MakeGrid` construye el `EmojiGridResultViewModel` con captura circular de `grid`:

- `OnActivate` copia `grid.Cells[grid.SelectedEmojiIndex].Char` al portapapeles vía `ClipboardService`.
- `OnLeft` llama a `grid.SelectPrevious()`; `OnRight` llama a `grid.SelectNext()`.
- `OnUp` llama a `grid.SelectUp()`; `OnDown` llama a `grid.SelectDown()`. Ambos devuelven `bool`: `true` si se movió dentro del grid (consumiendo la tecla), `false` si no hay fila superior/inferior disponible (delegando la navegación de lista a la ventana).

`PasteAfterActivate = true` indica a la UI que pegue automáticamente tras ocultar la ventana. El flujo completo en `MainWindow.axaml.cs` al pulsar Enter sobre el grid es: invocar `OnActivate` (copia al portapapeles) → limpiar `SearchText` → `Hide()` → `AppHandler.Instance.OnHide()` (restaura foco a la app anterior) → `AppHandler.Instance.SimulatePasteAsync()` (envía Cmd+V / Ctrl+V con delay). Ver `docs/calculator.md` para el funcionamiento de `ClipboardService`.

`OnLeft`/`OnRight`/`OnUp`/`OnDown` son propiedades de `ResultItemViewModel`. La ventana principal intercepta ←/→/↑/↓ en la fase túnel (`AddHandler(KeyDownEvent, ..., RoutingStrategies.Tunnel)`) y, si el item seleccionado tiene esas acciones, las invoca. Para ←/→ siempre marca el evento como handled; para ↑/↓, el evento queda handled solo si el callback devuelve `true`. Ver `MainWindow.axaml.cs`.

## Tests

`EmojiDataLoader` es `public`, pero su método `LoadAsync` es `internal`. La anotación `InternalsVisibleTo("Yottacast.Core.Tests")` en el `.csproj` de Core permite llamar a `LoadAsync` (y otros métodos internos como `ParseRawJson`, `ParseCompactCache`) directamente desde `EmojiDataLoaderTests`.

Los tests de `EmojiSearchTests` prepueblan una caché compacta en un directorio temporal y llaman `Start()` + `WhenReady()` antes de hacer queries, sin necesidad de red. Hay además una clase `EmojiSearchRealDataTests` que usa un `IClassFixture<RealEmojiDataFixture>` para cargar el dataset completo embebido una sola vez y compartirlo entre todos los tests de integración, evitando el coste de parsear el JSON raw en cada test.

## Gotchas

- **Selectores de variación FE0F** — algunos `unified` terminan en `-FE0F` (emoji presentation selector). Deben incluirse en la conversión a carácter; sin ellos el emoji se renderiza como símbolo de texto en lugar de emoji.
- **Pares surrogados** — la mayoría de emojis están en el plano suplementario (codepoints > U+FFFF) y ocupan dos `char` en .NET. `char.ConvertFromUtf32` los genera correctamente.
- **Secuencias ZWJ** — emojis compuestos (familias, profesiones) usan Zero-Width Joiner (`U+200D`) entre codepoints. `char.ConvertFromUtf32(0x200D)` lo maneja sin problema al estar en el BMP.
