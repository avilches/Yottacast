# Plan: mejora de calidad y rendimiento de búsqueda

Análisis del código actual y propuestas concretas ordenadas por área. Cada propuesta incluye el problema exacto en el código, la solución, el impacto esperado y la complejidad de implementación.

---

> Las mejoras de scoring y ranking (normalización de escalas, factor de cobertura en NameMatcher, boost por frecuencia, score por directorio) se tratan en `plans/scoring.md`.

## 1. Velocidad de respuesta

### 2.1 Debounce de 250ms: perceptible para queries rápidas

**Problema.** En `MainWindowViewModel.SearchAsync` hay un `await Task.Delay(250, ct)` fijo antes de la fase deferred. Para un usuario que escribe despacio (o que ya paró de escribir), este delay es perceptible: las apps aparecen de inmediato pero los documentos tardan al menos 250ms.

El valor está hardcoded en la implementación.

**Solución propuesta.** Reducir a 150ms. Alternativamente, hacer el debounce adaptativo: si la query no ha cambiado en 100ms, lanzar ya la búsqueda deferred. Esto se puede lograr sin cambiar la arquitectura, simplemente reduciendo el delay.

**Impacto.** Documentos aparecen ~100ms antes. Beneficio especialmente notable cuando el usuario escribe y hace pausa.

**Complejidad.** Baja. Una constante con nombre en `MainWindowViewModel` en lugar del literal `250`.

---

### 2.2 SpotlightInterop bloquea el thread completo durante MDQueryExecute

**Problema.** `MDQueryExecute(query, KMDQuerySynchronous)` en `SpotlightInterop.Query` bloquea el thread hasta que Spotlight devuelve todos los resultados. La iteración posterior es rápida (solo leer el array en memoria), pero el bloqueo inicial puede durar segundos en queries amplias.

Esto se mitiga con `Task.Run` en `MacOsPlatformProvider.SearchFilesAsync`, pero usa un thread del pool durante todo el tiempo de espera.

**Solución propuesta a corto plazo.** No hay API asíncrona en `MDQuery` para el modo sincrónico. La alternativa real sería usar `MDQueryExecute` con `KMDQueryOptionDefault` (asíncrono) y registrar un `CFNotificationCenter` callback para `kMDQueryDidFinishNotification`. Esto es considerablemente más complejo (requiere un CFRunLoop activo en el thread).

**Solución práctica.** Añadir un timeout explícito al `CancellationToken` antes de llamar a `SpotlightInterop.Query` en `ScanAppsAsync`, de modo que si Spotlight tarda demasiado en el scan inicial, la app arranca igualmente. Para el scan de apps ya existe el mecanismo en `UserDocumentSearch` (timeout 20s), pero `ScanAppsAsync` no tiene timeout propio.

```csharp
// MacOsPlatformProvider.ScanAppsAsync — añadir timeout de seguridad
using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
timeoutCts.CancelAfter(TimeSpan.FromSeconds(10));
await Task.Run(() => SpotlightInterop.Query(predicate, dirs, callback, timeoutCts.Token), timeoutCts.Token);
```

**Impacto.** Evita arranques colgados si Spotlight está lento (p.ej. primera vez tras reinicio, indexando).

**Complejidad.** Baja (timeout). Alta (modo asíncrono MDQuery completo).

---

### 2.3 ApplicationSearch: O(n) scan completo en cada query

**Problema.** `ApplicationSearch.SearchAsync` itera sobre todos los valores del `ConcurrentDictionary` en cada llamada:

```csharp
var results = _apps.Values
    .Select(a => (app: a, score: NameMatcher.Score(a.Name, query)))
    .Where(x => x.score > 0)
    ...
```

Con 500–1000 apps en caché (normal en macOS con /Applications, /System/Applications y ~/Applications), esto ejecuta ~750 llamadas a `NameMatcher.Score` por keystroke, cada una con `SplitTokens` que aloca una lista nueva.

**Solución propuesta.** Pre-computar y cachear los tokens de cada app en `AppInfo` al momento de `AddApp`. `SplitTokens` es determinista y no depende de la query:

```csharp
public sealed class AppInfo {
    public IReadOnlyList<string> Tokens { get; }  // pre-computado
    // ...
    internal AppInfo(string name, string path, Func<string, string?> getIconPath) {
        Tokens = NameMatcher.SplitTokens(name);
        // ...
    }
}
```

Y en `NameMatcher.ScoreWith`, aceptar tokens pre-computados:
```csharp
public static double Score(AppInfo app, string query) => ScoreWith(app.Tokens, query);
```

Esto elimina la aloacción de `List<string>` por llamada y la tokenización repetida.

