# UI: Themes / Keyboard / IsSearching

## Themes

Clase: `Yottacast.Services.ThemeService`

Lee `Themes/{name}.json`, aplica tokens en `Application.Current.Resources` en runtime.

`ThemeService.Apply(themeName)` — carga el JSON indicado.
`ThemeService.ApplyBuiltinDefault()` — aplica dark-default hardcodeado como fallback (no puede fallar).

Tokens: `Theme.*` (ej. `Theme.WindowBackground`). Colores: `#AARRGGBB` (no `#RRGGBBAA`).
Los JSON se copian al output vía `CopyToOutputDirectory=PreserveNewest`.

Temas incluidos: `dark-default`, `dark-raycast`, `dark-macos`, `light-blue`, `light-gray`.

**Metadata en JSON (author, url)**: todos los temas tienen `"author": ""` y `"url": ""`. `ThemeService` los ignora hoy; estarán disponibles cuando se implemente la descarga de temas.

**Gotcha — Temas cargados síncronamente en SettingsWindow**: `SettingsWindowViewModel` llama `LoadThemes()` en su constructor, que lee del disco los JSON de `Themes/`. Si ninguno carga, añade `"dark-default"` como fallback.

## Keyboard shortcuts (MainWindow)

- `ESC` con búsqueda en curso → para la búsqueda diferida (mantiene texto y resultados parciales)
- `ESC` sin búsqueda en curso y texto no vacío → limpia el texto
- `ESC` sin búsqueda y sin texto → oculta la ventana
- `↑` / `↓` → navega resultados
- `Enter` → activa resultado seleccionado
- `⌘,` → abre SettingsWindow (si MainWindow está visible)
- `ALT+Space` → global hotkey para mostrar/ocultar

**Gotcha — Window hide vs close**: `Hide()` en Escape (no `Close()`); `Show()` + `Activate()` restaura. Al ocultar la ventana el estado del ViewModel se preserva intacto: texto, resultados y búsquedas en curso continúan; al volver a mostrarla el usuario ve exactamente lo que había. El SettingsWindow evita duplicados: si ya está visible lo activa; si está oculto lo muestra; solo crea instancia nueva en el primer arranque o tras `Close()`.

**Gotcha — ALT+Space toggle con foco**: ALT+Space oculta la ventana solo si está visible **y activa** (`window.IsVisible && window.IsActive`). Si está visible pero sin foco (tapada por otra ventana), la trae al frente (`Show()` + `Activate()`) en lugar de ocultarla.

## Indicador de búsqueda en curso (IsSearching)

`MainWindowViewModel.IsSearching` es `true` mientras la fase diferida (`SearchDeferredAsync`) está activa. Se activa justo antes de iterar la fase diferida y se desactiva en el `finally` al completar, cancelar o fallar.

**Spinner en la UI**: cuando `IsSearching` es `true`, la search row muestra un `Ellipse` giratorio (`Classes="spinner"`, animación CSS en `Window.Styles`) en lugar del badge "ESC". Cuando `IsSearching` baja a `false`, la animación se detiene y el badge ESC reaparece (si el texto está vacío).

**`CancelDeferredSearch()`**: cancela solo la fase diferida sin tocar el texto ni la búsqueda instant. Llamado por el handler de ESC cuando `IsSearching == true`. Internamente cancela `_deferredCts`, que es un `CancellationTokenSource` enlazado al `ct` principal — si se teclea texto nuevo, el `ct` padre cancela ambas fases.

**`ShowNoResults`**: solo se activa si la búsqueda diferida completó sin cancelación (`completed = true`). Si se paró con ESC o por nueva búsqueda, los resultados parciales permanecen visibles sin mostrar "No results".

**Gotcha — `ResultItemViewModel.Shortcut`**: propiedad definida pero sin uso: nunca se asigna desde las fuentes de búsqueda ni se muestra en la UI. Placeholder para futuros atajos de teclado por resultado.
