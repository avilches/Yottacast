# Plan: Temas de usuario desde la carpeta plugins

## Contexto

Los temas built-in viven en `Yottacast/Themes/` junto al ejecutable. Se quiere permitir instalar temas personalizados como ficheros JSON en la carpeta de plugins (`~/Library/Application Support/Yottacast/plugins/`), con `"type": "theme"`. Si el tema seleccionado es de usuario, se vigila su fichero y se recarga automáticamente al modificarlo (igual que los plugins de WebSearch).

El fichero `Yottacast/Themes/settings.json` no lo usa nadie — se elimina.

---

## Diseño

### Identificación: prefijo `user:`

Los temas de usuario se identifican con el prefijo `user:` en su ID. Un fichero `my-theme.json` en plugins → ID `"user:my-theme"`. Esto elimina colisiones con built-in y facilita distinguirlos.

### Watcher propio en ThemeService (no en PluginService)

ThemeService vive en el proyecto UI (`Yottacast/`) donde tiene acceso a `Dispatcher.UIThread` y `Application.Resources`. PluginService vive en `Yottacast.Core/` sin acceso a UI. Además la lógica es distinta: solo se vigila el fichero del tema activo, no todos.

Dos watchers:
1. **Watcher de fichero activo**: vigila solo el fichero del tema seleccionado (si es `user:`). Al detectar cambio → debounce 300ms → re-aplica en UI thread. Se recrea cada vez que cambia el tema seleccionado.
2. **Watcher de directorio**: vigila `AppPaths.PluginsDir` para detectar temas añadidos/eliminados → fire `ThemesChanged` para que Settings actualice el picker.

---

## Archivos a modificar

### 1. Eliminar `Yottacast/Themes/settings.json`

Fichero muerto. Borrar del proyecto y del `.csproj` si tiene entrada explícita.

### 2. `Yottacast/Services/ThemeService.cs`

**Nuevos campos:**
```csharp
private const string UserThemePrefix = "user:";
private FileSystemWatcher? _activeThemeWatcher;
private FileSystemWatcher? _pluginsDirWatcher;
private CancellationTokenSource? _debounceCts;
private string? _activeThemeId;
```

**Nuevos helpers:**
```csharp
public static bool IsUserTheme(string id) => id.StartsWith(UserThemePrefix);

private static string UserThemeFilePath(string id) =>
    Path.Combine(AppPaths.PluginsDir, id[UserThemePrefix.Length..] + ".json");
```

**`AvailableThemes()` — extender:**
Después de escanear `ThemesFolder`, escanear `AppPaths.PluginsDir` (si existe) buscando `*.json` con `"type": "theme"` (case-insensitive). Añadir con ID `"user:{filename}"`.

**`Apply(string themeName)` — extender path resolution:**
```csharp
var themePath = IsUserTheme(themeName)
    ? UserThemeFilePath(themeName)
    : Path.Combine(ThemesFolder, $"{themeName}.json");
```
Al final de Apply exitoso, llamar `WatchActiveTheme(themeName)`.

**`StartWatching()` — nuevo método público:**
Crea el watcher de directorio sobre `AppPaths.PluginsDir` para `*.json`. En `Created`/`Deleted`/`Renamed` → debounce → fire `ThemesChanged`. También llama `WatchActiveTheme` con el tema actual.

**`WatchActiveTheme(string themeId)` — nuevo:**
- Dispose `_activeThemeWatcher`.
- Si `!IsUserTheme(themeId)` → null y return.
- Crear `FileSystemWatcher` en `AppPaths.PluginsDir` con `Filter` = nombre del fichero.
- En `Changed` → debounce 300ms → `Dispatcher.UIThread.Post(() => Apply(themeId))`.

**Evento `ThemesChanged`:**
```csharp
public event Action? ThemesChanged;
```

**`IDisposable`:**
Dispose ambos watchers y el CancellationTokenSource.

### 3. `Yottacast/ViewModels/SettingsWindowViewModel.cs`

**Cambiar `Themes` de `get`-only a `[ObservableProperty]`:**
```csharp
// Antes:
public IReadOnlyList<ThemeOption> Themes { get; }
// Después:
[ObservableProperty] private IReadOnlyList<ThemeOption> _themes;
```

**Suscribirse a `ThemesChanged` en constructor:**
```csharp
themeService.ThemesChanged += OnThemesChanged;
```

**Handler:**
```csharp
private void OnThemesChanged() {
    Dispatcher.UIThread.Post(() => {
        var currentId = SelectedTheme?.Id;
        Themes = _themeService.AvailableThemes();
        SelectedTheme = Themes.FirstOrDefault(t => t.Id == currentId)
                        ?? Themes.FirstOrDefault();
    });
}
```

### 4. `Yottacast/App.axaml.cs`

Después de `themeService.Apply(userSettings.Theme)` (línea 52), añadir:
```csharp
themeService.StartWatching();
```

### 5. `docs/ui-themes.md`

Añadir sección "Temas de usuario" documentando:
- Ubicación: `AppPaths.PluginsDir`
- Formato: mismo JSON que built-in + `"type": "theme"`
- Prefijo `user:` en el ID
- Recarga automática del tema activo
- Actualización del picker en Settings

---

## Casos borde

| Caso | Comportamiento |
|---|---|
| Usuario borra el fichero del tema activo | `Apply()` falla → `ApplyBuiltinDefault()` + warning en log |
| JSON inválido en tema de usuario | try/catch existente → `ApplyBuiltinDefault()` |
| Carpeta plugins no existe al arrancar | `StartWatching()` crea el directorio (como PluginService) |
| Tema user con mismo nombre que built-in | IDs distintos gracias al prefijo `user:` |

---

## Verificación

1. `cd Yottacast && dotnet build` → 0 errores
2. `cd Yottacast.Core.Tests && dotnet test` → todos pasan
3. Copiar un JSON de tema a `~/Library/Application Support/Yottacast/plugins/` con `"type": "theme"` → aparece en Settings
4. Seleccionar el tema de usuario → se aplica
5. Modificar el fichero → el tema se recarga en caliente
6. Borrar el fichero → fallback al tema por defecto
7. Verificar que los temas built-in siguen funcionando igual
