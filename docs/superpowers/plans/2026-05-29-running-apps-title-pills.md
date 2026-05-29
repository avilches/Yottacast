# Running Apps + Title Pills — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Mostrar qué apps están corriendo en los resultados con una pill "Running" inline en el título, añadir acciones Bring to Front / Quit / Force Quit, y migrar "from clipboard" de texto concatenado a una pill "Info" con tokens de tema propios.

**Architecture:** Se añaden dos propiedades string nullable (`RunningTag`, `InfoTag`) a `ResultItemViewModel`. `ApplicationSearch.Search()` consulta `PlatformProvider.GetRunningApps()` en cada búsqueda para construir el set de procesos activos. Los temas controlan el estilo de las pills (filled vs outline) mediante nuevos tokens `Theme.Results.Tag.*`. El AXAML renderiza las pills como dos `Border` opcionales después del título.

**Tech Stack:** .NET 9, C#, Avalonia 11, xUnit. macOS via P/Invoke a libobjc/NSWorkspace; Windows via `System.Diagnostics.Process`.

---

## Mapa de ficheros

| Fichero | Acción |
|---|---|
| `Yottacast.Core/Platform/PlatformProvider.cs` | +`RunningAppInfo`, +`GetRunningApps()`, +`QuitApp()`, +`ForceQuitApp()` |
| `Yottacast.Core/Platform/MacOsPlatformProvider.cs` | Implementa los 3 métodos nuevos |
| `Yottacast.Core/Platform/WindowsPlatformProvider.cs` | Implementa los 3 métodos nuevos |
| `Yottacast.Core/ViewModels/ResultItemViewModel.cs` | +`RunningTag?`, +`InfoTag?` |
| `Yottacast.Core/Search/Application/ApplicationSearch.cs` | Llama `GetRunningApps()` en `Search()`, ajusta label y acciones |
| `Yottacast.Core/Search/Clipboard/ClipboardSearch.cs` | Usa `InfoTag` en vez de concatenar al `Title` |
| `Yottacast.Core.Tests/Search/ApplicationSearchTests.cs` | Nuevos tests de running |
| `Yottacast.Core.Tests/Fakes/FakePlatformProvider.cs` | Stub `GetRunningApps()` |
| `Yottacast/Services/ThemeService.cs` | Lee `results.tags.*`, defaults en `ApplyBuiltinDefault()` |
| `Yottacast/Themes/dark-default.json` | +`results.tags` (filled) |
| `Yottacast/Themes/dark-macos.json` | +`results.tags` (outline) |
| `Yottacast/Views/MainWindow.axaml` | Fila título → StackPanel horizontal con dos Borders opcionales |
| `docs/ui-themes.md` | +tabla tokens `results.tags.*` |
| `docs/result-viewmodels.md` | +`RunningTag`, +`InfoTag` |

---

### Task 1: Core — RunningAppInfo, PlatformProvider, ResultItemViewModel

**Files:**
- Modify: `Yottacast.Core/Platform/PlatformProvider.cs`
- Modify: `Yottacast.Core/ViewModels/ResultItemViewModel.cs`

- [ ] **Step 1.1: Añadir `RunningAppInfo` y métodos abstractos en `PlatformProvider`**

Abre `Yottacast.Core/Platform/PlatformProvider.cs`. Añade el record `RunningAppInfo` justo antes de la declaración de clase, y los tres métodos virtuales al final de la clase (antes del último `}`):

```csharp
// Añadir ANTES de la clase PlatformProvider:
public record RunningAppInfo(string Path, int Pid);
```

```csharp
// Añadir DENTRO de PlatformProvider, al final (antes del último }):

/// <summary>Returns the list of currently running applications with their process IDs.</summary>
public virtual IReadOnlyList<RunningAppInfo> GetRunningApps() => [];

/// <summary>Requests the application with the given PID to quit gracefully (SIGTERM / CloseMainWindow).</summary>
public virtual void QuitApp(int pid) { }

/// <summary>Forcefully terminates the application with the given PID (SIGKILL / Kill).</summary>
public virtual void ForceQuitApp(int pid) { }
```

- [ ] **Step 1.2: Añadir `RunningTag` e `InfoTag` a `ResultItemViewModel`**

Abre `Yottacast.Core/ViewModels/ResultItemViewModel.cs`. Añade las dos propiedades al final de la clase `ResultItemViewModel`:

```csharp
/// <summary>When non-null, renders a green "Running" pill after the title. Text is the pill label.</summary>
public string? RunningTag { get; init; }

/// <summary>When non-null, renders a blue "Info" pill after the title. Text is the pill label.</summary>
public string? InfoTag { get; init; }
```

- [ ] **Step 1.3: Verificar que compila**

```bash
cd Yottacast.Core && dotnet build -warnaserror:CS0103,CS8618 2>&1 | tail -5
```

Esperado: `Build succeeded`.

- [ ] **Step 1.4: Actualizar `FakePlatformProvider` para stub `GetRunningApps`**

Abre `Yottacast.Core.Tests/Fakes/FakePlatformProvider.cs`. La clase base `FakePlatformProvider` hereda la implementación por defecto vacía de `PlatformProvider`, así que no necesita cambios.

Ahora abre `Yottacast.Core.Tests/Search/ApplicationSearchTests.cs`. Localiza la clase interna `FakePlatformProviderWithApps` (líneas ~16-36) y añade soporte para running apps y los métodos de quit:

```csharp
internal sealed class FakePlatformProviderWithApps : FakePlatformProvider {
    public IReadOnlyList<string> AppPaths { get; set; }
    public IReadOnlyList<RunningAppInfo> RunningApps { get; set; } = [];
    public string? LastLaunchedPath { get; private set; }
    public int? LastQuitPid { get; private set; }
    public int? LastForceQuitPid { get; private set; }

    public FakePlatformProviderWithApps(IReadOnlyList<string> appPaths) : base([]) {
        AppPaths = appPaths;
    }

    public override async Task ScanAppsAsync(
        Action<string> addApp, IReadOnlyList<string> dirs, CancellationToken ct) {
        foreach (var path in AppPaths) {
            ct.ThrowIfCancellationRequested();
            addApp(path);
        }
        await Task.CompletedTask;
    }

    public override void LaunchApp(string path) => LastLaunchedPath = path;
    public override IReadOnlyList<RunningAppInfo> GetRunningApps() => RunningApps;
    public override void QuitApp(int pid) => LastQuitPid = pid;
    public override void ForceQuitApp(int pid) => LastForceQuitPid = pid;
}
```

- [ ] **Step 1.5: Commit**

```bash
git add Yottacast.Core/Platform/PlatformProvider.cs \
        Yottacast.Core/ViewModels/ResultItemViewModel.cs \
        Yottacast.Core.Tests/Search/ApplicationSearchTests.cs
git commit -m "feat: add RunningAppInfo, tag properties on ResultItemViewModel"
```

---

### Task 2: Tests para ApplicationSearch — comportamiento running

**Files:**
- Modify: `Yottacast.Core.Tests/Search/ApplicationSearchTests.cs`

Los tests van al final de la clase `ApplicationSearchTests`, antes del cierre `}`.

También necesitarás el using al inicio del fichero (ya debería estar, pero verifica):
```csharp
using Yottacast.Core.Platform;
```

- [ ] **Step 2.1: Escribir los tests fallidos**

Añade al final de `ApplicationSearchTests`:

```csharp
// ── Running apps ──────────────────────────────────────────────────────────────

[Fact]
public async Task Search_RunningApp_HasRunningTagAndBringToFrontLabel() {
    var (search, _, platform) = BuildSearchWithSettings("/Applications/Safari.app");
    platform.RunningApps = [new RunningAppInfo("/Applications/Safari.app", 1234)];
    await StartAndWaitAsync(search);
    var results = SearchAll(search, "Safari");
    Assert.Single(results);
    Assert.Equal("Running", results[0].RunningTag);
    Assert.Equal("Bring to Front", results[0].Actions[0].Label);
}

[Fact]
public async Task Search_NotRunningApp_HasNullRunningTagAndOpenLabel() {
    var (search, _, platform) = BuildSearchWithSettings("/Applications/Safari.app");
    platform.RunningApps = [];
    await StartAndWaitAsync(search);
    var results = SearchAll(search, "Safari");
    Assert.Single(results);
    Assert.Null(results[0].RunningTag);
    Assert.Equal("Open", results[0].Actions[0].Label);
}

[Fact]
public async Task Search_RunningApp_HasQuitAndForceQuitInActions() {
    var (search, _, platform) = BuildSearchWithSettings("/Applications/Safari.app");
    platform.RunningApps = [new RunningAppInfo("/Applications/Safari.app", 1234)];
    await StartAndWaitAsync(search);
    var results = SearchAll(search, "Safari");
    var labels = results[0].Actions.Select(a => a.Label).ToList();
    Assert.Contains("Quit", labels);
    Assert.Contains("Force Quit", labels);
}

[Fact]
public async Task Search_RunningApp_QuitActionCallsQuitAppWithCorrectPid() {
    var (search, _, platform) = BuildSearchWithSettings("/Applications/Safari.app");
    platform.RunningApps = [new RunningAppInfo("/Applications/Safari.app", 5678)];
    await StartAndWaitAsync(search);
    var results = SearchAll(search, "Safari");
    var quitAction = results[0].Actions.First(a => a.Label == "Quit");
    quitAction.Execute?.Invoke();
    Assert.Equal(5678, platform.LastQuitPid);
}

[Fact]
public async Task Search_RunningApp_ForceQuitActionCallsForceQuitAppWithCorrectPid() {
    var (search, _, platform) = BuildSearchWithSettings("/Applications/Safari.app");
    platform.RunningApps = [new RunningAppInfo("/Applications/Safari.app", 5678)];
    await StartAndWaitAsync(search);
    var results = SearchAll(search, "Safari");
    var forceQuit = results[0].Actions.First(a => a.Label == "Force Quit");
    forceQuit.Execute?.Invoke();
    Assert.Equal(5678, platform.LastForceQuitPid);
}

[Fact]
public async Task Search_RunningAppPathCaseInsensitive_DetectedAsRunning() {
    var (search, _, platform) = BuildSearchWithSettings("/Applications/Safari.app");
    // Diferentes mayúsculas/minúsculas en el path
    platform.RunningApps = [new RunningAppInfo("/Applications/SAFARI.APP", 999)];
    await StartAndWaitAsync(search);
    var results = SearchAll(search, "Safari");
    Assert.Equal("Running", results[0].RunningTag);
}
```

- [ ] **Step 2.2: Ejecutar los tests — deben fallar**

```bash
cd Yottacast.Core.Tests && dotnet test --filter "RunningApp" -v normal 2>&1 | tail -20
```

Esperado: 6 tests FAIL con errores del tipo `Expected "Running" but was null` o similar.

- [ ] **Step 2.3: Commit de los tests (en rojo)**

```bash
git add Yottacast.Core.Tests/Search/ApplicationSearchTests.cs
git commit -m "test: running app detection tests (failing)"
```

---

### Task 3: ApplicationSearch — detectar running en Search()

**Files:**
- Modify: `Yottacast.Core/Search/Application/ApplicationSearch.cs`

- [ ] **Step 3.1: Modificar `CreateResultItem` para recibir PID opcional**

Localiza el método `CreateResultItem` (línea ~79). Reemplaza su firma y cuerpo completo:

```csharp
public ResultItemViewModel CreateResultItem(AppInfo app, double score = 4.0,
    string? scoreReason = null,
    IReadOnlyList<(int Start, int Length)>? titleRanges = null,
    int? runningPid = null) {
    var path = app.Path;
    var isRunning = runningPid.HasValue;

    var actions = new List<ResultAction> {
        new() {
            Label        = isRunning ? "Bring to Front" : "Open",
            Hotkey       = ActionHotkey.Enter,
            ShowInFooter = true,
            ShowInMenu   = true,
            ClosesMenu   = true,
            ClosesWindow = true,
            Execute      = () => platform.LaunchApp(path),
        },
        new() {
            Label        = "Copy path",
            Hotkey       = ActionHotkey.MetaC,
            ShowInFooter = true,
            ShowInMenu   = true,
            ClosesMenu   = true,
            HintProvider = () => "Path copied!",
            Execute      = () => clipboard.CopyText(path),
        },
    };

    if (isRunning) {
        var capturedPid = runningPid!.Value;
        actions.Add(new ResultAction {
            Label        = "Quit",
            ShowInMenu   = true,
            ClosesMenu   = true,
            ClosesWindow = true,
            Execute      = () => platform.QuitApp(capturedPid),
        });
        actions.Add(new ResultAction {
            Label        = "Force Quit",
            ShowInMenu   = true,
            ClosesMenu   = true,
            ClosesWindow = true,
            Execute      = () => platform.ForceQuitApp(capturedPid),
        });
    }

    return new() {
        Icon          = "📱",
        IconBytes     = iconCache.Get(path),
        Title         = app.Name,
        Subtitle      = path,
        ItemPath      = path,
        Category      = "Application",
        Score         = score,
        ScoreReason   = scoreReason,
        TitleRanges   = titleRanges,
        GetDragPayload = () => new DragPayload.File(path),
        RunningTag    = isRunning ? "Running" : null,
        Actions       = actions,
    };
}
```

