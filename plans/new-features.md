# Plan de nuevas funcionalidades — Yottacast

Análisis basado en el código actual a marzo 2026. Cada feature indica qué existe aprovechable, qué hay que crear, complejidad y boceto técnico.

---

## 1. Acciones contextuales sobre resultados

**Prioridad: Alta**
**Complejidad: Media**

### Qué falta

En Alfred/Raycast, pulsar `Tab` o `Cmd+K` sobre un resultado abre un panel de acciones secundarias: "Abrir con…", "Copiar path", "Mostrar en Finder", "Mover a la papelera", "Copiar nombre de fichero", etc. Ahora mismo cada `ResultItemViewModel` tiene un único `OnActivate`.

### Código aprovechable

- `ResultItemViewModel` ya tiene `OnActivate Action?`. El modelo solo necesita un segundo campo.
- `PlatformProvider` ya expone `LaunchApp(path)` — "Mostrar en Finder" es solo `LaunchApp("/System/Library/CoreServices/Finder.app")` con un argumento de ruta, o un método nuevo `RevealInFinder(path)`.
- `ClipboardService.CopyText` está disponible desde Core sin depender de Avalonia.
- `AppHandler.Instance.CloseWindowShortcut` y la detección de teclado en `MainWindow.axaml.cs` ya muestran cómo añadir shortcuts de teclado.

### Ficheros nuevos

- `Yottacast.Core/ViewModels/ResultAction.cs` — `record ResultAction(string Label, string Icon, Action Execute)`.

### Ficheros modificados

- `Yottacast.Core/ViewModels/ResultItemViewModel.cs` — añadir `IReadOnlyList<ResultAction> Actions { get; init; } = []`.
- `Yottacast.Core/Platform/PlatformProvider.cs` — añadir `abstract void RevealInFileManager(string path)`.
- `Yottacast.Core/Platform/MacOsPlatformProvider.cs` — implementar con `open -R <path>`.
- `Yottacast.Core/Platform/WindowsPlatformProvider.cs` — implementar con `explorer /select,<path>`.
- `ApplicationSearch.cs` y `UserDocumentSearch.cs` — poblar `Actions` con las acciones estándar al construir cada `ResultItemViewModel`.
- `MainWindow.axaml.cs` — interceptar `Tab` o `Cmd+K` para mostrar panel de acciones.
- `MainWindow.axaml` — panel lateral o popup de acciones que se muestra al activar el atajo.
- `MainWindowViewModel.cs` — `ObservableCollection<ResultAction> ContextActions` y lógica para popularlo cuando `SelectedResult` cambia.

### Acciones estándar por categoría

| Categoría | Acciones |
|---|---|
| Applications | Abrir (default), Mostrar en Finder, Copiar path |
| Files | Abrir (default), Abrir con…, Mostrar en Finder, Copiar path, Copiar nombre |
| Web | Abrir en browser (default), Copiar URL |
| Calculator/Converter | Copiar resultado (default) |

### Boceto técnico (teclado)

En `MainWindow.axaml.cs`, en el switch de `OnKeyDown`:

```csharp
case Key.Tab:
    vm.ToggleContextActions();
    e.Handled = true;
    break;
```

`ToggleContextActions()` en el ViewModel: si `ContextActions` está vacío, lo popula con `SelectedResult?.Actions`; si ya está visible, lo cierra.

---

## 2. Búsqueda web con prefijos configurables

**Prioridad: Alta**
**Complejidad: Baja**

### Qué falta

Raycast y Alfred permiten `gh pulsar/entrar` para buscar en GitHub, `yt gatos` para YouTube, `mdn Array.map` para MDN, etc. Yottacast ya tiene Google hardcoded en `MainWindowViewModel.MakeGoogleItem`, pero no hay sistema de prefijos extensible.

### Código aprovechable

- `MakeGoogleItem` en `MainWindowViewModel` es exactamente el patrón a generalizar — ya construye un `ResultItemViewModel` con `BrowserDiscovery.OpenUrl`.
- `BrowserDiscovery.OpenUrl(url, browser)` y `UserSettings.ActiveBrowser` están disponibles.
- `EmojiSearch` demuestra el patrón de fuente activada por prefijo (`:emoji`).
- `UserSettings` ya serializa listas; puede añadir `List<WebSearch>` con los buscadores configurados.

