# CLAUDE.md

Yottacast is a macOS/Windows app launcher — similar to Spotlight or PowerToys Run.
It's a frameless, transparent dark-themed window where the user types to search and uses arrow keys + Enter to launch items.

Update the CLAUDE.md when something non-obvious is worth keeping in mind for later.

Estructura

Yottacast/
├── Views/
│   ├── MainWindow.axaml          ← UI: ventana sin bordes, dark
│   └── MainWindow.axaml.cs       ← Teclado: ESC, ↑↓, Enter
├── ViewModels/
│   ├── MainWindowViewModel.cs    ← Búsqueda reactiva + filtrado
│   ├── ResultItemViewModel.cs    ← Modelo de cada resultado
│   └── ViewModelBase.cs
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
       

## Fixes/Features

Al arrancar, se registra ALT+Espacio. 
Al mostrar, se hace trim() para evitar que se añada un espacio al haber pulsado ALT+Espacio
Con texto, ESC limpia el texto solo. Sin texto, ESC cierra