- [ ] **Step 3.2: Modificar `Search()` para pasar el PID**

Localiza el método `Search()` (línea ~117). Reemplázalo:

```csharp
public IReadOnlyList<BaseResultItemViewModel> Search(string query, int limit) {
    if (!settings.EnableAppSearch) return [];

    var runningByPath = platform.GetRunningApps()
        .ToDictionary(x => x.Path, x => x.Pid, StringComparer.OrdinalIgnoreCase);

    var results = _apps.Values
        .Select(a => (app: a, match: NameMatcher.Match(a.Name, query)))
        .Where(x => x.match.Score > 0)
        .OrderByDescending(x => x.match.Score)
        .Take(limit)
        .Select(x => {
            var isRunning = runningByPath.TryGetValue(x.app.Path, out var pid);
            return CreateResultItem(
                x.app,
                Math.Max(x.match.Score * 4, AppDefaults.AppMinScore),
                x.match.Reason != null ? $"{x.match.Reason} (×4)" : null,
                x.match.Ranges,
                isRunning ? pid : null);
        })
        .ToList();

    logger.LogDebug("AppSearch query=\"{Query}\" cache={CacheCount} results={ResultCount} ready={Ready}",
        query, _apps.Count, results.Count, _readyTcs.Task.IsCompleted);
    return results;
}
```

- [ ] **Step 3.3: Ejecutar los tests — deben pasar**

```bash
cd Yottacast.Core.Tests && dotnet test --filter "RunningApp" -v normal 2>&1 | tail -15
```

Esperado: 6 tests PASS.

- [ ] **Step 3.4: Ejecutar la suite completa**

```bash
cd Yottacast.Core.Tests && dotnet test 2>&1 | tail -5
```

Esperado: `Failed: 0, Passed: 1269` (6 nuevos tests añadidos).

- [ ] **Step 3.5: Commit**

```bash
git add Yottacast.Core/Search/Application/ApplicationSearch.cs
git commit -m "feat: detect running apps in ApplicationSearch, add Bring to Front/Quit/Force Quit"
```

---

### Task 4: ClipboardSearch — migrar a InfoTag

**Files:**
- Modify: `Yottacast.Core/Search/Clipboard/ClipboardSearch.cs`

- [ ] **Step 4.1: Modificar `BuildUrlResult` para usar InfoTag**

Localiza `BuildUrlResult` (línea ~91). Cambia la construcción del `ResultItemViewModel`:

```csharp
// Antes:
Title = $"{(url.Length > 80 ? url[..77] + "…" : url)} · from clipboard",

// Después:
Title   = url.Length > 80 ? url[..77] + "…" : url,
InfoTag = "from clipboard",
```

El resultado final del objeto debe quedar:

```csharp
return new ResultItemViewModel
{
    IconBytes  = iconBytes,
    Title      = url.Length > 80 ? url[..77] + "…" : url,
    InfoTag    = "from clipboard",
    Subtitle   = $"Open in {browserLabel}",
    Category   = "Web",
    Score      = 4.0,
    Actions = [ ... ],  // sin cambios
};
```

- [ ] **Step 4.2: Modificar `BuildLocalPathResult` para usar InfoTag**

Localiza la creación del `FileResultItemViewModel` al final de `BuildLocalPathResult` (línea ~178). Cambia:

```csharp
// Antes:
return new FileResultItemViewModel
{
    IconBytes = fileIconCache.GetOrPreload(expanded),
    Title     = $"{title} · from clipboard",
    Subtitle  = expanded,
    ...
};

// Después:
return new FileResultItemViewModel
{
    IconBytes = fileIconCache.GetOrPreload(expanded),
    Title     = title,
    InfoTag   = "from clipboard",
    Subtitle  = expanded,
    ItemPath  = capturedPath,
    Category  = "Files",
    Score     = 4.0,
    Actions   = actions,
};
```

- [ ] **Step 4.3: Ejecutar la suite completa**