**Impacto.** Reducción de ~50% en tiempo de CPU por query, menos presión en GC. Notable con 1000+ apps.

**Complejidad.** Baja. Cambio en `AppInfo` y sobrecarga en `NameMatcher`.

---

### 2.4 GlobalSearch.SearchSourcesAsync: LINQ alloc por snapshot

**Problema.** En cada snapshot recibido de cualquier fuente, `GlobalSearch` ejecuta:

```csharp
yield return snapshots.SelectMany(s => s)
    .OrderByDescending(x => x.Score)
    .Take(limit)
    .ToList();
```

Esto crea un enumerador, una lista intermedia y ordena en cada snapshot. Para `UserDocumentSearch` que emite snapshots cada 200ms durante 20 segundos, son potencialmente 100 re-sorts del resultado fusionado.

**Solución propuesta.** Mantener una lista ordenada pre-calculada. Cuando llega un snapshot nuevo para el slot `i`, quitar los elementos del slot anterior y reinsertar los nuevos en posición correcta usando binary search. O más simple: dado que `limit` es 10 y las fuentes son pocas (3), el coste es mínimo y la optimización solo vale si se añaden más fuentes o el límite sube.

**Evaluación.** Con el diseño actual (3 fuentes, limit=10, ~100 snapshots en 20s), el coste total es despreciable (~3000 comparaciones). Esta optimización no es prioritaria hasta tener más fuentes.

**Complejidad.** Media. Diferir.

---

### 2.5 MathJsEngine: warm-up en background, pero WhenReady bloquea SearchInstantAsync

**Problema.** `CalculatorSearch.WhenReady()` delega en `engine.WhenReady()`. Esto significa que `GlobalSearch.SearchInstantAsync` (vía `SearchSourcesAsync`) hace `await s.WhenReady()` antes de llamar a `SearchAsync`, lo que bloquea el slot del calculator hasta que Jint termine de inicializar (puede ser 1–3s en el primer arranque).

Durante ese tiempo, el usuario ve resultados de apps pero no resultados de la calculadora, incluso para queries matemáticas.

Este comportamiento es correcto por diseño (no queremos resultados vacíos o erróneos), pero la UI no indica que hay algo cargando en fuentes instant.

**Solución propuesta.** `IsSearching` ya existe pero solo se activa para la fase deferred. Se podría extender para señalizar que fuentes instant están en warm-up. Alternativamente, `CalculatorSearch` puede devolver inmediatamente un snapshot vacío si el engine no está listo, en lugar de esperar.

**Complejidad.** Baja (devolver vacío si no listo). Media (indicador en UI para instant sources).

---

## 2. Relevancia: resultados incorrectos

### 2.1 UserDocumentSearch: sin filtro de extensiones irrelevantes

**Problema.** Spotlight devuelve cualquier fichero que coincida con `kMDItemFSName`. Una query "report" puede devolver `report.app`, `report.py`, `report.lock`, `report.DS_Store`, ficheros temporales, etc. No hay filtro de extensiones en `MacOsPlatformProvider.SearchFilesAsync`.

**Solución propuesta.** Añadir al predicado de Spotlight un filtro por tipo de documento:

```csharp
// Excluir tipos de sistema/temp
var excludeTypes = "kMDItemContentType != 'com.apple.application-bundle' && " +
                   "kMDItemFSName != '.DS_Store' && " +
                   "kMDItemFSName != '*.tmp' && " +
                   "kMDItemFSName != '*.lock'";
predicate = $"({predicate}) && ({excludeTypes})";
```

O mejor: aplicar un filtro de extensión en el callback `onResult` de `UserDocumentSearch`, descartando extensiones conocidas como ruido (`.tmp`, `.lock`, `.pyc`, `.class`, `.o`, `.a`, etc.).

**Impacto.** Menos ruido en resultados de documentos. Las primeras posiciones son ficheros abiertos por el usuario, no artefactos de build.

**Complejidad.** Baja. Lista de extensiones a excluir en `UserDocumentSearch` o en `MacOsPlatformProvider`.

---

### 2.2 Windows: ScanAppsAsync pierde apps en subdirectorios profundos

**Problema.** `WindowsPlatformProvider.ScanAppsAsync` solo busca un `.exe` en cada subdirectorio inmediato de los directorios configurados. Apps instaladas en estructuras más profundas (`C:\Program Files\Google\Chrome\Application\chrome.exe`) no se encuentran si `C:\Program Files\Google` no contiene directamente un `.exe`.

