# Busqueda de emojis

## Proposito

Yottacast permite buscar e insertar emojis en cualquier aplicacion. El usuario escribe `:` seguido de un termino de busqueda en el launcher y obtiene un grid visual con los emojis candidatos. Al confirmar la seleccion, el emoji se copia al portapapeles y se pega automaticamente en la aplicacion de destino.

---

## Activacion y query

| Query         | Comportamiento                                                                        |
|---------------|---------------------------------------------------------------------------------------|
| `:`           | Muestra todos los emojis ordenados por `sort_order` positivo ascendente (orden Unicode CLDR). Se excluyen los que tienen `sort_order == 0`. El viewport muestra las primeras filas visibles. |
| `:smile`      | Filtra todos los emojis cuyo nombre o keywords coincidan con "smile", ordenados por relevancia descendente. El filtrado no recorta por `limit` (todos los matches con score > 0 entran en el grid). |
| `: smile`     | Equivalente a `:smile` -- los espacios tras los dos puntos se ignoran.                |
| `smile`       | Sin `:` inicial, la busqueda de emojis no se activa. El usuario nunca ve emojis si no escribe `:`. |

**Invariante:** la busqueda de emojis solo se activa cuando la query empieza por `:`. Cualquier otro prefijo no produce resultados de emojis.

---

## Presentacion del grid

Los resultados se muestran como un unico item de tipo grid en la lista de resultados, en lugar de items individuales. El grid contiene:

- Secciones con cabecera que agrupa emojis por tipo: favoritos, frecuentes y categorias Unicode (por ejemplo "Smileys & Emotion", "People & Body"). Cada seccion tiene un header visible que se desplaza con el contenido.
- Cada seccion renderiza sus celdas en una `UniformGrid` con el numero de columnas y filas de viewport definidos por `EmojiLayoutConfig` (singleton mutable que `ThemeService` actualiza en cada cambio de tema; default `AppDefaults.EmojiColumns`/`EmojiViewportRows`). `EmojiSearch` los lee al construir el grid.
- Debajo del grid, informacion del emoji seleccionado: nombre, categoria y keywords.

El primer emoji del grid aparece seleccionado inicialmente. El icono y titulo del resultado en la lista se toman del primer emoji del grid. La categoria del resultado siempre es `"Emoji"`, el `ScoreReason` es `"Grid de emojis"` y el score es `5.5`.

**Invariante:** siempre se devuelve exactamente 0 o 1 resultado (nunca multiples items en la lista). Si no hay emojis que coincidan, no se muestra ningun resultado. Si no hay favoritos ni frecuentes, la primera seccion visible es la primera categoria Unicode - no se muestran secciones vacias.

> **Verificar en:** `EmojiSearch.MakeGrid()` -- construccion del `EmojiGridResultViewModel`; `EmojiGridResultView.axaml` -- template AXAML con secciones (`EmojiGridSection`) y panel de informacion; `EmojiGridResultViewModel.VisibleSections` -- agrupacion del viewport en secciones.

---

## Navegacion con teclado

| Tecla     | Comportamiento                                                                                          |
|-----------|---------------------------------------------------------------------------------------------------------|
| Izquierda | Mueve la seleccion a la celda anterior (con wrap circular al final del grid). Siempre consume el evento. |
| Derecha   | Mueve la seleccion a la celda siguiente (con wrap circular al inicio del grid). Siempre consume el evento. |
| Arriba    | Mueve la seleccion una fila hacia arriba. Si ya esta en la primera fila, no consume el evento y la ventana gestiona la navegacion de lista. |
| Abajo     | Mueve la seleccion una fila hacia abajo. Si ya esta en la ultima fila, no consume el evento y la ventana gestiona la navegacion de lista. |
| Enter     | Accion "Close and paste": copia el emoji seleccionado al portapapeles, oculta el launcher y pega automaticamente en la app anterior. Registra el uso en `EmojiUsageStore`. |
| Cmd+C     | Accion "Copy" (`ActionHotkey.MetaC`): copia el emoji seleccionado al portapapeles, oculta el launcher y restaura el foco a la app anterior (sin pegar). Registra el uso. `Meta` resuelve a Cmd en macOS y Ctrl en Windows/Linux. |
| Cmd+Shift+F | Accion "Favorite" (`ActionHotkey.MetaShiftF`, con `RequiresRefresh = true`): marca o desmarca el emoji seleccionado como favorito. Actualiza `IsFavorite` en todas las celdas con ese char y persiste en `EmojiUsageStore`. |

**Invariante:** las teclas izquierda/derecha nunca escapan del grid. Las teclas arriba/abajo escapan solo cuando no hay fila disponible en esa direccion, permitiendo al usuario navegar a otros resultados de la lista.

