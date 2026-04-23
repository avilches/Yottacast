# Busqueda de emojis

## Proposito

Yottacast permite buscar e insertar emojis en cualquier aplicacion. El usuario escribe `:` seguido de un termino de busqueda en el launcher y obtiene un grid visual con los emojis candidatos. Al confirmar la seleccion, el emoji se copia al portapapeles y se pega automaticamente en la aplicacion de destino.

---

## Activacion y query

| Query         | Comportamiento                                                                        |
|---------------|---------------------------------------------------------------------------------------|
| `:`           | Muestra los 20 emojis con menor `sort_order` positivo (orden Unicode CLDR). Se excluyen los que tienen `sort_order == 0`. |
| `:smile`      | Filtra todos los emojis cuyo nombre o keywords coincidan con "smile", ordenados por relevancia descendente, limitados por el parametro `limit` de la pipeline. |
| `: smile`     | Equivalente a `:smile` -- los espacios tras los dos puntos se ignoran.                |
| `smile`       | Sin `:` inicial, la busqueda de emojis no se activa. El usuario nunca ve emojis si no escribe `:`. |

**Invariante:** la busqueda de emojis solo se activa cuando la query empieza por `:`. Cualquier otro prefijo no produce resultados de emojis.

---

## Presentacion del grid

Los resultados se muestran como un unico item de tipo grid en la lista de resultados, en lugar de items individuales. El grid contiene:

- Una cuadricula de celdas de 40x40 px con 8 columnas (constante `AppDefaults.EmojiColumns`).
- Debajo del grid, informacion del emoji seleccionado: nombre, categoria y keywords.

El primer emoji del grid aparece seleccionado inicialmente. El icono y titulo del resultado en la lista se toman del primer emoji del grid. La categoria del resultado siempre es `"Emoji"` y el score es `3.5`.

**Invariante:** siempre se devuelve exactamente 0 o 1 resultado (nunca multiples items en la lista). Si no hay emojis que coincidan, no se muestra ningun resultado.

> **Verificar en:** `EmojiSearch.MakeGrid()` -- construccion del `EmojiGridResultViewModel`; `EmojiGridResultView.axaml` -- template AXAML con `UniformGrid` y panel de informacion.

---

## Navegacion con teclado

| Tecla     | Comportamiento                                                                                          |
|-----------|---------------------------------------------------------------------------------------------------------|
| Izquierda | Mueve la seleccion a la celda anterior (con wrap circular al final del grid). Siempre consume el evento. |
| Derecha   | Mueve la seleccion a la celda siguiente (con wrap circular al inicio del grid). Siempre consume el evento. |
| Arriba    | Mueve la seleccion una fila hacia arriba. Si ya esta en la primera fila, no consume el evento y la ventana gestiona la navegacion de lista. |
| Abajo     | Mueve la seleccion una fila hacia abajo. Si ya esta en la ultima fila, no consume el evento y la ventana gestiona la navegacion de lista. |
| Enter     | Copia el emoji seleccionado al portapapeles, oculta el launcher y pega automaticamente en la app anterior. Registra el uso en `EmojiUsageStore`. |
| Cmd+C     | Copia el emoji seleccionado al portapapeles sin ocultar la ventana ni pegar. Registra el uso. Solo activo en modo emoji (si `OnCopy` no es null). |
| Cmd+Shift+F | Marca o desmarca el emoji seleccionado como favorito. Actualiza `IsFavorite` en la celda y persiste en `EmojiUsageStore`. |

**Invariante:** las teclas izquierda/derecha nunca escapan del grid. Las teclas arriba/abajo escapan solo cuando no hay fila disponible en esa direccion, permitiendo al usuario navegar a otros resultados de la lista.

> **Verificar en:** `EmojiGridResultViewModel.SelectNext()`, `SelectPrevious()` (wrap circular), `SelectUp()`, `SelectDown()` (devuelven `bool`); `MainWindow.axaml.cs` -- `OnTunnelKeyDown()` maneja las flechas en fase tunnel; `OnKeyDown()` -- `Key.C` con Meta y `Key.F` con Meta+Shift.

---

## Footer contextual

Cuando el resultado seleccionado es un `EmojiGridResultViewModel`, el footer cambia de los atajos genericos ("navigate", "open") a los atajos especificos del modo emoji: "Cmd+C copy", "Enter paste", "Cmd+Shift+F fav". Los simbolos de Meta y Shift se obtienen de `AppHandler.Instance` para adaptarse a cada plataforma.

La propiedad `IsEmojiMode` en `MainWindowViewModel` se recalcula cada vez que cambia `SelectedResult`. En el AXAML, dos `StackPanel` con visibilidad opuesta (`IsVisible="{Binding IsEmojiMode}"` / `!IsEmojiMode`) muestran el footer correspondiente.

> **Verificar en:** `MainWindowViewModel.IsEmojiMode`, `OnSelectedResultChanged`; `MainWindow.axaml` -- footer con dos StackPanels condicionados.

---

## Favoritos y mas usados