### Ficheros nuevos

- `Yottacast.Core/Search/WebSearch.cs` — `ISearchSource` de tipo `IsInstant=true`. Carga la lista de buscadores de `UserSettings`. Ante cualquier query que coincida con un prefijo configurado (p.ej. `"gh "` → búsqueda en GitHub), o ante cualquier query que no parezca un path ni una expresión matemática, emite un resultado por buscador relevante.

### Ficheros modificados

- `Yottacast.Core/Services/UserSettings.cs` — añadir `List<WebSearchConfig> WebSearches { get; set; }` con una lista por defecto que incluya Google, GitHub, YouTube, DuckDuckGo, MDN.
- `UserSettingsData` record interno — añadir el campo correspondiente.
- `MainWindowViewModel.cs` — eliminar `MakeGoogleItem` y `_googleItem`; Google pasa a ser un ítem más de `WebSearchSource` con score alto.
- `App.axaml.cs` `BuildServices()` — registrar `WebSearch` como `ISearchSource`.
- `Yottacast/Views/SettingsWindow.axaml` — UI para añadir/editar/eliminar buscadores web.

### Boceto técnico

```csharp
// UserSettings.cs
public record WebSearchConfig(string Name, string Icon, string Prefix, string UrlTemplate);
// UrlTemplate ejemplo: "https://github.com/search?q={query}"

// WebSearch.cs
public class WebSearch(UserSettings settings, BrowserDiscovery browserDiscovery) : ISearchSource {
    public bool IsInstant => true;
    // ...
    public async IAsyncEnumerable<...> SearchAsync(string query, int limit, CancellationToken ct) {
        var q = query.Trim();
        var results = new List<ResultItemViewModel>();
        foreach (var ws in settings.WebSearches) {
            string? searchTerm = null;
            if (!string.IsNullOrEmpty(ws.Prefix) && q.StartsWith(ws.Prefix, StringComparison.OrdinalIgnoreCase))
                searchTerm = q[ws.Prefix.Length..].Trim();
            else if (string.IsNullOrEmpty(ws.Prefix)) // buscadores sin prefijo (Google) siempre aparecen
                searchTerm = q;
            if (searchTerm is null || searchTerm.Length == 0) continue;
            var captured = searchTerm; var capturedWs = ws;
            results.Add(new ResultItemViewModel {
                Icon = ws.Icon, Title = $"Search \"{captured}\" on {ws.Name}",
                Subtitle = ws.UrlTemplate.Replace("{query}", Uri.EscapeDataString(captured)),
                Category = "Web", Score = ws.Name == "Google" ? 3 : 2.5,
                OnActivate = () => {
                    var browser = settings.ActiveBrowser; if (browser is null) return;
                    browserDiscovery.OpenUrl(capturedWs.UrlTemplate.Replace("{query}", Uri.EscapeDataString(captured)), browser);
                },
            });
        }
        if (results.Count > 0) yield return results;
        await Task.CompletedTask;
    }
}
```

Buscadores por defecto:

```json
[
  { "name": "Google",  "icon": "🔍", "prefix": "",     "urlTemplate": "https://www.google.com/search?q={query}" },
  { "name": "GitHub",  "icon": "",  "prefix": "gh ",  "urlTemplate": "https://github.com/search?q={query}" },
  { "name": "YouTube", "icon": "▶",  "prefix": "yt ",  "urlTemplate": "https://www.youtube.com/results?search_query={query}" },
  { "name": "MDN",     "icon": "📖", "prefix": "mdn ", "urlTemplate": "https://developer.mozilla.org/search?q={query}" }
]
```

---

## 3. Snippets y portapapeles con historial

**Prioridad: Media**
**Complejidad: Media**

### Qué falta

Alfred tiene "Snippets" (textos predefinidos con atajo) y "Clipboard History" (historial de lo que has copiado). Son dos features distintas con intersección técnica.

### Código aprovechable

