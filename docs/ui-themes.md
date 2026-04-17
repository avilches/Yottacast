# UI: Themes

Clase: `Yottacast.Services.ThemeService`

Lee `Themes/{name}.json`, aplica tokens en `Application.Current.Resources` en runtime.

`ThemeService.Apply(themeName)` — carga el JSON indicado. Si el fichero no existe o el parsing falla, registra un warning en el log y llama `ApplyBuiltinDefault()` como fallback.
`ThemeService.ApplyBuiltinDefault()` — aplica dark-default hardcodeado como fallback (no puede fallar).

All theme tokens are listed in `ThemeService.ApplyBuiltinDefault()` which also serves as the canonical default. Colors use Avalonia's `Color.TryParse` format; see any theme JSON file for examples.
Los JSON se copian al output vía `CopyToOutputDirectory=PreserveNewest`.

Available themes are the JSON files in `Yottacast/Themes/` (excluding `settings.json`).

**`ThemeOption` record**: `AvailableThemes()` devuelve `IReadOnlyList<ThemeOption>` donde cada entrada tiene `Id` (nombre de fichero sin extensión) y `DisplayName` (campo `"name"` del JSON; si falta o el parse falla, usa el `Id` como fallback). Los ficheros se procesan ordenados alfabéticamente; si dos ficheros producen el mismo `Id` (p.ej. copias de conflicto de iCloud como `dark-default 2.json`), solo el primero se incluye.

**Campo `variant` en el JSON**: `Apply()` lee `json["variant"]` y asigna `app.RequestedThemeVariant` a `ThemeVariant.Light` si el valor es `"light"`, o `ThemeVariant.Dark` en cualquier otro caso.

**Aplicación al arranque**: `App.OnFrameworkInitializationCompleted()` llama `themeService.Apply(userSettings.Theme)` de forma síncrona antes de crear la `MainWindow`, por lo que el tema está activo antes de que cualquier control se renderice.

**Hot-swap en Settings**: `SettingsWindowViewModel.OnSelectedThemeChanged()` llama `_themeService.Apply(value.Id)` inmediatamente al cambiar el picker. El cambio es instantáneo sin reiniciar la aplicación.

**Metadata en JSON (author, url)**: todos los temas tienen `"author": ""` y `"url": ""`. `ThemeService` los ignora hoy; estarán disponibles cuando se implemente la descarga de temas.

**Gotcha — Colores mal formados ignorados silenciosamente**: `SetBrush()` usa `Color.TryParse`. Si el valor del color en el JSON no es un color válido, el brush no se asigna y el token conserva su valor anterior sin ningún error o aviso.

**Gotcha — Temas cargados síncronamente en SettingsWindow**: `SettingsWindowViewModel` llama `AvailableThemes()` en su constructor, que enumera los JSON de `Themes/` ordenados alfabéticamente por nombre de fichero y excluye `settings.json`. Si ninguno carga, añade `"dark-default"` como fallback.

**Resolución de la carpeta de temas**: `ThemesFolder` se resuelve como `Path.Combine(AppContext.BaseDirectory, "Themes")`, relativo al directorio del ejecutable, no al directorio de trabajo actual.

**`Apply()` con `Application.Current` nulo**: si `Application.Current` es null en el momento de aplicar un tema (p.ej. en tests o inicio muy temprano), `Apply()` llama directamente `ApplyBuiltinDefault()` sin registrar error.

**Tokens numéricos ignorados silenciosamente**: `SetDouble()` y `SetCornerRadius()` aplican el mismo patrón que `SetBrush()` — si el nodo JSON es null, el token se omite sin aviso y conserva su valor anterior. Esto afecta a todos los tokens de `fonts` y `layout`.

**Detección del modo oscuro del sistema**: `PlatformProvider.DefaultTheme()` llama a `IsSystemDarkMode()` (abstracto, implementado por cada plataforma) y devuelve `"dark-default"` si es dark o null, y `"light-gray"` si es light. Este valor se usa en `UserSettings.Load()` cuando el fichero de settings no existe o cuando el campo `theme` está vacío, garantizando que el primer arranque se adapta al modo del sistema.

**Selección inicial del tema en el picker de Settings**: el constructor de `SettingsWindowViewModel` inicializa `_selectedTheme` buscando en `Themes` el tema cuyo `Id` coincide con `settings.Theme`; si no hay coincidencia, usa el primero de la lista. La asignación se hace directamente al campo (no a la propiedad) para no disparar `OnSelectedThemeChanged` durante la inicialización.
