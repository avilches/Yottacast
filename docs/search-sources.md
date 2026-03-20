# Fuentes de búsqueda

## ApplicationSearch

Clase: `Yottacast.Core.Search.ApplicationSearch` (implementa `ISearchSource`)

Mantiene un `ConcurrentDictionary<string, AppInfo>` en memoria con las apps instaladas.
Inyecta `UserSettings` para leer `AppDirectories`.

**Arranque por plataforma** — toda la lógica OS-específica está en `PlatformProvider`:
- **macOS**: `ScanAppsAsync` ejecuta `mdfind` con `StandardCommandRunner`; `CreateAppWatchers` monta watchers en `*.app`.
- **Windows**: `ScanAppsAsync` escanea `AppDirectories` buscando `.exe`; `CreateAppWatchers` en `*.exe`.
- **Linux**: `ScanAppsAsync` escanea `AppDirectories` buscando `.desktop`; `CreateAppWatchers` en `*.desktop`.

Evento `AppAdded` notifica cuando se detecta una app nueva (disponible para suscriptores externos; actualmente ningún componente lo consume).

**Cambio de AppDirectories en settings** ⚠️ TODO: `ReloadAppDirectories()` no está implementado. `SettingsWindowViewModel` tiene `ApplicationSearch` inyectado — cuando se añada UI para `AppDirectories`, implementar `ReloadAppDirectories()` que haga `Stop()` + `Start()` limpiando el caché.

**Gotcha: Lazy icon en AppInfo** — `AppInfo` usa `Lazy<T>` para diferir la lectura de `Info.plist` hasta el primer acceso al icono, evitando parsear cientos de plists al arranque.

## UserDocumentSearch

Clase: `Yottacast.Core.Search.UserDocumentSearch` (implementa `ISearchSource`)

Sin caché. Cada búsqueda llama a `FileSearch.SearchAsync` con `settings.ExpandedSearchFolders`.
Si los directorios cambian en settings, la siguiente búsqueda los usará automáticamente.

`Start()` y `Stop()` son no-ops (no hay estado que gestionar).

**Queries cortas**: hace `yield break` si `query.Length < 2` — la búsqueda de ficheros requiere al menos 2 letras. `FileSearch` y los `PlatformProvider` hacen early return (`Task.CompletedTask`) para queries vacías.

**Criterios de parada**: `SearchAsync` crea un `CancellationTokenSource` interno ligado al `ct` del caller.
- **Timeout configurable** (por defecto `20_000ms`, parámetro `timeoutMs` del constructor): `cts.CancelAfter(timeoutMs)` detiene mdfind tras el tiempo configurado.
- El `OperationCanceledException` se captura — se emite un snapshot final con lo que hay en el buffer.
- No hay cap de líneas (`maxResults: int.MaxValue`): el timeout y el early exit son los únicos mecanismos de parada.

**Flujo completo**:
```
mdfind emite línea → onResult callback → puntúa → añade al buffer
                   → cada 200ms (SnapshotIntervalMs) → snapshot parcial al channel
cts.Token expira (timeoutMs) → OperationCanceledException → snapshot final → channel.Complete()
MainWindowViewModel recibe snapshots → RefreshResults() en cada uno
```

## Scoring

### ApplicationSearch — NameMatcher

`NameMatcher` implementa tres modos de matching por prioridad:
1. **Prefix de token** (Score = 1.0): cualquier token CamelCase o palabra empieza por el query. "Saf" → "Safari", "Mon" → "Activity Monitor". "af" NO coincide con "Safari".
2. **Iniciales** (Score = 1.0): las iniciales de todos los tokens empiezan por el query. "AM" → "Activity Monitor", "MON" → "Microsoft OneNote" (M=Microsoft, O=One, N=Note del CamelCase).
3. **Substring interno** (Score = 0.25): el nombre contiene el query (solo para queries ≥ 2 letras). "ari" → "Safari".

Scores de referencia: Calculator/Converter = 4 · Google = 3 · Apps prefix/initials = 1.0 · App substring = 0.25.

### UserDocumentSearch

Para **queries con wildcard** (`*`) todos los resultados puntúan 0.5 (base). Para **queries sin wildcard**, distingue dos modos:

**Query de un token** (ej. `"report"`):

| Condición | Score |
|---|---|
| `name == query` o `stem == query` (case-insensitive) | **1.0** |
| `name.StartsWith(query)` o `stem.StartsWith(query)` | **0.75** |
| `name.EndsWith(query)` o `stem.EndsWith(query)` | **0.5** |
| Contains (base) | **0.5** |

Stem = `Path.GetFileNameWithoutExtension(name)`, por lo que `"report"` puntúa 1.0 contra `"report.pdf"`.

**Query multi-token** (ej. `"xls calc mis"`): la plataforma construye un predicado AND en Spotlight/Windows Search/locate, de modo que solo llegan ficheros que contienen todos los tokens. El scoring es:

| Condición | Score |
|---|---|
| Todos los tokens son prefijo de algún segmento del nombre (split por espacios/guiones/puntos) | **0.75** |
| Todos presentes pero alguno solo como substring | **0.5** |
| Algún token no aparece en el nombre (safety net) | descartado |

Ejemplo: `"xls calc mis"` → `"mis calculos.xls"`: segmentos `["mis","calculos","xls"]`; "mis"→"mis"✓, "calc"→"calculos"✓, "xls"→"xls"✓ → score 0.75.

No hay bonus por ser directorio (solo afecta icono y categoría).

**Snapshots progresivos**: `UserDocumentSearch` emite un snapshot máximo cada 200ms (throttling por tiempo, `SnapshotIntervalMs`) y uno final al terminar o cancelar. Esto evita que queries con muchos resultados (p.ej. "a") saturen la UI con decenas de actualizaciones por segundo.