```csharp
foreach (var subDir in Directory.EnumerateDirectories(dir)) {
    var exe = Directory.EnumerateFiles(subDir, $"{folderName}.exe").FirstOrDefault()
           ?? Directory.EnumerateFiles(subDir, "*.exe").FirstOrDefault();
```

La búsqueda es exactamente 1 nivel profundo.

**Solución propuesta.** En Windows, la fuente canónica de apps instaladas es el registro (`HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall`). Usarlo en lugar del scan de directorios daría un resultado más completo y más rápido:

```csharp
using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
foreach (var subKeyName in key.GetSubKeyNames()) {
    using var subKey = key.OpenSubKey(subKeyName);
    var displayName = subKey?.GetValue("DisplayName") as string;
    var installLocation = subKey?.GetValue("InstallLocation") as string;
    // ...
}
```

**Impacto.** Cobertura completa de apps instaladas en Windows.

**Complejidad.** Media. Nuevo código en `WindowsPlatformProvider`, sin afectar otras plataformas.

---

### 2.3 Query de 1 carácter: apps sí, documentos no

**Problema.** `UserDocumentSearch` hace `yield break` si `query.Length < 2`, pero `ApplicationSearch` responde con cualquier query. Esto crea una asimetría: una query "s" devuelve apps pero no documentos. El umbral de 2 chars para documentos es razonable (Spotlight también lo aplica), pero puede sorprender al usuario.

**Solución.** Documentar el comportamiento en la UI (p.ej. subtitle del spinner: "Type 2+ characters to search files") o mostrar un hint cuando hay resultados de apps pero no de documentos con query corta.

**Complejidad.** Baja (hint en UI). Sin cambios en la lógica de búsqueda.

---

## 3. Fuzzy matching

### 3.1 No hay tolerancia a errores tipográficos

**Problema.** Una query "Saafri" no encuentra Safari. Una query "Acitivity Monitor" no encuentra "Activity Monitor". El NameMatcher actual es estricto: cada hump debe ser prefijo exacto del token correspondiente.

**Solución propuesta (Levenshtein en tokens).** Para queries de 4+ chars sin match exacto, aplicar una distancia de edición por token con umbral adaptativo:

```csharp
// En NameMatcher, como fallback después de todos los intentos actuales:
if (query.Length >= 4) {
    var bestFuzzyScore = TryFuzzyMatch(tokens, queryHumps);
    if (bestFuzzyScore > 0) return bestFuzzyScore * 0.7; // penalización por fuzzy
}

private static double TryFuzzyMatch(IReadOnlyList<string> tokens, IReadOnlyList<string> queryHumps) {
    // Para cada hump del query, buscar el token más cercano por Levenshtein
    // Permitir distancia 1 para humps de 4+ chars, distancia 0 para humps cortos
}
```

**Alternativa más simple: doble-metáfono o Soundex.** Para nombres de apps en inglés, un algoritmo fonético captura "Saafri" → "SFR" ≈ "Safari" → "SFR". Menor esfuerzo de implementación.

**Evaluación.** Levenshtein por token es el enfoque más correcto. La implementación de Levenshtein es ~20 líneas. El coste computacional es O(n*m) por par de strings, pero dado que los humps son cortos (3–10 chars) y el número de tokens es pequeño (2–6), el impacto en rendimiento es mínimo.

**Impacto.** Usuarios con errores tipográficos frecuentes encuentran lo que buscan. Especialmente útil para nombres largos como "Microsoft Teams" o "Visual Studio Code".

**Complejidad.** Media. Nueva función en `NameMatcher`, tests de regresión para asegurar que el fuzzy no introduce falsos positivos, y ajuste del score penalizado para que el fuzzy nunca supere a un match exacto.

---

### 3.2 Acrónimos no habituales no se encuentran

**Problema.** El matching de iniciales en `NameMatcher` requiere que los caracteres del query sean las primeras letras de tokens consecutivos desde el inicio. "vsc" no encuentra "Visual Studio Code" porque el initials fallback compara `string.Concat(tokens.Select(t => t[0]))` = "VSC" con el query "vsc" — en realidad esto sí funciona porque `StartsWith` es case-insensitive. Pero "vscode" no funciona porque no es un acrónimo estándar.

El problema real es para acrónimos conocidos que no siguen el patrón CamelCase exacto del nombre oficial. "slack" encuentra Slack (prefijo de token 0, score 1.0), pero si una app se llama "Slack for Teams", "slack" sí funciona; "sft" no funciona porque la lógica de initials solo concatena primeras letras de tokens ("Sft"), lo cual sí matchea. Revisar: el initials check sí funciona para "sft".