- `ClipboardService` ya tiene la infraestructura `Initialize(Action<string>)` para leer/escribir portapapeles desde Core. Solo tiene `CopyText`; habría que añadir lectura.
- `EmojiSearch` ya demuestra el patrón `PasteAfterActivate = true` — el launcher se oculta y simula Cmd+V. Los snippets y el historial de portapapeles usarían exactamente este mecanismo.
- `App.axaml.cs` ya tiene el wire-up de `ClipboardService` con el callback de UI thread. Ampliar ese callback para incluir lectura es trivial.
- `LaunchHistory` (feature 1) demuestra el patrón de persistencia JSON en el directorio de datos.

### Ficheros nuevos

- `Yottacast.Core/Services/ClipboardHistory.cs` — singleton. Mantiene una lista circular de las últimas N entradas copiadas (texto, timestamp). Persiste en JSON. Expone `IReadOnlyList<ClipboardEntry> Entries`.
- `Yottacast.Core/Services/SnippetStore.cs` — singleton. Lee/escribe `List<Snippet>` de un JSON. `Snippet` tiene `Name`, `Keyword`, `Content`, `Category`.
- `Yottacast.Core/Search/ClipboardSearch.cs` — `ISearchSource` `IsInstant=true`. Activada por prefijo `"cb "` o shortcut dedicado. Busca en `ClipboardHistory.Entries` por contenido.
- `Yottacast.Core/Search/SnippetSearch.cs` — `ISearchSource` `IsInstant=true`. Activada por prefijo `"snip "` o directamente si el query coincide con un `Keyword`. Activa con `PasteAfterActivate = true`.

### Ficheros modificados

- `Yottacast.Core/Services/ClipboardService.cs` — añadir `Func<Task<string?>>? _read` e `Initialize` que también acepte el getter.
- `App.axaml.cs` — ampliar el wire-up para pasar también el getter del clipboard Avalonia; instalar un `DispatcherTimer` o suscripción para polling del portapapeles del sistema cada ~500ms y llamar `ClipboardHistory.Record(text)`.
- `App.axaml.cs` `BuildServices()` — registrar `ClipboardHistory`, `SnippetStore`, `ClipboardSearch`, `SnippetSearch` como `ISearchSource`.
- `Yottacast/Views/SettingsWindow.axaml` — UI CRUD de snippets.

### Gotcha: polling del portapapeles del sistema

Avalonia no expone un evento de cambio de portapapeles. El mecanismo estándar en launchers es un timer que lee el portapapeles cada ~500ms y detecta si cambió. En macOS existe `NSPasteboard.changeCount` para detectar cambios sin leer el contenido completo; esto va en `MacAppHandler` ya que depende de Avalonia/UI layer. La lógica de almacenamiento permanece en Core (`ClipboardHistory`).

---

## 4. Comandos de sistema

**Prioridad: Media**
**Complejidad: Baja**

### Qué falta

Alfred y Raycast permiten escribir "sleep", "lock", "empty trash", "restart", "shutdown", "logout" directamente en el launcher. Son acciones de sistema que no requieren abrir ninguna app.

### Código aprovechable

- El patrón `ISearchSource` `IsInstant=true` es exactamente el que necesita esta feature — las acciones son un listado fijo en memoria, filtrado por `NameMatcher.Score`.
- `PlatformProvider` ya centraliza el código OS-específico. Las acciones de sistema se implementan ahí.
- `StandardCommandRunner` puede ejecutar comandos de shell (`pmset sleepnow`, `loginwindow`, etc.) sin lógica adicional.
- `AppHandler.OnHide()` ya se llama al ejecutar cualquier acción — los comandos de sistema que ocultan el launcher funcionan automáticamente.

### Ficheros nuevos

- `Yottacast.Core/Search/SystemCommandSearch.cs` — `ISearchSource` `IsInstant=true`. Lista estática de comandos por plataforma, filtrada por `NameMatcher`. Cada comando tiene `Title`, `Subtitle`, `Icon`, `Action`.

### Ficheros modificados

- `Yottacast.Core/Platform/PlatformProvider.cs` — añadir métodos abstractos: `void Sleep()`, `void Lock()`, `void EmptyTrash()`, `void Restart()`, `void Shutdown()`, `void Logout()`.
- `MacOsPlatformProvider.cs` — implementar con `pmset sleepnow`, `"/System/Library/CoreServices/Menu Extras/User.menu/Contents/Resources/CGSession" -suspend`, `osascript -e 'tell application "Finder" to empty trash'`, etc.
- `WindowsPlatformProvider.cs` — implementar con `rundll32.exe user32.dll,LockWorkStation`, `shutdown /s /t 0`, etc.
- `App.axaml.cs` `BuildServices()` — registrar `SystemCommandSearch` como `ISearchSource`.