```bash
cd Yottacast.Core.Tests && dotnet test 2>&1 | tail -5
```

Esperado: sin regresiones.

- [ ] **Step 4.4: Commit**

```bash
git add Yottacast.Core/Search/Clipboard/ClipboardSearch.cs
git commit -m "feat: migrate from-clipboard text to InfoTag pill"
```

---

### Task 5: MacOsPlatformProvider — GetRunningApps, QuitApp, ForceQuitApp

**Files:**
- Modify: `Yottacast.Core/Platform/MacOsPlatformProvider.cs`

- [ ] **Step 5.1: Añadir P/Invokes necesarios**

Abre `Yottacast.Core/Platform/MacOsPlatformProvider.cs`. Añade los P/Invokes al final de la clase (después de los existentes, antes del último `}`). Necesitas `System.Runtime.InteropServices` que ya está en los usings:

```csharp
// ── Running apps P/Invokes ────────────────────────────────────────────────────

[DllImport("libobjc.dylib", EntryPoint = "objc_getClass")]
private static extern IntPtr ObjcGetClass2(string name);

[DllImport("libobjc.dylib", EntryPoint = "sel_registerName")]
private static extern IntPtr Sel2(string name);

[DllImport("libobjc.dylib", EntryPoint = "objc_msgSend")]
private static extern IntPtr MsgSend(IntPtr receiver, IntPtr selector);

[DllImport("libobjc.dylib", EntryPoint = "objc_msgSend")]
private static extern nuint MsgSendCount(IntPtr receiver, IntPtr selector);

[DllImport("libobjc.dylib", EntryPoint = "objc_msgSend")]
private static extern IntPtr MsgSendAtIndex(IntPtr receiver, IntPtr selector, nuint index);

[DllImport("libobjc.dylib", EntryPoint = "objc_msgSend")]
private static extern IntPtr MsgSendUtf8(IntPtr receiver, IntPtr selector);

[DllImport("libobjc.dylib", EntryPoint = "objc_msgSend")]
private static extern int MsgSendPid(IntPtr receiver, IntPtr selector);

[DllImport("libc", EntryPoint = "kill")]
private static extern int Kill(int pid, int sig);
```

- [ ] **Step 5.2: Implementar `GetRunningApps`**

Añade el método justo antes de los P/Invokes:

```csharp
public override IReadOnlyList<RunningAppInfo> GetRunningApps() {
    try {
        var workspace    = MsgSend(ObjcGetClass2("NSWorkspace"), Sel2("sharedWorkspace"));
        var appsArray    = MsgSend(workspace, Sel2("runningApplications"));
        var count        = (int)MsgSendCount(appsArray, Sel2("count"));
        var selAtIndex   = Sel2("objectAtIndex:");
        var selBundlePath = Sel2("bundlePath");
        var selUtf8      = Sel2("UTF8String");
        var selPid       = Sel2("processIdentifier");

        var result = new List<RunningAppInfo>(count);
        for (nuint i = 0; i < (nuint)count; i++) {
            var app = MsgSendAtIndex(appsArray, selAtIndex, i);
            if (app == IntPtr.Zero) continue;
            var nsPath = MsgSend(app, selBundlePath);
            if (nsPath == IntPtr.Zero) continue;
            var utf8Ptr = MsgSendUtf8(nsPath, selUtf8);
            if (utf8Ptr == IntPtr.Zero) continue;
            var path = Marshal.PtrToStringUTF8(utf8Ptr);
            if (string.IsNullOrEmpty(path)) continue;
            var pid = MsgSendPid(app, selPid);
            result.Add(new RunningAppInfo(path, pid));
        }
        return result;
    } catch {
        return [];
    }
}
```

- [ ] **Step 5.3: Implementar `QuitApp` y `ForceQuitApp`**

```csharp
public override void QuitApp(int pid) {
    try { Kill(pid, 15); } catch { }  // SIGTERM
}

public override void ForceQuitApp(int pid) {
    try { Kill(pid, 9); } catch { }   // SIGKILL
}
```

- [ ] **Step 5.4: Verificar que compila**

```bash
cd Yottacast && dotnet build 2>&1 | tail -5
```

Esperado: `Build succeeded`.

- [ ] **Step 5.5: Commit**

