# Running Apps + Title Pills

**Fecha:** 2026-05-29

## Objetivo

Mostrar qué aplicaciones están corriendo en los resultados de búsqueda, con indicadores visuales (pills) inline en el título, acciones de gestión (Bring to Front, Quit, Force Quit), y un sistema de tokens de tema que permite a cada tema elegir entre estilo filled u outline para las pills.

---

## 1. Pills visuales

### ViewModel

Se añaden dos propiedades string nullable a `ResultItemViewModel`:

```csharp
public string? RunningTag { get; init; }  // ej. "Running"
public string? InfoTag    { get; init; }  // ej. "from clipboard"
```

- `RunningTag` → pill verde (estilo `running`)
- `InfoTag` → pill azul/info (estilo `info`)
- Ambas son independientes; un item puede tener una, las dos o ninguna.
- `ClipboardSearch` deja de concatenar `"· from clipboard"` al `Title` y usa `InfoTag = "from clipboard"` en su lugar.

### AXAML (`MainWindow.axaml`)

La fila del título pasa de un único `HighlightTextBlock` a un `StackPanel` horizontal:

```
[HighlightTextBlock Title] [Border RunningTag?] [Border InfoTag?]
```

Cada `Border` tiene `IsVisible` enlazado a si la propiedad es not-null. Sus colores apuntan a `DynamicResource` de su estilo respectivo. No se requiere ningún converter para elegir el estilo, ya que cada Border está dedicado a un tipo.

---

## 2. Tokens de tema

### Sección nueva en los JSON: `results.tags`

```json
"results": {
  "tags": {
    "cornerRadius": 4,
    "running": {
      "color":       "#30d158",
      "background":  "rgba(48,209,88,0.14)",
      "borderColor": "Transparent"
    },
    "info": {
      "color":       "#5ac8fa",
      "background":  "rgba(10,132,255,0.13)",
      "borderColor": "Transparent"
    }
  }
}
```

El filled vs outline se expresa con los mismos tokens: filled pone `background` opaco y `borderColor` transparente; outline pone `background` transparente y `borderColor` con color.

### Recursos Avalonia nuevos

| JSON path | Recurso Avalonia |
|---|---|
| `results.tags.cornerRadius` | `Theme.Results.Tag.CornerRadius` |
| `results.tags.running.color` | `Theme.Results.Tag.Running.Color` |
| `results.tags.running.background` | `Theme.Results.Tag.Running.Background` |
| `results.tags.running.borderColor` | `Theme.Results.Tag.Running.BorderColor` |
| `results.tags.info.color` | `Theme.Results.Tag.Info.Color` |
| `results.tags.info.background` | `Theme.Results.Tag.Info.Background` |
| `results.tags.info.borderColor` | `Theme.Results.Tag.Info.BorderColor` |

### Valores por defecto en cada tema

| Token | dark-default (filled) | dark-macos (outline) |
|---|---|---|
| `running.color` | `#30d158` | `#30d158` |
| `running.background` | `#2430D158` (verde ~14% opacidad, formato ARGB) | `Transparent` |
| `running.borderColor` | `Transparent` | `#30d158` (al 70%) |
| `info.color` | `#5ac8fa` | `#0A84FF` |
| `info.background` | `#1A0A84FF` (azul ~10% opacidad) | `Transparent` |
| `info.borderColor` | `Transparent` | `#0A84FF` (al 70%) |
| `cornerRadius` | `4` | `4` |

`ThemeService.Apply()` lee los nuevos tokens y `ApplyBuiltinDefault()` los define con los valores de dark-default. Los demás temas (dark-raycast, light-*) heredan los valores del fallback y pueden optar por definirlos explícitamente.

---

## 3. Detección de apps en running

### Nuevo tipo `RunningAppInfo`

```csharp
public record RunningAppInfo(string Path, int Pid);
```

Vive en `Yottacast.Core/Platform/`.

### Nuevo método en `PlatformProvider`

```csharp
public virtual IReadOnlyList<RunningAppInfo> GetRunningApps() => [];
```

- **macOS** (`MacOsPlatformProvider`): P/Invoke a `NSWorkspace.sharedWorkspace.runningApplications`. Itera el array, extrae `bundlePath` (string) y `processIdentifier` (int) de cada `NSRunningApplication`.
- **Windows** (`WindowsPlatformProvider`): `Process.GetProcesses()`, filtra los que tienen `MainModule?.FileName` no nulo.
- **Linux**: devuelve lista vacía (sin implementación).