Al escribir `:` sin termino de busqueda, el grid por defecto muestra tres secciones en orden:

1. **Favoritos**: emojis marcados por el usuario con Cmd+Shift+F, en el orden en que fueron anadidos. Maximo `EmojiMaxFavoriteRows * EmojiColumns` emojis (ver `AppDefaults`).
2. **Mas usados**: emojis ordenados por numero de usos descendente, excluyendo los que ya estan en favoritos. Maximo `EmojiMaxMostUsedRows * EmojiColumns` emojis.
3. **Resto**: todos los emojis restantes en orden Unicode CLDR (`sort_order`), sin duplicados con las secciones anteriores.

Las celdas de emojis favoritos tienen `IsFavorite = true` en su `EmojiCellViewModel`, lo que permite feedback visual futuro.

**Invariante:** nunca hay emojis duplicados entre las tres secciones. Un emoji favorito con uso alto solo aparece en la seccion de favoritos.

> **Verificar en:** `EmojiSearch.GetDefaultEmojis()` -- logica de merge; `EmojiUsageStore.Favorites`, `GetMostUsed()`; `AppDefaults.EmojiMaxFavoriteRows`, `EmojiMaxMostUsedRows`.

---

## Persistencia de uso

Los favoritos y contadores de uso se persisten en un fichero JSON separado (`AppPaths.EmojiUsageFile`, por defecto `emoji-usage.json` en el directorio de configuracion). El formato es:

```json
{ "favorites": ["emoji1", "emoji2"], "usage": { "emoji1": 42, "emoji2": 15 } }
```

`EmojiUsageStore` carga el fichero de forma asincrona durante `EmojiSearch.Start()`. Si el fichero no existe o esta corrupto, arranca con datos vacios sin error visible.

La escritura es atomica (fichero temporal + `File.Move`), el mismo patron que `EmojiDataLoader.WriteCompactCache`, para evitar corrupcion ante cierres inesperados.

> **Verificar en:** `EmojiUsageStore.LoadAsync()`, `Save()`; `AppPaths.EmojiUsageFile`; tests en `EmojiUsageStoreTests.cs`.

---

## Flujo de activacion (Enter)

Cuando el usuario pulsa Enter sobre el grid:

1. Se copia el caracter emoji seleccionado al portapapeles via `ClipboardService`.
2. Se registra el uso del emoji en `EmojiUsageStore`.
3. Se limpia el texto de busqueda.
4. Se oculta la ventana del launcher.
5. Se restaura el foco a la aplicacion que estaba activa antes de abrir Yottacast (`AppHandler.OnHide()`).
6. Se simula un pegado (Cmd+V en macOS, Ctrl+V en Windows) con un breve delay para que la app destino tenga tiempo de tomar el foco.

**Invariante:** el emoji se pega automaticamente en la aplicacion de destino sin intervencion adicional del usuario. Este comportamiento lo controla la propiedad `PasteAfterActivate = true`.

> **Verificar en:** `EmojiSearch.MakeGrid()` -- `OnActivate`, `OnCopy`, `OnToggleFavorite` y `PasteAfterActivate`; `MainWindow.axaml.cs` -- logica de Enter que invoca `OnActivate`, `Hide()`, `OnHide()`, `SimulatePasteAsync()`; `MacAppHandler.cs` / `WindowsAppHandler.cs` -- implementaciones de `SimulatePasteAsync()`.

---

## Algoritmo de matching

La busqueda compara el termino contra el nombre y los keywords de cada emoji. El sistema de puntuacion garantiza un orden de relevancia predecible:

| Tipo de coincidencia         | Score        | Ejemplo                                     |
|------------------------------|--------------|----------------------------------------------|
| Nombre exacto                | 3.0          | `:fire` coincide exactamente con "fire"       |
| Nombre parcial (NameMatcher) | 1.2 -- 2.0   | `:grin` coincide con "grinning face"          |
| Keyword (NameMatcher)        | 0.0 -- 1.0   | `:thumbsup` coincide con el keyword "thumbsup"|

**Invariante:** cualquier coincidencia por nombre siempre puntua mas alto que cualquier coincidencia exclusivamente por keyword. Esto garantiza que al buscar `:fire`, el emoji "fire" aparece antes que "fireworks" (que coincidiria por prefijo).

El matching de nombre usa los tokens pre-computados del nombre (space-split), aprovechando que los nombres de emoji son siempre minusculas separadas por espacios. Para keywords, se aplica `NameMatcher.Score(string, string)` a cada keyword individual y se toma el mejor score.

> **Verificar en:** `EmojiSearch.MatchScore()` -- logica de scoring con rangos; `EmojiEntry.NameTokens` -- pre-tokenizacion; `NameMatcher.Score()` en `NameMatcher.cs`.

---

## Datos de origen y cache

### Fuente de datos