```bash
git add Yottacast.Core/Platform/MacOsPlatformProvider.cs
git commit -m "feat: macOS GetRunningApps via NSWorkspace, QuitApp/ForceQuitApp via SIGTERM/SIGKILL"
```

---

### Task 6: WindowsPlatformProvider — GetRunningApps, QuitApp, ForceQuitApp

**Files:**
- Modify: `Yottacast.Core/Platform/WindowsPlatformProvider.cs`

- [ ] **Step 6.1: Implementar los tres métodos**

Abre `Yottacast.Core/Platform/WindowsPlatformProvider.cs`. Añade los métodos antes del cierre de clase. Asegúrate de que `using System.Diagnostics;` está en los usings (probablemente ya está):

```csharp
public override IReadOnlyList<RunningAppInfo> GetRunningApps() {
    try {
        return Process.GetProcesses()
            .Select(p => {
                try {
                    var path = p.MainModule?.FileName;
                    return path is null ? null : new RunningAppInfo(path, p.Id);
                } catch {
                    return null;
                }
            })
            .OfType<RunningAppInfo>()
            .ToList();
    } catch {
        return [];
    }
}

public override void QuitApp(int pid) {
    try { Process.GetProcessById(pid).CloseMainWindow(); } catch { }
}

public override void ForceQuitApp(int pid) {
    try { Process.GetProcessById(pid).Kill(); } catch { }
}
```

- [ ] **Step 6.2: Verificar que compila**

```bash
cd Yottacast && dotnet build 2>&1 | tail -5
```

Esperado: `Build succeeded`.

- [ ] **Step 6.3: Commit**

```bash
git add Yottacast.Core/Platform/WindowsPlatformProvider.cs
git commit -m "feat: Windows GetRunningApps/QuitApp/ForceQuitApp via System.Diagnostics.Process"
```

---

### Task 7: Tokens de tema — ThemeService y JSON

**Files:**
- Modify: `Yottacast/Services/ThemeService.cs`
- Modify: `Yottacast/Themes/dark-default.json`
- Modify: `Yottacast/Themes/dark-macos.json`

- [ ] **Step 7.1: Leer tokens en `ThemeService.Apply()`**

En `ThemeService.cs`, localiza el bloque `// ── Results ──` (línea ~221). Después de la línea que lee `matchHighlight` (la última del bloque `if (results != null)`), añade la lectura de tags **dentro** del mismo bloque `if (results != null)`:

```csharp
var tags = results["tags"];
if (tags != null) {
    SetCornerRadius(app, "Theme.Results.Tag.CornerRadius",         tags["cornerRadius"]);
    SetBrush(app,  "Theme.Results.Tag.Running.Color",              tags["running"]?["color"]);
    SetBrush(app,  "Theme.Results.Tag.Running.Background",         tags["running"]?["background"]);
    SetBrush(app,  "Theme.Results.Tag.Running.BorderColor",        tags["running"]?["borderColor"]);
    SetBrush(app,  "Theme.Results.Tag.Info.Color",                 tags["info"]?["color"]);
    SetBrush(app,  "Theme.Results.Tag.Info.Background",            tags["info"]?["background"]);
    SetBrush(app,  "Theme.Results.Tag.Info.BorderColor",           tags["info"]?["borderColor"]);
}
```

- [ ] **Step 7.2: Añadir defaults en `ApplyBuiltinDefault()`**

En el bloque `// ── Results ──` de `ApplyBuiltinDefault()` (línea ~420), añade al final de ese bloque (después de la línea de `MatchHighlight.BackgroundOpacity`):

```csharp
// ── Result Tags (pills) ──
app.Resources["Theme.Results.Tag.CornerRadius"]          = new CornerRadius(4);
app.Resources["Theme.Results.Tag.Running.Color"]         = B("#30D158");
app.Resources["Theme.Results.Tag.Running.Background"]    = B("#2430D158");  // verde 14% opacidad
app.Resources["Theme.Results.Tag.Running.BorderColor"]   = new SolidColorBrush(Colors.Transparent);
app.Resources["Theme.Results.Tag.Info.Color"]            = B("#5AC8FA");
app.Resources["Theme.Results.Tag.Info.Background"]       = B("#1A0A84FF");  // azul 10% opacidad
app.Resources["Theme.Results.Tag.Info.BorderColor"]      = new SolidColorBrush(Colors.Transparent);
```

- [ ] **Step 7.3: Añadir `results.tags` en `dark-default.json` (filled)**

