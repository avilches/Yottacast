pod# Búsqueda de emojis

`EmojiSearch` es un `ISearchSource` instant que se activa cuando la query empieza por `:`. Al activar un resultado copia el carácter emoji al portapapeles y lo pega automáticamente en la app de destino.

## Datos de origen

La fuente de datos es [iamcal/emoji-data](https://github.com/iamcal/emoji-data), un JSON de ~1.25 MB con más de 1600 entradas. El fichero `Resources/emoji-data.json` se descarga en tiempo de compilación si no existe (target `DownloadEmojiData` en el `.csproj`) y se embute en el ensamblado como `EmbeddedResource`. Para actualizar a una versión más reciente, basta con eliminar `Resources/emoji-data.json` y recompilar. No hay descarga en runtime. De los muchos campos del JSON original, solo se conservan los necesarios para mostrar y buscar emojis usando la representación nativa del SO (no sprites PNG). Ver `EmojiDataLoader.ParseRawJson` para los campos exactos que se extraen.

## EmojiDataLoader

Clase estática interna que gestiona el ciclo parseo → caché. `EmojiSearch` la llama desde `Start()`.

### Caché en disco

La caché se guarda en `AppData/Yottacast/emoji-cache.json` (misma carpeta que `settings.json`). El formato es un array de arrays de 5 elementos `[char, name, [keywords], category, sortOrder]`, mucho más compacto que el JSON original (~100-150 KB). Se escribe de forma atómica (fichero temporal + `File.Move`) para evitar corromperse ante un cierre inesperado.

El nombre del fichero está definido como constante en `EmojiDataLoader`. No hay TTL: la caché es válida indefinidamente. Para forzar una regeneración, basta con eliminar el fichero de caché.

### Flujo de carga

```
LoadAsync(cacheDir)
  ├─ ¿existe emoji-cache.json?  →  ParseCompactCache  →  return entries
  └─ else
       ├─ lee EmbeddedResource (emoji-data.json compilado en el ensamblado)
       ├─ ParseRawJson  →  WriteCompactCache  →  return entries
       └─ si falla el parseo  →  return []  (sin crash; emojis simplemente no aparecen)
```

### Parseo del JSON raw

Por cada entrada del JSON:
- Se descarta si `obsoleted_by` no es null (emojis reemplazados por versiones gendered o más recientes).
- El campo `unified` (hexadecimales separados por `-`) se convierte a carácter usando `char.ConvertFromUtf32` por cada segmento.
- El `name` (en mayúsculas en el JSON) se normaliza a minúsculas.
- Los `short_names` (identifiers tipo `:thumbsup:`) y los `texts` (ASCII como `:D`) se combinan en un único array `Keywords`.

## EmojiSearch

Implementa `ISearchSource` con `IsInstant = true` — sus resultados van por la pipeline instant de `GlobalSearch`, no por la deferred.

### Ciclo de vida

- `Start()` lanza `EmojiDataLoader.LoadAsync` en un `Task.Run` y encadena un `ContinueWith` que puebla `_entries` cuando termina.
- `WhenReady()` expone esa Task. `GlobalSearch` la espera antes de hacer queries.
- `Stop()` es no-op.
- `_entries` se marca `volatile` — se escribe una sola vez desde el `ContinueWith` y luego solo se lee.

### Resultados por defecto

Al escribir solo `:` (sin término de búsqueda) se devuelven los 6 emojis con menor `sort_order` según el orden Unicode CLDR. Si la caché no está lista aún o la descarga falló, se devuelve una lista vacía (sin error visible al usuario).

### Filtrado y scoring

Al escribir `:smile` (o cualquier término tras el `:`), se busca en `Name` y en `Keywords`. Las prioridades de scoring (de mayor a menor): nombre exacto, nombre con prefijo, nombre con substring, keyword exacta, keyword con prefijo, keyword con substring. Ver `EmojiSearch.MatchScore` para los valores numéricos.

### Activación

`OnActivate` copia el carácter al portapapeles vía `ClipboardService` y `PasteAfterActivate = true` indica a la UI que pegue automáticamente tras ocultar la ventana. Ver `docs/calculator.md` para el funcionamiento de `ClipboardService`.

## Tests

`EmojiDataLoader` es `internal`. La anotación `InternalsVisibleTo("Yottacast.Core.Tests")` en el `.csproj` de Core permite testarla directamente desde `EmojiDataLoaderTests`.

Los tests de `EmojiSearchTests` prepueblan una caché compacta en un directorio temporal y llaman `Start()` + `WhenReady()` antes de hacer queries, sin necesidad de red.

## Gotchas

- **Selectores de variación FE0F** — algunos `unified` terminan en `-FE0F` (emoji presentation selector). Deben incluirse en la conversión a carácter; sin ellos el emoji se renderiza como símbolo de texto en lugar de emoji.
- **Pares surrogados** — la mayoría de emojis están en el plano suplementario (codepoints > U+FFFF) y ocupan dos `char` en .NET. `char.ConvertFromUtf32` los genera correctamente.
- **Secuencias ZWJ** — emojis compuestos (familias, profesiones) usan Zero-Width Joiner (`U+200D`) entre codepoints. `char.ConvertFromUtf32(0x200D)` lo maneja sin problema al estar en el BMP.