### Boceto técnico

```csharp
// SystemCommandSearch.cs
public class SystemCommandSearch(PlatformProvider platform) : ISearchSource {
    public bool IsInstant => true;
    // ...
    private IReadOnlyList<ResultItemViewModel> BuildCommands() => [
        Cmd("Sleep",        "💤", "Put the computer to sleep",    platform.Sleep),
        Cmd("Lock Screen",  "🔒", "Lock the screen",              platform.Lock),
        Cmd("Empty Trash",  "🗑",  "Empty the trash",              platform.EmptyTrash),
        Cmd("Restart",      "🔄", "Restart the computer",         platform.Restart),
        Cmd("Shutdown",     "⏻",  "Shut down the computer",       platform.Shutdown),
        Cmd("Log Out",      "🚪", "Log out of the current user",  platform.Logout),
    ];

    private static ResultItemViewModel Cmd(string title, string icon, string subtitle, Action action) =>
        new() { Title = title, Icon = icon, Subtitle = subtitle, Category = "System", Score = 0, OnActivate = action };

    public async IAsyncEnumerable<...> SearchAsync(string query, int limit, CancellationToken ct) {
        var results = BuildCommands()
            .Select(c => (cmd: c, score: NameMatcher.Score(c.Title, query)))
            .Where(x => x.score > 0)
            .OrderByDescending(x => x.score)
            .Take(limit)
            .Select(x => x.cmd with { Score = x.score * 2 }) // boost para que compitan con apps
            .ToList();
        if (results.Count > 0) yield return results;
        await Task.CompletedTask;
    }
}
```

---

## 5. Preview de resultados

**Prioridad: Media**
**Complejidad: Alta**

### Qué falta

Raycast muestra un panel lateral con preview del resultado seleccionado: para ficheros muestra Quick Look, para apps muestra metadatos, para resultados de calculadora muestra el resultado ampliado. Es pura UI, sin cambios en la pipeline de búsqueda.

### Código aprovechable

- `ResultItemViewModel` ya tiene `Title`, `Subtitle`, `Category`, `Icon`. Un preview básico puede construirse con solo esos campos sin tocar el modelo.
- `AppInfo.IconPath` (lazy) ya está disponible para mostrar el icono real de la app en el preview.
- La arquitectura de `MainWindow` es un `Window` Avalonia sin decoraciones — el panel puede ser un segundo `Border` que aparece a la derecha cuando hay un resultado seleccionado.
- `AppHandler.Instance` ya gestiona el foco — el preview no necesita cambios ahí.

### Ficheros nuevos

- `Yottacast.Core/ViewModels/PreviewViewModel.cs` — VM observable con `Title`, `IconPath`, `Description`, `MetadataLines` (lista de pares clave/valor), `PreviewImagePath?`.

### Ficheros modificados

- `Yottacast.Core/ViewModels/ResultItemViewModel.cs` — añadir `Func<PreviewViewModel>? BuildPreview`. Lazy para no computar metadatos innecesariamente.
- `ApplicationSearch.cs` — poblar `BuildPreview` retornando nombre, path, icon, versión (leída del `Info.plist` si macOS).
- `UserDocumentSearch.cs` — poblar `BuildPreview` con nombre de fichero, ruta, tamaño, fecha de modificación.
- `MainWindowViewModel.cs` — añadir `PreviewViewModel? Preview` observable; actualizar cuando `SelectedResult` cambia: `Preview = SelectedResult?.BuildPreview?.Invoke()`.
- `MainWindow.axaml` — panel de preview a la derecha, visible solo cuando `Preview != null`. Binding a `DataContext.Preview`. Toggle con `Cmd+P` o aparición automática con un delay.
- `Yottacast/Views/SettingsWindow.axaml` — opción "Show preview panel" en Settings.

### Consideraciones de complejidad

El preview de ficheros con Quick Look en macOS requiere P/Invoke a `QLPreviewPanel` (similar a lo que ya se hace en `MacAppHandler` con ObjC). Para el MVP puede limitarse a metadatos de texto. El preview de imágenes puede usar `Avalonia.Controls.Image` con la ruta del fichero directamente.