> **Verificar en:** `EmojiGridResultViewModel.SelectNext()`, `SelectPrevious()` (wrap circular), `SelectUp()`, `SelectDown()` (devuelven `bool`); `EmojiSearch.MakeGrid()` -- acciones "Close and paste", "Copy" (`MetaC`), "Favorite" (`MetaShiftF`) y callbacks `OnLeft`/`OnRight`/`OnUp`/`OnDown`; `MainWindow.axaml.cs` -- maneja las flechas y dispara las acciones por hotkey; `ActionHotkey.cs` -- `Meta` agnostico de plataforma.

---

## Footer contextual

Cuando el resultado seleccionado es un `EmojiGridResultViewModel`, el footer cambia de los atajos genericos ("navigate", "open") a los atajos especificos del modo emoji: "Cmd+C copy", "Enter paste", "Cmd+Shift+F fav". Los simbolos de Meta y Shift se obtienen de `AppHandler.Instance` para adaptarse a cada plataforma.

La propiedad `IsEmojiMode` en `MainWindowViewModel` se recalcula cada vez que cambia `SelectedResult`. En el AXAML, dos `StackPanel` con visibilidad opuesta (`IsVisible="{Binding IsEmojiMode}"` / `!IsEmojiMode`) muestran el footer correspondiente.

> **Verificar en:** `MainWindowViewModel.IsEmojiMode`, `OnSelectedResultChanged`; `MainWindow.axaml` -- footer con dos StackPanels condicionados.

---

## Favoritos y mas usados

Al escribir `:` sin termino de busqueda, el grid por defecto muestra secciones con cabeceras visibles:

1. **Seccion combinada "Favorites & recently used"**: favoritos primero (marcados con Cmd+Shift+F), hasta `EmojiMaxFavorites` celdas (maximo 4); despues los emojis mas usados (excluyendo favoritos), hasta completar el total efectivo de la seccion pinned. El total efectivo es `EmojiMaxPinnedTotal` redondeado HACIA ARRIBA al siguiente multiplo de columnas, para que la seccion pinned nunca termine a mitad de fila (con 10 columnas el total es 10; con 8 columnas es 16; con 12 columnas es 12). Favoritos tienen `Section = Favorite`; los mas usados tienen `Section = MostUsed`. Ambos tipos comparten la misma cabecera de seccion visible.
2. **Secciones por categoria Unicode**: la lista completa de emojis restantes en orden Unicode CLDR (`sort_order`), agrupados por su categoria ("Smileys & Emotion", "People & Body", etc.). Cada celda tiene `Section = Default` y la cabecera se toma de `Category`.

Las cabeceras de seccion se renderizan en la UI con estilos controlados por tema (`Theme.Emoji.SectionHeader.*`). Las secciones se calculan en `EmojiGridResultViewModel.VisibleSections` agrupando las celdas visibles del viewport por `EmojiSection` y `Category`.

Las celdas de emojis favoritos tienen `IsFavorite = true` mostrando una estrella en la esquina. Las celdas con uso previo muestran un contador de uso (`UsageCount`).

**Ranking de más usados:** el orden usa un *decay score*: `count × 0.5^(días_desde_último_uso / halfLifeDays)` con `halfLifeDays = AppDefaults.EmojiHalfLifeDays` (30 días por defecto). Un emoji usado hace más de 30 días sin volver a usarse baja en el ranking; uno usado recientemente con menos usos totales puede superarlo. Tras ~4 meses sin uso, el score decae hasta ser despreciable.

**Límite total de la sección pinned:** favoritos + más usados juntos llenan como máximo el total efectivo de la sección (`EmojiMaxPinnedTotal` redondeado hacia arriba al múltiplo de columnas, ver arriba). El número de más usados se calcula como `effectivePinnedTotal - favorites.Count`. Con 10 columnas y 0 favoritos: hasta 10 más usados; con 4 favoritos: hasta 6. Con 8 columnas (total efectivo 16) y 0 favoritos: hasta 16 más usados.

Al alternar favorito con `OnToggleFavorite`, se reconstruye el grid y el cursor se mantiene en el mismo indice numerico. El unico caso especial es cuando ese indice ya no existe (p.ej. el ultimo favorito se quitó y el grid encoge): en ese caso el cursor va al indice anterior (`Count - 1`), que es el emoji que estaba a su izquierda.

**Disposicion de secciones:** la seccion Default contiene SIEMPRE todos los emojis en orden Unicode CLDR, sin excluir favoritos ni frecuentes. Adicionalmente, los favoritos aparecen tambien en la seccion Favorites (al principio) y los mas usados en la seccion Frequently Used. Esto imita el comportamiento del picker de emoji del sistema operativo, donde los favoritos y frecuentes son secciones de acceso rapido que no desplazan al emoji de su posicion natural. No se muestran secciones vacias.