**Frecuencia de consulta:** se llama en cada `ApplicationSearch.Search()`. No hay caché ni polling. `NSWorkspace.runningApplications` es una operación O(1) en macOS y no requiere caché.

### Cambios en `ApplicationSearch.Search()`

Al construir cada `ResultItemViewModel`:

1. Se llama `platform.GetRunningApps()` **una sola vez** al inicio de `Search()` y se construye un `Dictionary<string, int>` path → pid.
2. Si el path de la app está en el diccionario:
   - `RunningTag = "Running"`
   - La acción primaria cambia de `"Open"` a `"Bring to Front"` (misma `Execute`, diferente `Label`).
   - Se añaden al menú Tab: `"Bring to Front"` (Enter), `"Quit"`, `"Force Quit"`.
3. Si no está corriendo: comportamiento actual sin cambios.

### Acciones para apps en running

| Acción | Hotkey | ShowInFooter | ShowInMenu | ClosesWindow | Comportamiento |
|---|---|---|---|---|---|
| Bring to Front | Enter | true | true | true | `platform.LaunchApp(path)` (igual que Open) |
| Quit | — | false | true | true | `platform.QuitApp(pid)` |
| Force Quit | — | false | true | true | `platform.ForceQuitApp(pid)` |

### Nuevos métodos en `PlatformProvider`

```csharp
public virtual void QuitApp(int pid) { }
public virtual void ForceQuitApp(int pid) { }
```

- **macOS**: `kill(pid, SIGTERM)` / `kill(pid, SIGKILL)` via P/Invoke a `libc`.
- **Windows**: `Process.GetProcessById(pid).CloseMainWindow()` / `.Kill()`.

---

## 4. Ficheros afectados

| Fichero | Cambio |
|---|---|
| `Yottacast.Core/ViewModels/ResultItemViewModel.cs` | +`RunningTag`, +`InfoTag` |
| `Yottacast.Core/Platform/PlatformProvider.cs` | +`RunningAppInfo`, +`GetRunningApps()`, +`QuitApp()`, +`ForceQuitApp()` |
| `Yottacast.Core/Platform/MacOsPlatformProvider.cs` | Implementa `GetRunningApps`, `QuitApp`, `ForceQuitApp` |
| `Yottacast.Core/Platform/WindowsPlatformProvider.cs` | Implementa `GetRunningApps`, `QuitApp`, `ForceQuitApp` |
| `Yottacast.Core/Search/Application/ApplicationSearch.cs` | Detecta running en `Search()`, ajusta label y acciones |
| `Yottacast.Core/Search/Clipboard/ClipboardSearch.cs` | Usa `InfoTag` en vez de concatenar al `Title` |
| `Yottacast/Views/MainWindow.axaml` | Fila de título → StackPanel horizontal con dos Borders opcionales |
| `Yottacast/Services/ThemeService.cs` | Lee `results.tags.*`, define defaults en `ApplyBuiltinDefault()` |
| `Yottacast/Themes/dark-default.json` | +`results.tags` (filled) |
| `Yottacast/Themes/dark-macos.json` | +`results.tags` (outline) |
| `docs/ui-themes.md` | +tabla de tokens `results.tags.*` |
| `docs/result-viewmodels.md` | +`RunningTag`, +`InfoTag` |

---

## 5. Tests

- `ApplicationSearchTests.cs`: tests que verifican que apps en running reciben `RunningTag = "Running"` y que la acción primaria tiene label `"Bring to Front"`.
- `ApplicationSearchTests.cs`: test que verifica que apps no corriendo tienen `RunningTag = null` y label `"Open"`.
- `FakePlatformProvider.cs` / `TrackingPlatformProvider.cs`: añadir implementación stub de `GetRunningApps()`.

---

## Invariantes

- Un item de app nunca tiene `RunningTag` y `InfoTag` a la vez (las apps no salen de clipboard).
- `GetRunningApps()` nunca lanza excepción — si falla, devuelve lista vacía y las apps se muestran como no corriendo.
- El cambio de label (Open → Bring to Front) no altera el comportamiento de `Execute`.
- Temas que no definan `results.tags` heredan los valores del fallback (dark-default filled).