Abre `Yottacast/Themes/dark-default.json`. Dentro del objeto `"results"`, añade la sección `"tags"` al final del objeto (antes del cierre `}`):

```json
"tags": {
  "cornerRadius": 4,
  "running": {
    "color": "#30D158",
    "background": "#2430D158",
    "borderColor": "Transparent"
  },
  "info": {
    "color": "#5AC8FA",
    "background": "#1A0A84FF",
    "borderColor": "Transparent"
  }
}
```

- [ ] **Step 7.4: Añadir `results.tags` en `dark-macos.json` (outline)**

Abre `Yottacast/Themes/dark-macos.json`. Dentro del objeto `"results"`, añade:

```json
"tags": {
  "cornerRadius": 4,
  "running": {
    "color": "#30D158",
    "background": "Transparent",
    "borderColor": "#B330D158"
  },
  "info": {
    "color": "#0A84FF",
    "background": "Transparent",
    "borderColor": "#B30A84FF"
  }
}
```

(`#B3` = 70% opacidad en canal alpha hexadecimal: 0.7 × 255 ≈ 179 = 0xB3)

- [ ] **Step 7.5: Verificar que compila**

```bash
cd Yottacast && dotnet build 2>&1 | tail -5
```

Esperado: `Build succeeded`.

- [ ] **Step 7.6: Commit**

```bash
git add Yottacast/Services/ThemeService.cs \
        Yottacast/Themes/dark-default.json \
        Yottacast/Themes/dark-macos.json
git commit -m "feat: theme tokens for result tag pills (running/info, filled+outline)"
```

---

### Task 8: AXAML — pills en la fila del título

**Files:**
- Modify: `Yottacast/Views/MainWindow.axaml`

- [ ] **Step 8.1: Envolver el título en StackPanel horizontal con las dos pills**

Abre `Yottacast/Views/MainWindow.axaml`. Localiza el bloque `<!-- Title + subtitle -->` (línea ~437). Reemplaza únicamente el `HighlightTextBlock` del título (NO el del subtítulo) envolviéndolo en un `StackPanel` horizontal con los dos `Border` opcionales:

El bloque antes del cambio:
```xml
<!-- Title + subtitle -->
<StackPanel Grid.Column="1"
            Orientation="Vertical"
            VerticalAlignment="Center"
            Spacing="2">
    <controls:HighlightTextBlock x:CompileBindings="False"
                                 Text="{Binding Title}"
                                 Ranges="{Binding TitleRanges}"
                                 Foreground="{DynamicResource Theme.Results.Title.Color}"
                                 FontSize="{DynamicResource Theme.Results.Title.Size}"
                                 FontWeight="Medium"/>
    <controls:HighlightTextBlock x:CompileBindings="False"
                                 Text="{Binding Subtitle}"
```

El bloque después del cambio (sustituye solo hasta antes del segundo `HighlightTextBlock`):

```xml
<!-- Title + subtitle -->
<StackPanel Grid.Column="1"
            Orientation="Vertical"
            VerticalAlignment="Center"
            Spacing="2">
    <StackPanel Orientation="Horizontal" Spacing="6" VerticalAlignment="Center">
        <controls:HighlightTextBlock x:CompileBindings="False"
                                     Text="{Binding Title}"
                                     Ranges="{Binding TitleRanges}"
                                     Foreground="{DynamicResource Theme.Results.Title.Color}"
                                     FontSize="{DynamicResource Theme.Results.Title.Size}"
                                     FontWeight="Medium"/>
        <Border x:CompileBindings="False"
                IsVisible="{Binding RunningTag, Converter={x:Static ObjectConverters.IsNotNull}}"
                Background="{DynamicResource Theme.Results.Tag.Running.Background}"
                BorderBrush="{DynamicResource Theme.Results.Tag.Running.BorderColor}"
                BorderThickness="1"
                CornerRadius="{DynamicResource Theme.Results.Tag.CornerRadius}"
                Padding="5,1"
                VerticalAlignment="Center">
            <TextBlock x:CompileBindings="False"
                       Text="{Binding RunningTag}"
                       Foreground="{DynamicResource Theme.Results.Tag.Running.Color}"
                       FontSize="10"
                       FontWeight="Medium"/>
        </Border>
        <Border x:CompileBindings="False"
                IsVisible="{Binding InfoTag, Converter={x:Static ObjectConverters.IsNotNull}}"
                Background="{DynamicResource Theme.Results.Tag.Info.Background}"
                BorderBrush="{DynamicResource Theme.Results.Tag.Info.BorderColor}"
                BorderThickness="1"
                CornerRadius="{DynamicResource Theme.Results.Tag.CornerRadius}"
                Padding="5,1"
                VerticalAlignment="Center">
            <TextBlock x:CompileBindings="False"
                       Text="{Binding InfoTag}"
                       Foreground="{DynamicResource Theme.Results.Tag.Info.Color}"
                       FontSize="10"
                       FontWeight="Medium"/>
        </Border>
    </StackPanel>
    <controls:HighlightTextBlock x:CompileBindings="False"
                                 Text="{Binding Subtitle}"
```