La ventana frameless actual tiene ancho fijo — habría que hacerla elástica o crear un segundo panel flotante anclado a la ventana principal.

---

## 6. Búsqueda en contenido de archivos

**Prioridad: Baja**
**Complejidad: Media**

### Qué falta

Alfred y Raycast permiten buscar dentro del contenido de PDFs, documentos Word, código fuente, etc. Yottacast busca solo por nombre de fichero.

### Código aprovechable

- `UserDocumentSearch` ya maneja el streaming de resultados y el patrón de snapshots progresivos. La búsqueda en contenido puede ser una segunda fuente que usa el mismo channel y throttling.
- `PlatformProvider.SearchFilesAsync` en macOS ya usa `mdfind` (Spotlight), que indexa contenido por defecto. El query `mdfind "contenido"` ya devuelve ficheros que contienen ese texto. El problema es que `FileSearch` actualmente construye el query por nombre (`kMDItemDisplayName == "*query*"cd`).
- `StandardCommandRunner` puede ejecutar `grep -r` o `rg` para búsqueda de contenido en directorios configurados.

### Ficheros nuevos

- `Yottacast.Core/Search/FileContentSearch.cs` — `ISearchSource` `IsInstant=false`. Activada por prefijo `"in:"` (ej. `"in:función async"`). Usa `PlatformProvider.SearchFilesByContentAsync`.

### Ficheros modificados

- `Yottacast.Core/Platform/PlatformProvider.cs` — añadir `abstract Task SearchFilesByContentAsync(string query, Action<FileResult> onResult, IReadOnlyList<string>? folders, CancellationToken ct)`.
- `MacOsPlatformProvider.cs` — implementar usando `mdfind` con query de contenido (`mdfind -onlyin <folder> "<term>"`). Spotlight indexa contenido de PDF, Pages, Word, código.
- `WindowsPlatformProvider.cs` — implementar con Windows Search IFilter o `findstr`.
- `LinuxPlatformProvider.cs` — implementar con `grep -r` o `rg` si está disponible.
- `App.axaml.cs` `BuildServices()` — registrar `FileContentSearch` como `ISearchSource`.

### Boceto técnico

```csharp
// FileContentSearch.cs — activado con prefijo "in:"
public class FileContentSearch(UserSettings settings, PlatformProvider platform) : ISearchSource {
    public bool IsInstant => false;
    // SearchAsync: si query no empieza por "in:", yield break
    // Si empieza: extrae el término, llama platform.SearchFilesByContentAsync
    // Reutiliza el mismo patrón de channel + throttling de UserDocumentSearch
}
```

La activación por prefijo `"in:"` evita penalizar a las búsquedas normales de nombre. Podría combinarse con un toggle en la UI (p.ej. `Cmd+F` mientras escribe).

---

## 7. Scripts y extensiones con Jint

**Prioridad: Baja**
**Complejidad: Alta**

### Qué falta

Alfred tiene "Workflows" y Raycast tiene "Extensions". Yottacast ya tiene Jint (el mismo motor JS que usa `MathJsEngine`) — la infraestructura de scripting está a medias.

### Código aprovechable

- `MathJsEngine` demuestra que Jint funciona, está thread-safe con `lock`, y puede cargar recursos embebidos. El mismo patrón sirve para ejecutar scripts de usuario.
- `ISearchSource` es la interfaz natural para exponer extensiones como fuentes de búsqueda.
- `ResultItemViewModel.OnActivate` ya soporta cualquier `Action` — un script puede emitir resultados con acciones que ejecuten más JS.
- `ClipboardService`, `BrowserDiscovery`, `TerminalDiscovery` pueden exponerse al contexto JS como un API bridge.
- `StandardCommandRunner` puede exponerse para que los scripts lancen procesos externos.

### Ficheros nuevos

