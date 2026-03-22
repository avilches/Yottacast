# UI: Themes

Clase: `Yottacast.Services.ThemeService`

Lee `Themes/{name}.json`, aplica tokens en `Application.Current.Resources` en runtime.

`ThemeService.Apply(themeName)` — carga el JSON indicado. Si el fichero no existe o el parsing falla, registra un warning en el log y llama `ApplyBuiltinDefault()` como fallback.
`ThemeService.ApplyBuiltinDefault()` — aplica dark-default hardcodeado como fallback (no puede fallar).

All theme tokens are listed in `ThemeService.ApplyBuiltinDefault()` which also serves as the canonical default. Colors use Avalonia's `Color.TryParse` format; see any theme JSON file for examples.
Los JSON se copian al output vía `CopyToOutputDirectory=PreserveNewest`.

Available themes are the JSON files in `Yottacast/Themes/` (excluding `settings.json`).

**`ThemeOption` record**: `AvailableThemes()` devuelve `IReadOnlyList<ThemeOption>` donde cada entrada tiene `Id` (nombre de fichero sin extensión) y `DisplayName` (campo `"name"` del JSON; si falta o el parse falla, usa el `Id` como fallback).

**Campo `variant` en el JSON**: `Apply()` lee `json["variant"]` y asigna `app.RequestedThemeVariant` a `ThemeVariant.Light` si el valor es `"light"`, o `ThemeVariant.Dark` en cualquier otro caso.

**Aplicación al arranque**: `App.OnFrameworkInitializationCompleted()` llama `themeService.Apply(userSettings.Theme)` de forma síncrona antes de crear la `MainWindow`, por lo que el tema está activo antes de que cualquier control se renderice.

**Hot-swap en Settings**: `SettingsWindowViewModel.OnSelectedThemeChanged()` llama `_themeService.Apply(value.Id)` inmediatamente al cambiar el picker. El cambio es instantáneo sin reiniciar la aplicación.

**Metadata en JSON (author, url)**: todos los temas tienen `"author": ""` y `"url": ""`. `ThemeService` los ignora hoy; estarán disponibles cuando se implemente la descarga de temas.

**Gotcha — Colores mal formados ignorados silenciosamente**: `SetBrush()` usa `Color.TryParse`. Si el valor del color en el JSON no es un color válido, el brush no se asigna y el token conserva su valor anterior sin ningún error o aviso.

**Gotcha — Temas cargados síncronamente en SettingsWindow**: `SettingsWindowViewModel` llama `AvailableThemes()` en su constructor, que enumera los JSON de `Themes/` ordenados alfabéticamente por nombre de fichero y excluye `settings.json`. Si ninguno carga, añade `"dark-default"` como fallback.
