# CLAUDE.md

Yottacast is a macOS/Windows app launcher — similar to Spotlight or PowerToys Run.
It's a frameless, transparent dark-themed window where the user types to search and uses arrow keys + Enter to launch items.

**Stack**: Avalonia 11.3.12, .NET 9, CommunityToolkit.Mvvm 8.2.1, SharpHook 7.1.1, Jint 3.1.0 (JS engine).

**Regla de mantenimiento**: describe siempre el estado actual del código. No documentes cambios respecto a versiones anteriores ni migraciones. Si al editar escribes algo como "ahora X en vez de Y", "ya no se usa Z", o "antes se hacía así", reformúlalo para describir solo el comportamiento actual. Los gotchas y precauciones sí se documentan, pero sin referenciar versiones pasadas.

**Regla de documentación**: los ficheros en `docs/` explican diseño, arquitectura y relaciones entre componentes. No duplican constantes concretas, listas completas de rutas, puntuaciones numéricas, patrones regex ni otros detalles de implementación que ya son legibles en el código; en su lugar, señalan dónde viven esos detalles (p. ej. "ver `ClassName.Method`" o "definido en `File.cs`"). Esto evita que la documentación quede desactualizada cuando cambian los valores. Los docs responden "¿cómo funciona esto?" y "¿dónde lo busco?", no "¿cuáles son los valores exactos?".

## Estructura de la solución

```
Yottacast.sln
├── Yottacast/                          ← GUI app (Avalonia, WinExe, net9.0)
├── Yottacast.Core/                     ← Shared library (net9.0, sin UI)
├── Yottacast.Cli/                      ← CLI para testear servicios (Exe, net9.0)
└── Yottacast.Core.Tests/               ← Tests xUnit
```

### Yottacast/ (GUI)

```
├── Views/
│   ├── MainWindow.axaml/.cs            ← Ventana frameless; teclado: ESC, ↑↓, Enter, ⌘,
│   └── SettingsWindow.axaml/.cs        ← Preferencias (decorada, no frameless)
├── ViewModels/
│   ├── MainWindowViewModel.cs          ← Búsqueda con debounce, resultado inmediato Google y calculadora
│   └── SettingsWindowViewModel.cs      ← Browser, terminal, theme pickers, carpetas donde mirar documentos
├── Services/
│   ├── ThemeService.cs                 ← Aplica tema JSON en runtime
│   ├── AppHandler.cs                   ← abstract base: OnStart(), OnShow(), OnHide(); Instance singleton
│   ├── MacAppHandler.cs                ← macOS: política de activación (sin Dock), captura/restaura foco via ObjC P/Invoke
│   ├── WindowsAppHandler.cs            ← Windows
│   └── LinuxAppHandler.cs              ← Linux
├── Themes/
│   ├── dark-default.json / dark-raycast.json / dark-macos.json
│   ├── light-blue.json / light-gray.json
│   └── settings.json                   ← Tema activo: { "theme": "dark-default" }
├── ViewLocator.cs
├── Program.cs                          ← Entry point; llama AppHandler.Instance.OnStart() antes de Avalonia
└── App.axaml / App.axaml.cs            ← DI, hotkey global, singleton SettingsWindow
```

### Yottacast.Core/ (lib compartida)