- `Yottacast.Core/Scripts/ScriptExtension.cs` — representa un script cargado: nombre, prefijo/trigger, fichero JS.
- `Yottacast.Core/Services/ExtensionLoader.cs` — escanea `~/.config/Yottacast/extensions/` buscando manifiestos `extension.json` con `name`, `prefix`, `script`.
- `Yottacast.Core/Search/ExtensionSearch.cs` — `ISearchSource` `IsInstant=false` (los scripts son código arbitrario). Para cada extensión instalada cuyo `prefix` coincida, ejecuta el script JS en Jint pasándole el query y recoge los resultados.
- `Yottacast.Core/Scripts/YottacastApi.cs` — objeto bridge expuesto al contexto JS: `yottacast.clipboard.copy(text)`, `yottacast.browser.open(url)`, `yottacast.shell.run(cmd)`.

### Ficheros modificados

- `App.axaml.cs` `BuildServices()` — registrar `ExtensionLoader` y `ExtensionSearch` como `ISearchSource`.
- `Yottacast.Core/Platform/PlatformProvider.cs` — si se quiere que scripts puedan buscar apps o ficheros, exponer un método de consulta directa al caché de `ApplicationSearch`.

### Boceto técnico

```json
// ~/.config/Yottacast/extensions/my-extension/extension.json
{
  "name": "My Extension",
  "prefix": "myext ",
  "script": "index.js"
}
```

```javascript
// index.js — API expuesta por YottacastApi
function search(query) {
  return [
    { title: "Result for " + query, subtitle: "My extension", icon: "⚡", onActivate: "open" },
  ];
}
function activate(action, item) {
  if (action === "open") yottacast.browser.open("https://example.com?q=" + item.title);
}
```

### Consideraciones de complejidad

Jint no tiene sandboxing de I/O — un script puede leer ficheros del disco. Esto es aceptable en un launcher personal (como Alfred Workflows). La complejidad real está en definir el API bridge de forma estable y en el manejo de errores (timeouts, excepciones JS). El timeout puede reutilizar el mismo `CancelAfter` que `UserDocumentSearch`.

---

## Resumen de prioridades

| # | Feature | Prioridad | Complejidad | Ficheros Core nuevos | Ficheros modificados principales |
|---|---|---|---|---|---|
| 1 | Acciones contextuales | Alta | Media | `ResultAction.cs` | `ResultItemViewModel`, `PlatformProvider`, `ApplicationSearch`, `UserDocumentSearch`, `MainWindow.axaml` |
| 2 | Búsqueda web con prefijos | Alta | Baja | `WebSearch.cs` | `UserSettings`, `MainWindowViewModel` (eliminar `MakeGoogleItem`) |
| 3 | Snippets / historial clipboard | Media | Media | `ClipboardHistory.cs`, `SnippetStore.cs`, `ClipboardSearch.cs`, `SnippetSearch.cs` | `ClipboardService`, `App.axaml.cs` |
| 4 | Comandos de sistema | Media | Baja | `SystemCommandSearch.cs` | `PlatformProvider`, `MacOsPlatformProvider`, `WindowsPlatformProvider` |
| 5 | Preview de resultados | Media | Alta | `PreviewViewModel.cs` | `ResultItemViewModel`, `MainWindowViewModel`, `MainWindow.axaml` |
| 6 | Búsqueda en contenido | Baja | Media | `FileContentSearch.cs` | `PlatformProvider` (nuevo método), todas las implementaciones |
| 7 | Scripts / extensiones | Baja | Alta | `ScriptExtension.cs`, `ExtensionLoader.cs`, `ExtensionSearch.cs`, `YottacastApi.cs` | `App.axaml.cs` |

> El boost por frecuencia/recencia (LaunchHistory) se trata en `plans/scoring.md §4`.

### Orden de implementación recomendado

1. **Búsqueda web con prefijos** (2): mínimo cambio de código, máximo impacto UX. Elimina el Google hardcoded.
2. **Comandos de sistema** (4): es básicamente una lista estática + tres métodos en `PlatformProvider`. Un par de horas.
3. **Acciones contextuales** (1): requiere añadir UI nueva pero el modelo de datos es sencillo.
4. **Snippets / clipboard** (3): depende de ampliar `ClipboardService` con lectura.
5. **Preview** (5): el mayor impacto visual, pero requiere decisiones de diseño de UI.
6. **Búsqueda en contenido** (6): en macOS, `mdfind` ya lo hace — el esfuerzo real es el Windows/Linux path.
7. **Scripts** (7): cuando las bases estén sólidas; es la feature que más puede romper si el API no está bien definido.