**Problema real identificado.** El initials fallback exige que los caracteres estén en posiciones de tokens consecutivos desde el inicio, pero no permite saltar tokens. "mc" no encuentra "Microsoft Word" porque las iniciales son "MW". "mc" podría referirse a apps que contengan "mc" en cualquier posición inicial.

**Solución.** Permitir iniciales no consecutivas (subsequence matching) con un score más bajo que las iniciales consecutivas:

```csharp
// Nueva capa en ScoreWith, después del initials check actual:
// Subsequence: todos los chars del query aparecen como primeras letras de algún token (en orden)
if (IsSubsequenceOfInitials(tokens, query))
    return 0.4;
```

**Complejidad.** Baja. Función adicional de 10–15 líneas en `NameMatcher`.

---

## 4. Índices

### 4.1 ApplicationSearch: no hay índice invertido por prefijo

**Problema.** Con el fix de tokens pre-computados (§2.3), el scan sigue siendo O(n) sobre todas las apps. Para 1000 apps esto es ~1ms, lo cual es aceptable. Sin embargo, si el número de apps crece (p.ej. en Windows con el registro que puede tener 500+ entradas) o si se añaden nuevas fuentes de datos, un índice mejoraría la latencia.

**Solución propuesta.** Un trie o un diccionario de prefijos construido al momento de `AddApp`/`RemoveApp`:

```csharp
// En ApplicationSearch:
private readonly Dictionary<string, List<string>> _prefixIndex = new();

private void IndexApp(AppInfo app) {
    foreach (var token in app.Tokens) {
        for (var len = 1; len <= token.Length; len++) {
            var prefix = token[..len].ToLowerInvariant();
            if (!_prefixIndex.TryGetValue(prefix, out var list)) {
                _prefixIndex[prefix] = list = [];
            }
            if (!list.Contains(app.Name)) list.Add(app.Name);
        }
    }
}
```

Con este índice, una query "saf" hace lookup directo en `_prefixIndex["saf"]` (O(1)) en lugar de iterar todas las apps.

**Evaluación.** Con 1000 apps y tokens de longitud media 6, el índice tiene ~6000 entradas. La memoria adicional es negligible (~500KB). La construcción es O(n * longitud_media_token). El beneficio real solo se nota para 5000+ apps.

**Complejidad.** Media. El índice invertido complica el mantenimiento (remove requiere reindexar o mantener referencias). Para el tamaño actual del problema, diferir.

---

### 4.2 EmojiSearch: scan lineal sobre ~1800 emojis en cada keystroke

**Problema.** `FilterEmojis` en `EmojiSearch` hace un scan completo de todos los emojis en cada keystroke con una query `:term`. Con ~1800 emojis en el dataset, esto es ~1800 llamadas a `MatchScore` que incluyen hasta 6 comparaciones de string cada una.

**Solución propuesta.** Pre-construir un índice de palabras clave al inicializar `Entries`:

```csharp
private static readonly Lazy<Dictionary<string, List<EmojiEntry>>> KeywordIndex = new(() => {
    var idx = new Dictionary<string, List<EmojiEntry>>(StringComparer.OrdinalIgnoreCase);
    foreach (var e in Entries.Value) {
        AddToIndex(idx, e.Name);
        foreach (var kw in e.Keywords) AddToIndex(idx, kw);
    }
    return idx;
});
```

**Impacto.** Lookup de O(1800) a O(resultados). Dado que el tiempo actual es <1ms (strings cortas, CPU moderna), el impacto perceptible es nulo. Solo vale si hay más emojis o más keywords.

**Complejidad.** Media. Diferir.

---

## 5. Spotlight / MDQuery

### 5.1 Spotlight para documentos: predicado AND por tokens es muy restrictivo

**Problema.** En `MacOsPlatformProvider.SearchFilesAsync`, para una query multi-token como "informe ventas 2024":

```csharp
predicate = "kMDItemFSName == '*informe*'cd && kMDItemFSName == '*ventas*'cd && kMDItemFSName == '*2024*'cd"
```

Esto requiere que los tres tokens aparezcan en el nombre del fichero. Pero un fichero llamado "Informe Q4 2024.pdf" cuyo contenido menciona "ventas" no aparecería. Spotlight puede buscar también en el contenido con `kMDItemTextContent`, pero esto es más lento.

**Solución propuesta.** Para queries multi-token, construir un predicado OR primero en Spotlight (para obtener candidatos ampliamente) y luego refinar client-side en el callback. Actualmente el callback de `UserDocumentSearch` ya filtra con `queryTokens.All(t => nameLower.Contains(t))`, así que Spotlight ya funciona de pre-filtro. El problema es que pre-filtra demasiado agresivamente (AND en lugar de OR en Spotlight).