```
├── Platform/
│   ├── PlatformProvider.cs             ← abstract base: todo el código OS-específico
│   ├── MacOsPlatformProvider.cs        ← implementación macOS
│   ├── WindowsPlatformProvider.cs      ← implementación Windows
│   ├── LinuxPlatformProvider.cs        ← implementación Linux
│   └── SpotlightInterop.cs             ← P/Invoke wrapper CoreServices MDQuery (Spotlight); síncrono, bloquea el hilo llamante
├── Process/
│   ├── StandardCommandRunner.cs        ← public: Process.RedirectStandardOutput; único runner disponible
│   └── ProcessResult.cs                ← (Elapsed, ExitCode, Cancelled, Error?)
├── Search/
│   ├── ISearchSource.cs                ← Interfaz: Start() void, WhenReady() Task, Stop(), SearchAsync → IAsyncEnumerable
│   ├── GlobalSearch.cs                 ← Agrega ISearchSource[], merge streaming vía Channel
│   ├── AppInfo.cs + ApplicationSearch.cs  ← ISearchSource: caché en memoria de apps
│   ├── UserDocumentSearch.cs           ← ISearchSource: delega en FileSearch (streaming)
│   ├── NameMatcher.cs                  ← Lógica de scoring CamelHump/prefix/substring para ApplicationSearch
│   ├── CalculatorSearch.cs             ← ISearchSource instant: evalúa expresiones math y conversiones de unidades vía MathJsEngine
│   └── RandomSearch.cs                 ← ISearchSource fake para tests de la pipeline streaming; emite resultados con delay
├── Services/
│   ├── FileSearch.cs                   ← Instancia que delega en PlatformProvider.SearchFilesAsync
│   ├── UserSettings.cs                 ← Config persistida en JSON
│   ├── BrowserDiscovery.cs             ← Detecta navegadores; OpenUrl() delega en PlatformProvider
│   ├── TerminalDiscovery.cs            ← Detecta terminales; ExecuteCommand() delega en PlatformProvider
│   ├── ClipboardService.cs             ← Bridge Core→Avalonia; Initialize() wired in App.axaml.cs
│   └── MathJsEngine.cs                 ← Singleton: Jint + math.js 11.x (embedded resource); init en background
└── ViewModels/
    ├── ResultItemViewModel.cs           ← (Icon, Title, Subtitle, Category, Score, OnActivate)
    └── ViewModelBase.cs                 ← ObservableObject (CommunityToolkit.Mvvm)
```

### Yottacast.Cli/

CLI interactivo para probar servicios. Comandos: `browsers`, `terminals`, `apps`, `search <query>`, `run <binary> [args]`.

## Build & Run

```bash
# GUI
cd Yottacast && dotnet run
dotnet publish -c Release -r osx-arm64 --self-contained

# CLI (para probar servicios)
cd Yottacast.Cli && dotnet run

# Tests
cd Yottacast.Core.Tests && dotnet test
```

## Documentación detallada

| Fichero | Contenido |
|---|---|
| `docs/search-design.md` | Arranque, DI, GlobalSearch, debounce, arquitectura snapshot |
| `docs/search-sources.md` | ApplicationSearch, UserDocumentSearch, scoring detallado |
| `docs/user-settings.md` | Campos, rutas, auto-reparación Browser/Terminal, EnsureIntegrity |
| `docs/calculator.md` | CalculatorSearch, MathJsEngine, ClipboardService |
| `docs/platform.md` | PlatformProvider, StandardCommandRunner, SharpHook |
| `docs/browser-terminal.md` | BrowserDiscovery, TerminalDiscovery, FileSearch, launch per-app |
| `docs/ui-themes-keyboard.md` | Themes, keyboard shortcuts, IsSearching/spinner |

## Gotchas (Avalonia / transversales)

- **No animar `RenderTransform` con keyframes CSS** — No hay animator registrado para `ITransform`; lanza `InvalidOperationException`. Animar solo propiedades de tipo simple (`double`, `Color`, `Thickness`…). Para indicadores de carga, usar `Opacity` con `PlaybackDirection="Alternate"`. `AutoReverse` no existe en Avalonia — el equivalente es `PlaybackDirection="Alternate"`.
- **No `BoxShadow` en el root Border** — Avalonia lo renderiza como rectángulo independientemente del `CornerRadius`. macOS provee sombra redondeada nativa vía la ventana frameless transparente.
- **Compiled bindings** habilitados globalmente (`AvaloniaUseCompiledBindingsByDefault=true`) — los bindings deben ser type-resolvable en compile time.
- **`DataAnnotationsValidationPlugin`** deshabilitado en `App.axaml.cs` para evitar conflictos con CommunityToolkit.Mvvm.
