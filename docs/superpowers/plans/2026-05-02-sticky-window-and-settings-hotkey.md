# Sticky Window Always-On-Top + Settings Survives Hotkey Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Cuando `StickyWindow = true`, la ventana de búsqueda flota sobre todas las demás (Topmost); y cuando el hotkey oculta la ventana principal con settings abierto, settings permanece visible y recibe el foco.

**Architecture:** Dos cambios independientes en `App.axaml.cs`. El primero añade un evento `StickyWindowChanged` en `UserSettings` para propagar cambios en tiempo real a `Topmost`. El segundo ajusta el hotkey handler para activar settings en vez de restaurar la app anterior cuando settings está abierto.

**Tech Stack:** .NET 9, Avalonia 11, CommunityToolkit.Mvvm

---

### Task 1: StickyWindow → Topmost

**Files:**
- Modify: `Yottacast.Core/Services/UserSettings.cs` (línea ~30)
- Modify: `Yottacast/ViewModels/SettingsWindowViewModel.cs` (línea ~141)
- Modify: `Yottacast/App.axaml.cs` (líneas ~92-93 y método `OpenSettings`)
- Modify: `docs/user-settings.md` (descripción de `StickyWindow`)

- [ ] **Step 1: Añadir evento `StickyWindowChanged` en `UserSettings`**

En `Yottacast.Core/Services/UserSettings.cs`, después de la línea 30 (`public void NotifySearchSettingsChanged() => SearchSettingsChanged?.Invoke();`), añadir:

```csharp
public event Action? StickyWindowChanged;
public void NotifyStickyWindowChanged() => StickyWindowChanged?.Invoke();
```

- [ ] **Step 2: Disparar `StickyWindowChanged` al cambiar el toggle en Settings**

En `Yottacast/ViewModels/SettingsWindowViewModel.cs`, línea ~141, reemplazar:

```csharp
partial void OnStickyWindowChanged(bool value)                  { _settings.StickyWindow                 = value; _settings.Save(); _logger.LogInformation("Settings: StickyWindow = {Value}", value); }
```

Por:

```csharp
partial void OnStickyWindowChanged(bool value)                  { _settings.StickyWindow                 = value; _settings.Save(); _logger.LogInformation("Settings: StickyWindow = {Value}", value); _settings.NotifyStickyWindowChanged(); }
```

- [ ] **Step 3: Aplicar `Topmost` al crear `mainWindow` y suscribirse a cambios**

En `Yottacast/App.axaml.cs`, después de la línea `desktop.MainWindow = mainWindow;` (~línea 93), añadir:

```csharp
mainWindow.Topmost = userSettings.StickyWindow;
userSettings.StickyWindowChanged += () =>
    Dispatcher.UIThread.InvokeAsync(() => mainWindow.Topmost = userSettings.StickyWindow);
```

- [ ] **Step 4: Settings hereda `Topmost` al abrirse**

En `Yottacast/App.axaml.cs`, en el método `OpenSettings()`, reemplazar:

```csharp
_settingsWindow = new SettingsWindow {
    DataContext = _settingsVm,
};
```

Por:

```csharp
_settingsWindow = new SettingsWindow {
    DataContext = _settingsVm,
    Topmost = _services.GetRequiredService<UserSettings>().StickyWindow,
};
```

Esto garantiza que settings también flota sobre otras ventanas cuando sticky está activo, evitando que quede detrás de la ventana principal.

- [ ] **Step 5: Actualizar doc `user-settings.md`**

En `docs/user-settings.md`, en la tabla de preferencias, reemplazar la descripción de `StickyWindow`:

```
| StickyWindow | `true` | La ventana permanece visible al perder el foco; `false` = se oculta al estilo Alfred |
```

Por:

```
| StickyWindow | `true` | La ventana permanece visible al perder el foco y flota sobre todas las demás ventanas del SO (always on top); `false` = se oculta al estilo Alfred |
```

- [ ] **Step 6: Compilar**

```bash
cd Yottacast && dotnet build
```

Esperado: sin errores.

- [ ] **Step 7: Verificar comportamiento**

```bash
cd Yottacast && dotnet run
```

1. Abrir Settings (Cmd+,) → activar StickyWindow → la ventana de búsqueda debe flotar sobre otras apps (arrastra cualquier app sobre Yottacast y verifica que no puede taparla).
2. Desactivar StickyWindow → la ventana vuelve a z-order normal.
3. Con StickyWindow activo, abrir Settings → settings también debe flotar y aparecer encima de la ventana de búsqueda al activarse.

- [ ] **Step 8: Ejecutar tests**

```bash
cd Yottacast.Core.Tests && dotnet test
```

Esperado: todos los tests pasan.

- [ ] **Step 9: Commit**

```bash
git add Yottacast.Core/Services/UserSettings.cs \
        Yottacast/ViewModels/SettingsWindowViewModel.cs \
        Yottacast/App.axaml.cs \
        docs/user-settings.md
git commit -m "feat: StickyWindow sets window always on top (Topmost)"
```

---

### Task 2: Settings sobrevive al hotkey

**Files:**
- Modify: `Yottacast/App.axaml.cs` (líneas ~359-363 dentro de `RegisterGlobalHotKey`)

- [ ] **Step 1: Modificar el hotkey handler**

En `Yottacast/App.axaml.cs`, dentro de `RegisterGlobalHotKey`, reemplazar el bloque `else` que oculta la ventana (líneas ~359-363):

```csharp
} else {
    // App or settings is focused → hide main window; settings stays open
    window.Hide();
    AppHandler.Instance.OnHide();
}
```

Por:

```csharp
} else {
    // App or settings is focused → hide main window
    window.Hide();
    if (settingsOpen) {
        // Settings is open: give it focus instead of restoring the previous app.
        // OnHide() is skipped so _previousApp is preserved for the next hide.
        _settingsWindow!.Activate();
    } else {
        AppHandler.Instance.OnHide();
    }
}
```

- [ ] **Step 2: Compilar**

```bash
cd Yottacast && dotnet build
```

Esperado: sin errores.

- [ ] **Step 3: Verificar comportamiento**

```bash
cd Yottacast && dotnet run
```

1. Mostrar el launcher con el hotkey.
2. Abrir Settings con Cmd+,.
3. Pulsar el hotkey de nuevo → la ventana de búsqueda se oculta, **settings permanece visible y recibe el foco**.
4. Cerrar settings (Cmd+W o botón nativo).
5. Pulsar el hotkey para mostrar el launcher, usarlo, pulsar el hotkey de nuevo → el foco vuelve a la app anterior (comportamiento normal sin settings).

- [ ] **Step 4: Ejecutar tests**

```bash
cd Yottacast.Core.Tests && dotnet test
```

Esperado: todos los tests pasan.

- [ ] **Step 5: Commit**

```bash
git add Yottacast/App.axaml.cs
git commit -m "fix: settings window survives hotkey hide"
```