> **Verificar en:** `EmojiSearch.GetDefaultEmojis()` -- lógica de merge con `EmojiSection` y cálculo de límite dinámico; `EmojiSearch.MakeGrid()` -- asignación de `Section` y `UsageCount`; `EmojiUsageStore.Favorites`, `GetMostUsed()`, `GetUsageCount()`, `DecayScore()`; `AppDefaults.EmojiMaxFavorites`, `EmojiMaxPinnedTotal`, `EmojiHalfLifeDays`; `EmojiGridResultViewModel.VisibleSections`, `PinnedCount()` y `SectionKey()` -- viewport por secciones y agrupación (Favorite y MostUsed comparten la misma clave); `MainWindow.axaml.cs` -- la accion con `RequiresRefresh = true` captura el indice/char seleccionado y, tras `RefreshSearch()`, restaura el cursor (cae a `Cells.Count - 1` si el indice ya no existe).

---

## Persistencia de uso

Los favoritos y contadores de uso se persisten en un fichero JSON separado (`AppPaths.EmojiUsageFile`, por defecto `emoji-usage.json` en el directorio de configuracion). El formato es:

```json
{
  "favorites": ["emoji1", "emoji2"],
  "usage": {
    "emoji1": { "count": 42, "lastUsedAt": "2026-04-20T10:30:00Z" },
    "emoji2": { "count": 15, "lastUsedAt": "2026-01-01T00:00:00Z" }
  }
}
```

`EmojiUsageStore` carga el fichero de forma asincrona durante `EmojiSearch.Start()`. Si el fichero no existe o esta corrupto, arranca con datos vacios sin error visible.

El formato acepta valores enteros en `usage` como migración automática (formato anterior): se leen como `count` con `lastUsedAt = DateTime.UtcNow` en el momento de la carga.

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

**Invariante:** el emoji se pega automaticamente en la aplicacion de destino sin intervencion adicional del usuario. Este comportamiento lo controla la accion por defecto (label "Close and paste") con `PasteAfterClose = true` (campo de `ResultAction`).

> **Verificar en:** `EmojiSearch.MakeGrid()` -- lista `Actions` (acciones "Close and paste" con `PasteAfterClose`, "Copy", "Favorite" con `RequiresRefresh`), `OnLeft`/`OnRight`/`OnUp`/`OnDown` y `GetDragPayload`; `MainWindow.axaml.cs` -- logica de Enter que invoca la accion, `Hide()`, `OnHide()`, `SimulatePasteAsync()`; `MacAppHandler.cs` / `WindowsAppHandler.cs` -- implementaciones de `SimulatePasteAsync()`.

---

## Algoritmo de matching

La busqueda compara el termino contra el nombre y los keywords de cada emoji. El sistema de puntuacion garantiza un orden de relevancia predecible:

| Tipo de coincidencia         | Score        | Ejemplo                                     |
|------------------------------|--------------|----------------------------------------------|
| Nombre exacto                | 3.0          | `:fire` coincide exactamente con "fire"       |
| Nombre parcial (NameMatcher) | 1.2 -- 2.0   | `:grin` coincide con "grinning face" (score + 1) |
| Keyword (NameMatcher)        | 0.0 -- 1.0   | `:thumbsup` coincide con el keyword "thumbsup"|
| Categoria (NameMatcher × 0.5)| 0.0 -- 0.5   | coincidencia con el nombre de categoria; puntua por debajo de keywords |

**Invariante:** cualquier coincidencia por nombre siempre puntua mas alto que cualquier coincidencia exclusivamente por keyword. Esto garantiza que al buscar `:fire`, el emoji "fire" aparece antes que "fireworks" (que coincidiria por prefijo).

El matching usa tokenizaciones pre-computadas: `EmojiEntry` construye un `MatchableName` para el nombre, otro por cada keyword y uno para la categoria al cargar los datos, de modo que `FilterEmojis` (que recorre todo el dataset en cada keystroke) nunca re-tokeniza. El nombre se matchea con `NameMatcher.Match(NameMatch, term)`; cada keyword con su `MatchableName`, tomando el mejor score; y si no hay match de nombre ni keyword, se intenta contra la categoria, ponderada a la mitad (`× 0.5`) para que nunca supere a un keyword. Como los nombres de emoji son minusculas separadas por espacios, la tokenizacion de `NameMatcher` coincide con un simple split por espacios.

**Queries multi-palabra:** si el termino contiene varias palabras (`:flag sp`), cada token debe coincidir y el score final es el minimo entre los tokens. Si algun token no coincide, el emoji se descarta.

> **Verificar en:** `EmojiSearch.MatchScore()` y `EmojiSearch.SingleTermScore()` -- logica de scoring con rangos, match de categoria y queries multi-palabra; `EmojiEntry.NameMatch`/`KeywordMatches`/`CategoryMatch` -- tokenizaciones pre-computadas; `NameMatcher.Match(MatchableName, string)` en `NameMatcher.cs`.

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