- [ ] **Step 8.2: Verificar que compila**

```bash
cd Yottacast && dotnet build 2>&1 | tail -5
```

Esperado: `Build succeeded`.

- [ ] **Step 8.3: Ejecutar la suite de tests completa**

```bash
cd Yottacast.Core.Tests && dotnet test 2>&1 | tail -5
```

Esperado: `Failed: 0`.

- [ ] **Step 8.4: Commit**

```bash
git add Yottacast/Views/MainWindow.axaml
git commit -m "feat: render RunningTag and InfoTag pills inline in result title row"
```

---

### Task 9: Documentación

**Files:**
- Modify: `docs/ui-themes.md`
- Modify: `docs/result-viewmodels.md`

- [ ] **Step 9.1: Añadir tabla de tokens en `docs/ui-themes.md`**

Localiza la sección `### Results` en `docs/ui-themes.md` (después de la tabla de tokens de Results). Añade una subsección nueva al final de esa sección:

```markdown
#### Tags (pills inline en el título)

| JSON path | Recurso Avalonia |
|---|---|
| `results.tags.cornerRadius` | `Theme.Results.Tag.CornerRadius` |
| `results.tags.running.color` | `Theme.Results.Tag.Running.Color` |
| `results.tags.running.background` | `Theme.Results.Tag.Running.Background` |
| `results.tags.running.borderColor` | `Theme.Results.Tag.Running.BorderColor` |
| `results.tags.info.color` | `Theme.Results.Tag.Info.Color` |
| `results.tags.info.background` | `Theme.Results.Tag.Info.Background` |
| `results.tags.info.borderColor` | `Theme.Results.Tag.Info.BorderColor` |

El estilo filled (fondo tintado, borde transparente) u outline (fondo transparente, borde con color) se controla
combinando `background` y `borderColor`: filled pone `background` con alpha y `borderColor: "Transparent"`;
outline hace lo contrario. `dark-default` usa filled; `dark-macos` usa outline.
```

- [ ] **Step 9.2: Actualizar `docs/result-viewmodels.md`**

En el fichero `docs/result-viewmodels.md`, en la sección que describe `ResultItemViewModel` (busca "ResultItemViewModel"), añade las dos propiedades nuevas con su descripción:

```markdown
- `RunningTag` (`string?`) — cuando no es null, muestra una pill verde con este texto después del título. Asignado por `ApplicationSearch` cuando la app está en la lista de procesos activos.
- `InfoTag` (`string?`) — cuando no es null, muestra una pill azul con este texto después del título. Asignado por `ClipboardSearch` con el valor `"from clipboard"`.
```

- [ ] **Step 9.3: Commit final**

```bash
git add docs/ui-themes.md docs/result-viewmodels.md
git commit -m "docs: document RunningTag, InfoTag and results.tags theme tokens"
```

---

## Verificación manual

Tras completar todos los tasks, arranca la app (`cd Yottacast && dotnet run`) y verifica:

1. Busca una app que esté corriendo (ej. "Finder") → debe aparecer pill "Running" verde inline después del nombre.
2. Busca una app que no esté corriendo → sin pill.
3. Con una app running seleccionada, pulsa Tab → menú muestra "Bring to Front", "Copy path", "Quit", "Force Quit".
4. Pulsa Enter en una app running → la trae al frente (no la relanza).
5. Copia una URL al portapapeles, abre Yottacast → el resultado muestra la URL limpia con pill "from clipboard" azul.
6. Cambia el tema a dark-macos en Settings → las pills pasan a estilo outline.
7. Cambia a dark-default → las pills vuelven a estilo filled.
