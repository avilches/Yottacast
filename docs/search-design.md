# Diseño de búsqueda

## Arranque (App.axaml.cs)

`App.OnFrameworkInitializationCompleted` es síncrono. Orden de arranque:

1. `BuildServices()` — construye el contenedor DI
2. `ThemeService.Apply(...)` + creación de `MainWindow` — la ventana aparece inmediatamente
3. `base.OnFrameworkInitializationCompleted()` — señala a Avalonia que la inicialización terminó
4. `_ = globalSearch.Start()` — fire-and-forget; el arranque **no espera** a que termine

El arranque no bloquea en el paso 4. La ventana ya es interactiva y el usuario puede escribir mientras `Start()` trabaja en segundo plano.

**Qué hace `globalSearch.Start()`** — delega en cada `ISearchSource`:

- **`ApplicationSearch.Start()`** — la única con trabajo real. Llama `ScanAndWatchAsync()` como fire-and-forget, que:
  1. `await platform.ScanAppsAsync(...)` — escaneo inicial (macOS: mdfind; Windows/Linux: scan de directorios)
  2. Completa la task `WhenReady()` al terminar el scan
  3. Instala `FileSystemWatcher`s vía `platform.CreateAppWatchers(...)`
- **`UserDocumentSearch.Start()`** — no-op. `WhenReady()` retorna `Task.CompletedTask` inmediatamente.

**`WhenReady()`** — `ISearchSource` expone `Task WhenReady()` que se completa cuando el scan inicial ha terminado y el caché está poblado. `GlobalSearch.WhenReady()` hace `Task.WhenAll` sobre todos los sources.

**Consecuencia para búsquedas**: hasta que `WhenReady()` complete en macOS, `SearchAsync` de `ApplicationSearch` devuelve vacío. La UI es interactiva desde el arranque; simplemente no hay apps en los resultados hasta que mdfind acaba.

**Consecuencia para Settings**: `App.OpenSettings()` es `async void` y hace `await applicationSearch.WhenReady()` antes de crear `SettingsWindowViewModel`. Esto garantiza que `BrowserDiscovery.Discover()` y `TerminalDiscovery.Discover()` (llamados en el constructor del ViewModel) ya tienen el caché poblado. Si el caché ya está listo (usuario abre Settings tarde), el await es instantáneo.

`UserSettings.Load(platform)` carga (o crea) el JSON y siempre hace `Save()` al final. La validación de Browser/Terminal no ocurre en el arranque; `UserSettings` se auto-repara en el momento de uso, cuando se accede a `ActiveBrowser` / `ActiveTerminal`.

## Servicios registrados en DI

- `PlatformProvider` (singleton, instancia concreta elegida en `BuildServices()` con una única comprobación de OS)
- `UserSettings` (singleton, cargado con `UserSettings.Load(platform)`)
- `ApplicationSearch` (singleton, ISearchSource)
- `UserDocumentSearch` (singleton, ISearchSource)
- `GlobalSearch` (singleton, recibe `IEnumerable<ISearchSource>`)
- `BrowserDiscovery`, `TerminalDiscovery`, `FileSearch` (singleton)
- `MainWindowViewModel`, `SettingsWindowViewModel` (transient)

## Motor de búsqueda: GlobalSearch

Clase: `Yottacast.Core.Search.GlobalSearch`

Agrega múltiples `ISearchSource` recibidas por inyección. `SearchAsync` devuelve `IAsyncEnumerable<IReadOnlyList<ResultItemViewModel>>` — cada emisión es un snapshot completo (los mejores N resultados hasta ese momento). Cada fuente "posee" un slot; cuando emite un nuevo snapshot, el slot se actualiza y GlobalSearch emite la unión ordenada de todos los slots.

```
ISearchSource
├── ApplicationSearch    ← apps instaladas (desde caché en memoria)
└── UserDocumentSearch   ← documentos (delega en FileSearch, streaming via Channel)
```

Para añadir una nueva fuente: implementar `ISearchSource` y registrarla en `BuildServices` como `services.AddSingleton<ISearchSource>(...)`.

## Debounce (MainWindowViewModel)

```
OnSearchTextChanged → cancela CTS anterior
  → Phase 1 (instant): SearchInstantAsync inmediato → actualiza _instantSnapshot → RefreshResults()
  → espera 250ms
  → Phase 2 (deferred): SearchDeferredAsync → cada snapshot actualiza _deferredSnapshot → RefreshResults()
```

`RefreshResults()` reconstruye `Results` fusionando `[googleItem] + _instantSnapshot + _deferredSnapshot`, ordenados por score descendente. Preserva la selección actual si el item sigue en la lista.

## Arquitectura snapshot-por-fuente

`ISearchSource.SearchAsync` devuelve `IAsyncEnumerable<IReadOnlyList<ResultItemViewModel>>`: cada yield es un snapshot completo (los mejores N ordenados), no un item individual. Esto permite **reemplazar** en lugar de **acumular**:

- `ApplicationSearch` → emite un único snapshot con todas las apps coincidentes
- `UserDocumentSearch` → emite snapshots progresivos con throttling por tiempo (`SnapshotIntervalMs=200ms`) y uno final
- `GlobalSearch` → mantiene un array `snapshots[sourceIndex]`; cada nuevo snapshot reemplaza su slot y se emite la unión ordenada
- `MainWindowViewModel` → mantiene `_instantSnapshot` y `_deferredSnapshot`; `RefreshResults()` los fusiona en cada actualización
