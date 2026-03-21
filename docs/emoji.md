# Búsqueda de emojis

`EmojiSearch` es un `ISearchSource` instant que se activa cuando la query empieza por `:`. Devuelve un único `EmojiGridResultViewModel` que agrupa todos los emojis candidatos en una fila horizontal navegable con ←/→. Al activarlo copia el emoji seleccionado al portapapeles y lo pega automáticamente en la app de destino.

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
  ├─ ¿embedded emoji-cache.json?  →  ParseCompactCache  →  return entries
  ├─ ¿existe emoji-cache.json en disco (cacheDir)?  →  ParseCompactCache  →  return entries
  └─ else
       ├─ lee EmbeddedResource (emoji-data.json compilado en el ensamblado)
       ├─ ParseRawJson  →  WriteCompactCache(disco)  →  return entries
       └─ si falla el parseo  →  return []  (sin crash; emojis simplemente no aparecen)
```

El `emoji-cache.json` embebido se genera en desarrollo y se incluye en el repo; ver `docs/release-workflow.md` para el ciclo completo.

### EmojiEntry y pre-tokenización

`EmojiEntry` almacena los cinco campos del JSON (`Char`, `Name`, `Keywords`, `Category`, `SortOrder`) más una propiedad `NameTokens` inicializada en construcción con un simple space-split del `Name`. Esto equivale al resultado de `NameMatcher.SplitTokens` para nombres de emoji (siempre minúsculas separadas por espacios), evitando re-tokenizar en cada búsqueda.

### Parseo del JSON raw

Por cada entrada del JSON:
- Se descarta si `obsoleted_by` no es null (emojis reemplazados por versiones gendered o más recientes).
- El campo `unified` (hexadecimales separados por `-`) se convierte a carácter usando `char.ConvertFromUtf32` por cada segmento.
- El `name` (en mayúsculas en el JSON) se normaliza a minúsculas.
- Los `short_names` (identifiers tipo `:thumbsup:`) y los `texts` (ASCII como `:D`) se combinan en un único array `Keywords`.

## ViewModels del grid

### EmojiCellViewModel

Representa una celda individual: `Char` (el carácter emoji), `Name` y `IsSelected` (con INPC manual). El template AXAML aplica la clase CSS `emoji-selected` al `Border` cuando `IsSelected` es true.

### EmojiGridResultViewModel

Hereda de `ResultItemViewModel`. Contiene la lista de `EmojiCellViewModel` (propiedad `Cells`) y gestiona el índice seleccionado (`SelectedEmojiIndex`). Al cambiar el índice, actualiza `IsSelected` en las celdas afectadas y notifica `SelectedEmoji` (la celda activa), cuyo `Name` se muestra debajo del grid. Expone `SelectNext()` y `SelectPrevious()` con wrap circular.

## EmojiSearch

Implementa `ISearchSource` con `IsInstant = true` — sus resultados van por la pipeline instant de `GlobalSearch`, no por la deferred.

### Ciclo de vida

- `Start()` lanza `EmojiDataLoader.LoadAsync` en un `Task.Run` y encadena un `ContinueWith` que puebla `_entries` cuando termina.
- `WhenReady()` expone esa Task. `GlobalSearch` la espera antes de hacer queries.
- `Stop()` es no-op.
- `_entries` se marca `volatile` — se escribe una sola vez desde el `ContinueWith` y luego solo se lee.

### Resultados

`SearchAsync` devuelve siempre una lista de un único elemento: un `EmojiGridResultViewModel` construido por `MakeGrid`.

- Al escribir solo `:` (sin término): grid con los 20 emojis de menor `sort_order` según el orden Unicode CLDR.
- Al escribir `:smile` (o cualquier término): grid con todos los emojis que coincidan, ordenados por score descendente, hasta el límite de la query. `EmojiSearch.MatchScore` prioriza nombre exacto > nombre con `NameMatcher.Score` (usando `NameTokens` pre-computados) > keyword con `NameMatcher.Score`. El rango de scores garantiza que cualquier match por nombre supera a cualquier match por keyword.

Si la caché no está lista o la carga falló, se devuelve lista vacía (sin error visible al usuario).

### Activación y navegación

`MakeGrid` construye el `EmojiGridResultViewModel` con captura circular de `grid`:

- `OnActivate` copia `grid.Cells[grid.SelectedEmojiIndex].Char` al portapapeles vía `ClipboardService`.
- `OnLeft` llama a `grid.SelectPrevious()`; `OnRight` llama a `grid.SelectNext()`.

`PasteAfterActivate = true` indica a la UI que pegue automáticamente tras ocultar la ventana. Ver `docs/calculator.md` para el funcionamiento de `ClipboardService`.

`OnLeft`/`OnRight` son propiedades de `ResultItemViewModel`. La ventana principal intercepta ←/→ en la fase túnel (`AddHandler(KeyDownEvent, ..., RoutingStrategies.Tunnel)`) y, si el item seleccionado tiene esas acciones, las invoca y marca el evento como handled antes de que el `TextBox` pueda mover el cursor de texto. Ver `MainWindow.axaml.cs`.

## Tests

`EmojiDataLoader` es `internal`. La anotación `InternalsVisibleTo("Yottacast.Core.Tests")` en el `.csproj` de Core permite testarla directamente desde `EmojiDataLoaderTests`.

Los tests de `EmojiSearchTests` prepueblan una caché compacta en un directorio temporal y llaman `Start()` + `WhenReady()` antes de hacer queries, sin necesidad de red.

## Gotchas

- **Selectores de variación FE0F** — algunos `unified` terminan en `-FE0F` (emoji presentation selector). Deben incluirse en la conversión a carácter; sin ellos el emoji se renderiza como símbolo de texto en lugar de emoji.
- **Pares surrogados** — la mayoría de emojis están en el plano suplementario (codepoints > U+FFFF) y ocupan dos `char` en .NET. `char.ConvertFromUtf32` los genera correctamente.
- **Secuencias ZWJ** — emojis compuestos (familias, profesiones) usan Zero-Width Joiner (`U+200D`) entre codepoints. `char.ConvertFromUtf32(0x200D)` lo maneja sin problema al estar en el BMP.
