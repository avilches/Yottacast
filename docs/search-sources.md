# Fuentes de búsqueda

## ApplicationSearch

Clase: `Yottacast.Core.Search.Application.ApplicationSearch` (implementa `IInstantSearchSource`)

Mantiene un `ConcurrentDictionary<string, AppInfo>` en memoria con las apps instaladas.
Inyecta `UserSettings` para leer `AppDirectories`.

**Arranque por plataforma** — toda la lógica OS-específica está en `PlatformProvider`:
- **macOS**: `ScanAppsAsync` ejecuta `mdfind` con `StandardCommandRunner`; `CreateAppWatchers` monta watchers en `*.app`.
- **Windows**: `ScanAppsAsync` escanea `AppDirectories` buscando `.exe`; `CreateAppWatchers` en `*.exe`.
- **Linux**: `ScanAppsAsync` escanea `AppDirectories` buscando `.desktop`; `CreateAppWatchers` en `*.desktop`.

Evento `AppAdded` notifica cuando se detecta una app nueva (disponible para suscriptores externos; actualmente ningún componente lo consume).

**Cambio de AppDirectories en settings** ⚠️ TODO: `ReloadAppDirectories()` no está implementado. `SettingsWindowViewModel` tiene `ApplicationSearch` inyectado — cuando se añada UI para `AppDirectories`, implementar `ReloadAppDirectories()` que haga `Stop()` + `Start()` limpiando el caché.

**Gotcha: Lazy icon en AppInfo** — `AppInfo` usa `Lazy<T>` para diferir la lectura de `Info.plist` hasta el primer acceso al icono, evitando parsear cientos de plists al arranque.

**Métodos de consulta directa** — usados por `BrowserDiscovery` y `TerminalDiscovery` para consultar el caché sin pasar por la pipeline de búsqueda:

| Método | Comportamiento |
|---|---|
| `Find(string name)` | Búsqueda exacta en el caché por clave de nombre (case-insensitive); devuelve `AppInfo?` |
| `FindAll()` | Devuelve todas las apps en caché como `IReadOnlyList<AppInfo>` |

## UserDocumentSearch

Clase: `Yottacast.Core.Search.UserDocuments.UserDocumentSearch` (implementa `IDeferredSearchSource`)

Sin caché. Cada búsqueda llama a `FileSearch.SearchAsync` con `settings.ExpandedSearchFolders`.
Si los directorios cambian en settings, la siguiente búsqueda los usará automáticamente.

`Start()`, `WhenReady()` y `Stop()` son no-ops (no hay estado que gestionar).

**Queries cortas**: hace `yield break` si `query.Length < 2` — la búsqueda de ficheros requiere al menos 2 caracteres. `FileSearch` y los `PlatformProvider` hacen early return (`Task.CompletedTask`) para queries vacías.

**Criterios de parada**: `SearchAsync` crea un `CancellationTokenSource` interno ligado al `ct` del caller.
- **Timeout configurable** (por defecto `20_000ms`, parámetro `timeoutMs` del constructor): `cts.CancelAfter(timeoutMs)` detiene mdfind tras el tiempo configurado.
- El `OperationCanceledException` se captura — se emite un snapshot final con lo que hay en el buffer.
- No hay cap de líneas (`maxResults: int.MaxValue`): el timeout y el early exit son los únicos mecanismos de parada.

**Flujo completo**:
```
mdfind emite línea → onResult callback → puntúa → añade al buffer
                   → cada SnapshotIntervalMs → snapshot parcial al channel
cts.Token expira (timeoutMs) → OperationCanceledException → snapshot final → channel.Complete()
MainWindowViewModel recibe snapshots → RefreshResults() en cada uno
```

## Scoring

### ApplicationSearch — NameMatcher

`NameMatcher` expone dos overloads públicos:
- `Score(string name, string query)` — tokeniza el nombre internamente con `SplitTokens` y delega en el otro overload.
- `Score(IReadOnlyList<string> tokens, string name, string query)` — acepta tokens pre-computados; usado por `EmojiSearch` que almacena `NameTokens` en `EmojiEntry` para no re-tokenizar en cada keystroke.

Ambos overloads aplican el mismo algoritmo: evalúan el query tal como viene y, si la query es todo minúsculas y el score no es el máximo, reintenta con `query.ToUpperInvariant()` (para que "am" coincida con "Activity Monitor"). `SplitTokens` es público para que los consumidores puedan pre-computar tokens cuando la cadena de entrada es estable.

`ScoreWith` implementa cuatro modos de matching por prioridad descendente:

1. **CamelHump prefix** — cada hump del query debe ser prefijo del token correspondiente en la secuencia:
   - Match empezando en el hump 0 (inicio del nombre). Ej. "Saf" → "Safari", "ActMon" → "Activity Monitor".
   - Match empezando en hump > 0 (interior del nombre). Ej. "Mon" puede coincidir con "Activity Monitor" desde el token "Monitor".
   - "af" NO coincide con "Safari" (no es prefijo de ningún token).
2. **Iniciales**: las iniciales concatenadas de todos los tokens empiezan por el query. "AM" → "Activity Monitor", "MON" → "Microsoft OneNote" (M=Microsoft, O=One, N=Note del CamelCase).
3. **Abreviatura multi-palabra**: variante de abreviatura que cubre patrones de query más largos donde los humps del query se distribuyen entre múltiples tokens. Ver `NameMatcher.cs` para la lógica exacta.
4. **Substring interno**: el nombre contiene el query, solo para queries de una longitud mínima. "ari" → "Safari".

Sin match: devuelve 0.

Ver `Search/Application/NameMatcher.cs` y `NameMatcherTests.cs` para los valores exactos de scoring y casos canónicos.

Los scores exactos de cada fuente se definen en cada clase de búsqueda y en `MainWindowViewModel.MakeGoogleItem()`.

### UserDocumentSearch

Para **queries con wildcard** (`*`) todos los resultados reciben un score base. Para **queries sin wildcard**, distingue dos modos:

**Query de un token** (ej. `"report"`): el score varía según si el nombre es exactamente el query, empieza por él, termina en él, o simplemente lo contiene.

Stem = `Path.GetFileNameWithoutExtension(name)`, por lo que `"report"` puntúa igual que el nombre completo contra `"report.pdf"`.

**Query multi-token** (ej. `"xls calc mis"`): la plataforma construye un predicado AND en Spotlight/Windows Search/locate para pre-filtrar, pero el callback `onResult` aplica un segundo filtro client-side: descarta cualquier resultado donde `!queryTokens.All(t => nameLower.Contains(t))`. El scoring de los resultados que pasan ese filtro depende de si todos los tokens son prefijo de algún segmento del nombre (split por espacios/guiones/puntos) o solo aparecen como substring.

Ejemplo: `"xls calc mis"` → `"mis calculos.xls"`: segmentos `["mis","calculos","xls"]`; "mis"→"mis"✓, "calc"→"calculos"✓, "xls"→"xls"✓ → score prefijo.

Ver `Search/UserDocuments/UserDocumentSearch.cs` para los valores exactos de scoring.

`FileResult` solo contiene `Name` y `Path` — no hay distinción entre fichero y directorio en los resultados.

**Snapshots progresivos**: `UserDocumentSearch` emite un snapshot cuando ha transcurrido suficiente tiempo desde el último (el intervalo está definido como constante local dentro de `SearchAsync`) — throttling puramente por tiempo, no por número de resultados — y uno final al terminar o cancelar. Esto evita que queries con muchos resultados (p.ej. "a") saturen la UI con decenas de actualizaciones por segundo.
