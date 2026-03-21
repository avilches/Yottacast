# UI: Themes / Keyboard / IsSearching

## Themes

Clase: `Yottacast.Services.ThemeService`

Lee `Themes/{name}.json`, aplica tokens en `Application.Current.Resources` en runtime.

`ThemeService.Apply(themeName)` — carga el JSON indicado. Si el fichero no existe o el parsing falla, registra un warning en el log y llama `ApplyBuiltinDefault()` como fallback.
`ThemeService.ApplyBuiltinDefault()` — aplica dark-default hardcodeado como fallback (no puede fallar).

All theme tokens are listed in `ThemeService.ApplyBuiltinDefault()` which also serves as the canonical default. Colors use Avalonia's `Color.TryParse` format; see any theme JSON file for examples.
Los JSON se copian al output vía `CopyToOutputDirectory=PreserveNewest`.

Available themes are the JSON files in `Yottacast/Themes/` (excluding `settings.json`).

**Metadata en JSON (author, url)**: todos los temas tienen `"author": ""` y `"url": ""`. `ThemeService` los ignora hoy; estarán disponibles cuando se implemente la descarga de temas.

**Gotcha — Colores mal formados ignorados silenciosamente**: `SetBrush()` usa `Color.TryParse`. Si el valor del color en el JSON no es un color válido, el brush no se asigna y el token conserva su valor anterior sin ningún error o aviso.

**Gotcha — Temas cargados síncronamente en SettingsWindow**: `SettingsWindowViewModel` llama `AvailableThemes()` en su constructor, que enumera los JSON de `Themes/` ordenados alfabéticamente por nombre de fichero y excluye `settings.json`. Si ninguno carga, añade `"dark-default"` como fallback.

## Keyboard shortcuts (MainWindow)

- `ESC` con búsqueda en curso → para la búsqueda diferida (mantiene texto y resultados parciales)
- `ESC` sin búsqueda en curso y texto no vacío → limpia el texto
- `ESC` sin búsqueda y sin texto → oculta la ventana
- `↑` / `↓` → navega resultados
- `Enter` → activa resultado seleccionado
- `⌘,` → abre SettingsWindow (si MainWindow está visible)
- `ALT+Space` → global hotkey para mostrar/ocultar
- `⌘W` (macOS) / `Ctrl+F4` (Windows) / `Ctrl+W` (Linux) → oculta la ventana en lugar de cerrarla (`CloseWindowShortcut`)

**Gotcha — Window hide vs close**: `Hide()` en Escape (no `Close()`); `Show()` + `Activate()` restaura. Al ocultar la ventana el estado del ViewModel se preserva intacto: texto, resultados y búsquedas en curso continúan; al volver a mostrarla el usuario ve exactamente lo que había. El SettingsWindow evita duplicados: si ya está visible lo activa; si está oculto lo muestra; solo crea instancia nueva en el primer arranque o tras `Close()`.

**Gotcha — ALT+Space toggle con foco**: ALT+Space oculta la ventana solo si está visible **y activa** (`window.IsVisible && window.IsActive`). Si está visible pero sin foco (tapada por otra ventana), la trae al frente (`Show()` + `Activate()`) en lugar de ocultarla.

## Indicador de búsqueda en curso (IsSearching)

`MainWindowViewModel.IsSearching` es `true` mientras la fase diferida (`SearchDeferredAsync`) está activa. Se activa justo antes de iterar la fase diferida y se desactiva en el `finally` al completar, cancelar o fallar.

**Spinner en la UI**: cuando `IsSearching` es `true`, la search row muestra un `Ellipse` giratorio (`Classes="spinner"`, animación CSS en `Window.Styles`) en lugar del badge "ESC". La animación pulsa la opacidad (duración definida en `MainWindow.axaml`) con `PlaybackDirection="Alternate"`. Cuando `IsSearching` baja a `false`, la animación se detiene y el badge ESC reaparece (si el texto está vacío).

**`CancelDeferredSearch()`**: cancela solo la fase diferida sin tocar el texto ni la búsqueda instant. Llamado por el handler de ESC cuando `IsSearching == true`. Internamente cancela `_deferredCts`, que es un `CancellationTokenSource` enlazado al `ct` principal — si se teclea texto nuevo, el `ct` padre cancela ambas fases.

**`ShowNoResults`**: solo se activa si la búsqueda diferida completó sin cancelación (`completed = true`). Si se paró con ESC o por nueva búsqueda, los resultados parciales permanecen visibles sin mostrar "No results".

**Auto-selección de resultado Calculator/Converter**: `RefreshResults()` busca el primer resultado cuya categoría sea `"Calculator"` o `"Converter"`. Si lo encuentra y el usuario no ha navegado manualmente (`_userNavigated == false`), ese resultado queda seleccionado automáticamente. Si el usuario ya navegó con ↑↓, la selección previa se preserva.

**Gotcha — `ResultItemViewModel.Shortcut`**: propiedad definida pero sin uso: nunca se asigna desde las fuentes de búsqueda ni se muestra en la UI. Placeholder para futuros atajos de teclado por resultado.

**Gotcha — `ALT+Space` consumido por MainWindow**: MainWindow intercepta `ALT+Space` explícitamente para evitar el beep nativo de macOS cuando la app está en background pero la ventana recibe el evento.

**Gotcha — `ResultItemViewModel.OnUp()`/`OnDown()`**: devuelven `bool`. `true` significa que el ítem ha consumido la tecla (p.ej. navegación interna del grid emoji); `false` delega la navegación de lista a la ventana. Ver `MainWindow.axaml.cs` para el handler.