```csharp
// Cambiar AND por OR en el predicado de Spotlight, dejando el filtrado estricto al callback:
predicate = string.Join(" || ", tokens.Select(t => $"kMDItemFSName == '*{t}*'cd"));
```

Y el callback de `UserDocumentSearch` ya aplica el filtro AND client-side. Esto trae más candidatos de Spotlight pero el callback los filtra correctamente.

**Impacto.** Ninguno si todos los tokens están en el nombre (mismo resultado). Mejor recall si algún token está en el contenido y se relaja la query.

**Complejidad.** Baja. Cambio de `&&` a `||` en la construcción del predicado. Cuidado: trae más resultados de Spotlight, lo que aumenta el tiempo de respuesta para queries amplias.

---

### 5.2 Spotlight: sin paginación ni streaming real

**Problema.** `SpotlightInterop.Query` con `KMDQuerySynchronous` bloquea hasta tener TODOS los resultados y solo entonces itera. No hay streaming real: los resultados llegan en bloque, no uno a uno. Esto explica por qué `UserDocumentSearch` puede tardar varios segundos en emitir el primer snapshot para queries con muchos resultados.

**Solución propuesta.** La API de MDQuery en modo asíncrono (`KMDQueryOptionDefault`) permite recibir notificaciones incrementales via `kMDQueryDidUpdateNotification`. Implementar esto requiere un `CFRunLoop` en un thread dedicado, que puede mantenerse vivo durante toda la sesión de búsqueda.

Arquitectura:

1. Thread dedicado con `CFRunLoop` para Spotlight.
2. `SpotlightInterop.QueryAsync` que inicia una query asíncrona y emite paths via `IAsyncEnumerable` o `Channel`.
3. Los primeros resultados llegan en ~50–200ms en lugar de esperar el bloque completo.

**Impacto.** El primer snapshot de documentos aparece mucho antes. La percepción de velocidad mejora notablemente.

**Complejidad.** Alta. Requiere P/Invoke para `CFRunLoop`, manejo de `CFNotificationCenter`, y un thread dedicado. Es el mayor cambio de rendimiento posible en Spotlight.

---

## 6. Tests de calidad

> Los tests de calidad de ranking (end-to-end y normalización de scores) se tratan en `plans/scoring.md §9`.

### 6.1 Tests de fuzzy matching (ausentes)

Cuando se implemente §3.1, añadir:

```csharp
[Theory]
[InlineData("Saafri",   "Safari",           true)]   // typo en vocal
[InlineData("Activty",  "Activity Monitor", true)]   // letra omitida
[InlineData("Chrme",    "Google Chrome",    false)]  // muy diferente, no debe matchear
public void FuzzyMatch_HandlesTypos(string query, string appName, bool shouldMatch)
```

### 6.2 Tests de UserDocumentSearch con extensiones a excluir (ausentes)

```csharp
[Theory]
[InlineData("report.tmp",  "report", false)]  // temp file excluido
[InlineData("report.lock", "report", false)]  // lock file excluido
[InlineData("report.pdf",  "report", true)]   // documento real incluido
public async Task DocSearch_ExcludesNoisyExtensions(
    string fileName, string query, bool shouldAppear)
```

---

## Resumen priorizado

> El scoring y el boost por frecuencia se tratan en `plans/scoring.md`. Las prioridades aquí son de rendimiento y relevancia de búsqueda.

| # | Propuesta | Impacto | Complejidad | Prioridad |
|---|---|---|---|---|
| 1.3 | Pre-computar tokens en AppInfo | Medio | Baja | 1 |
| 2.1 | Filtrar extensiones de ruido en documentos | Medio | Baja | 2 |
| 1.1 | Reducir debounce a 150ms | Bajo | Baja | 3 |
| 3.1 | Fuzzy matching (Levenshtein por token) | Alto | Media | 4 |
| 3.2 | Subsequence initials matching | Medio | Baja | 5 |
| 2.2 | Windows: scan por registro en vez de dirs | Alto (Windows) | Media | 6 |
| 6.1–6.2 | Tests de calidad (fuzzy + extensiones) | Alto (seguridad) | Baja | En paralelo con cada cambio |
| 5.2 | Spotlight asíncrono (streaming real) | Alto | Alta | 7 |
| 1.4 | Timeout en ScanAppsAsync | Bajo | Baja | 8 |
| 4.1 | Índice invertido en ApplicationSearch | Bajo | Media | Diferir |
| 4.2 | Índice de keywords en EmojiSearch | Nulo | Media | No hacer |
| 1.5 | Lista ordenada en GlobalSearch | Nulo | Media | No hacer |
