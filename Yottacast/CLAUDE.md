# CLAUDE.md

Yottacast is a macOS/Windows app launcher — similar to Spotlight or PowerToys Run.
It's a frameless, transparent dark-themed window where the user types to search and uses arrow keys + Enter to launch items.

Update the CLAUDE.md when something non-obvious is worth keeping in mind for later.

Estructura

Yottacast/
├── Views/
│   ├── MainWindow.axaml          ← UI: ventana sin bordes, dark
│   ├── MainWindow.axaml.cs       ← Teclado: ESC, ↑↓, Enter, ⌘,
│   ├── SettingsWindow.axaml      ← Ventana de preferencias (decorada, no frameless)
│   └── SettingsWindow.axaml.cs
├── ViewModels/
│   ├── MainWindowViewModel.cs    ← Búsqueda reactiva + filtrado
│   ├── ResultItemViewModel.cs    ← Modelo de cada resultado
│   ├── SettingsWindowViewModel.cs ← Browser, terminal, theme pickers
│   └── ViewModelBase.cs
├── Services/
│   ├── BrowserDiscovery.cs       ← Detecta navegadores instalados → List<BrowserInfo>
│   ├── TerminalDiscovery.cs      ← Detecta terminales instalados  → List<TerminalInfo>
│   ├── BrowserLauncher.cs        ← Abre una URL en un navegador concreto
│   ├── TerminalLauncher.cs       ← Ejecuta un comando en un terminal concreto
│   ├── FileSearch.cs             ← Búsqueda de archivos via Spotlight / Windows Search / locate
│   ├── ThemeService.cs           ← Aplica tema JSON en runtime (colores, fuentes, radios)
│   └── UserSettings.cs           ← Configuración de usuario persistida en JSON
└── App.axaml                     ← Tema Dark forzado

## Build & Run

```bash
dotnet run
dotnet publish -c Release -r osx-arm64 --self-contained
```

## SharpHook (global hotkey)

En v7 los tipos están en `SharpHook.Data`, no en `SharpHook.Native` (que era v5). `ModifierMask` se llama `EventMask`.

```csharp
using SharpHook;       // TaskPoolGlobalHook
using SharpHook.Data;  // KeyCode, EventMask
```

## Gotchas

- **No `BoxShadow` on the root Border** — Avalonia renders it as a rectangle regardless of `CornerRadius`. macOS provides the native rounded shadow automatically via the transparent frameless window.
- **Compiled bindings** are enabled globally (`AvaloniaUseCompiledBindingsByDefault=true`) — bindings must be type-resolvable at compile time.
- **`DataAnnotationsValidationPlugin`** is disabled in `App.axaml.cs` to avoid conflicts with CommunityToolkit.Mvvm validation.
- **Window hide vs close** — la ventana usa `Hide()` en Escape (no `Close()`) para poder restaurarla con el hotkey global. `Show()` + `Activate()` la devuelve.
       

## Services

### BrowserDiscovery / TerminalDiscovery
Buscan en `/Applications` y `~/Applications` (macOS) o rutas de `Program Files` (Windows) contra una lista de nombres conocidos. No usan ninguna API de sistema, solo comprueba si existe el `.app` o el `.exe`.

### BrowserLauncher
macOS: `open -a "Nombre" "url"`. Windows: lanza el `.exe` con la URL como argumento.

### TerminalLauncher
macOS varía por terminal:
- **Terminal.app** → AppleScript `do script`
- **iTerm** → AppleScript `create window with default profile command`
- **Warp** → URL scheme `warp://action/new_tab?command=...`
- **Resto** → genera `.command` temporal con `chmod +x` y lo abre con `open -a`

Windows: PowerShell usa `-NoExit -Command`, CMD usa `/K`.

### FileSearch
Busca ficheros usando el índice nativo del SO:
- **macOS** → `mdfind -name` (Spotlight), scope limitado a `$HOME` por defecto
- **Windows** → PowerShell + ADODB.Connection contra `Provider=Search.CollatorDSO` (Windows Search Index)
- **Linux** → `plocate` (si existe en `/usr/bin/plocate`) o `locate -b`

API: `await FileSearch.SearchAsync(query, maxResults, ct)` → `IReadOnlyList<FileResult>` con `.Name` y `.Path`.
Errores de proceso se silencian (devuelve lista vacía) para no romper el launcher.

### ThemeService
Lee `Themes/settings.json` → nombre del tema → carga `Themes/{name}.json` y aplica colores, fuentes y `CornerRadius` directamente en `Application.Current.Resources` en runtime. Los tokens siguen el patrón `Theme.*` (ej. `Theme.WindowBackground`).

Para cambiar de tema: editar `Themes/settings.json` → `{ "theme": "dark-raycast" }`.
Temas disponibles: `dark-default`, `dark-raycast`, `dark-macos`, `light-blue`, `light-gray`.
`MainWindow.axaml` usa `{DynamicResource Theme.X}` — ningún color/tamaño está hardcodeado.
Colores: `#AARRGGBB`. Para transparencia usar `#20FFFFFF` (no `#FFFFFF20`).
Los JSON se copian al output via `CopyToOutputDirectory=PreserveNewest` en el `.csproj`.

### Gotcha: implicit usings desactivados
El proyecto no tiene `<ImplicitUsings>enable</ImplicitUsings>` en el .csproj — hay que añadir todos los `using System.*` manualmente en cada archivo.

### Gotcha: raw string literals con variables PowerShell
Usar `$$"""..."""` en lugar de `$"""..."""` cuando el contenido tiene variables PowerShell (`$var`). Con `$$`, la sintaxis de interpolación C# pasa a ser `{{expr}}` y los `$` sueltos son literales.

### UserSettings
Persiste browser preferido, terminal preferido y tema en:
- macOS: `~/Library/Application Support/Yottacast/settings.json`
- Windows: `%APPDATA%\Yottacast\settings.json`

API: `UserSettings.Load()` → instancia. `settings.Save()` guarda cambios.
`App.axaml.cs` carga la instancia al inicio y la pasa a `MainWindowViewModel` y `SettingsWindowViewModel`.
Las preferencias se guardan automáticamente al cambiar cada campo en el SettingsWindow.

`ThemeService.Apply(string themeName)` ya no lee `Themes/settings.json` — recibe el nombre directamente.
`ThemeService.ApplyBuiltinDefault()` aplica hardcoded dark-default por si el JSON de tema falla o no existe.

⌘, abre el SettingsWindow (si la MainWindow está visible). La instancia se reutiliza — si ya está abierta, la trae al frente.

## Fixes/Features

Al arrancar, se registra ALT+Espacio.
Al mostrar, se hace trim() para evitar que se añada un espacio al haber pulsado ALT+Espacio
Con texto, ESC limpia el texto solo. Sin texto, ESC cierra