Los emojis provienen del proyecto [iamcal/emoji-data](https://github.com/iamcal/emoji-data), un JSON con mas de 1600 entradas. El fichero `Search/Emoji/emoji-data.json` se descarga en tiempo de compilacion si no existe (target MSBuild `DownloadEmojiData`). No hay descarga en runtime.

Para actualizar a una version mas reciente, basta con eliminar `Search/Emoji/emoji-data.json` y recompilar.

### Estrategia de cache

El sistema usa una cache compacta (`emoji-cache.json`) para evitar parsear el JSON raw (~1.25 MB) en cada inicio. El formato compacto es un array de arrays de 5 elementos `[char, name, [keywords], category, sortOrder]` (~100-150 KB).

El flujo de carga sigue esta prioridad:

1. Cache en disco (ruta de AppData del SO) -- si existe, se usa directamente.
2. Cache embebida en el ensamblado -- si existe en el recurso embebido.
3. JSON raw embebido -- se parsea, se genera la cache en disco, y se devuelve.
4. Si todo falla -- se devuelve lista vacia sin crash. Los emojis simplemente no aparecen.

**Invariante:** la aplicacion nunca hace peticiones de red en runtime para obtener datos de emojis. Si la cache no esta lista cuando el usuario busca, se devuelve una lista vacia sin errores visibles.

La cache no tiene TTL: es valida indefinidamente. Para forzar una regeneracion, se elimina el fichero de cache.

La escritura de cache es atomica (fichero temporal + `File.Move`) para evitar corrupcion ante cierres inesperados.

> **Verificar en:** `EmojiDataLoader.LoadAsync()` -- flujo de carga con prioridades; `EmojiDataLoader.WriteCompactCache()` -- escritura atomica; `Yottacast.Core.csproj` -- targets `DownloadEmojiData` y `CopyEmojiCache`, y recursos embebidos.

---

## Parseo del JSON raw

Al procesar el JSON original de iamcal/emoji-data, se aplican las siguientes reglas:

| Regla                                  | Detalle                                                                                  |
|----------------------------------------|------------------------------------------------------------------------------------------|
| Emojis obsoletos se descartan          | Entradas con `obsoleted_by` no vacio se ignoran (reemplazados por versiones mas recientes). |
| Campo `unified` obligatorio            | Si falta o esta vacio, la entrada se descarta.                                           |
| Conversion de codepoints               | `unified` (hex separados por `-`) se convierte a string via `char.ConvertFromUtf32` por segmento. Si la conversion falla, se descarta silenciosamente. |
| Nombre normalizado a minusculas        | El campo `name` (mayusculas en el JSON) se normaliza a minusculas.                       |
| Keywords combinados                    | `short_names` y `texts` (ASCII como `:D`) se combinan en un unico array `Keywords`, eliminando duplicados. |

> **Verificar en:** `EmojiDataLoader.ParseRawJson()`.

---

## Consideraciones Unicode

- **Selector de variacion FE0F** -- Algunos codepoints terminan en `-FE0F` (emoji presentation selector). Se incluyen en la conversion; sin ellos, el emoji se renderiza como simbolo de texto.
- **Pares surrogados** -- La mayoria de emojis estan en el plano suplementario (codepoints > U+FFFF) y ocupan dos `char` en .NET. `char.ConvertFromUtf32` los genera correctamente.
- **Secuencias ZWJ** -- Emojis compuestos (familias, profesiones) usan Zero-Width Joiner (`U+200D`) entre codepoints, que se maneja sin problema al estar en el BMP.

> **Verificar en:** `EmojiDataLoader.UnifiedToChar()` -- conversion de hex a string; tests `ParseRawJson_IncludesFE0FVariationSelector` y `ParseRawJson_HandlesMultiCodepointEmoji` en `EmojiDataLoaderTests.cs`.

---

## Testing

Los tests cubren dos niveles:

| Nivel       | Clase                          | Estrategia                                                                                    |
|-------------|--------------------------------|-----------------------------------------------------------------------------------------------|
| Unitario    | `EmojiSearchTests`             | Prepueblan una cache compacta en un directorio temporal. No requieren red ni recurso embebido. Incluyen tests de favoritos, mas usados, OnCopy y OnToggleFavorite. |
| Unitario    | `EmojiUsageStoreTests`         | Prueban ToggleFavorite, RecordUsage, GetMostUsed, persistencia y recuperacion de fichero corrupto. |
| Unitario    | `EmojiDataLoaderTests`         | Prueban `ParseRawJson`, `ParseCompactCache` y `LoadAsync` con datos sinteticos y reales.      |
| Integracion | `EmojiSearchRealDataTests`     | Cargan el dataset completo embebido una sola vez via `IClassFixture<RealEmojiDataFixture>` y validan el matching contra datos de produccion. |

El acceso a metodos `internal` de `EmojiDataLoader` desde los tests se habilita mediante `InternalsVisibleTo("Yottacast.Core.Tests")` en el `.csproj`.

> **Verificar en:** `EmojiSearchTests.cs`, `EmojiDataLoaderTests.cs`, `RealEmojiDataFixture` en `EmojiSearchTests.cs`; atributo `InternalsVisibleTo` en `Yottacast.Core.csproj`.
