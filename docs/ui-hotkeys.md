# Hotkeys

## Keyboard shortcuts (MainWindow)

- `ESC` con búsqueda en curso → para la búsqueda diferida (mantiene texto y resultados parciales)
- `ESC` sin búsqueda en curso y texto no vacío → limpia el texto
- `ESC` sin búsqueda y sin texto → oculta la ventana
- `↑` / `↓` → navega resultados (wrapping circular: de último ítem vuelve al primero y viceversa)
- `Enter` → activa resultado seleccionado, limpia el texto y oculta la ventana; si `result.PasteAfterActivate` es `true`, después llama `AppHandler.OnHide()` y `SimulatePasteAsync()`
- `←` / `→` → interceptados en fase **tunnel** antes de llegar al TextBox; si el `SelectedResult` tiene `OnLeft`/`OnRight`, se invoca el handler del ítem y la tecla queda consumida
- `⌘,` → abre SettingsWindow (si MainWindow está visible)
- `ALT+Space` → global hotkey para mostrar/ocultar
- `⌘W` (macOS) / `Ctrl+F4` (Windows) / `Ctrl+W` (Linux) → oculta la ventana en lugar de cerrarla (`CloseWindowShortcut`)

**Tunnel handler para flechas**: `MainWindow` registra `OnTunnelKeyDown` con `RoutingStrategies.Tunnel`. Las teclas ←/→ se consumen aquí si el ítem tiene handler, impidiendo que el TextBox mueva el cursor. Las teclas ↑/↓ también pasan por tunnel: si el ítem devuelve `true` en `OnUp`/`OnDown`, la ventana no navega la lista; si devuelve `false`, el bubble handler de la ventana continúa con la navegación normal.

**ALT+Space consumido en bubble phase**: además de la supresión a nivel OS vía `e.SuppressEvent`, `MainWindow.OnKeyDown` también marca `e.Handled = true` al detectar `Key.Space + Alt`. Esto evita que macOS emita un beep cuando la supresión no está disponible (p.ej. sin permiso de Accesibilidad).

**`NotifyUserNavigated` y selección automática**: al navegar con ↑/↓, `SelectNext` llama `vm.NotifyUserNavigated()` que activa el flag `_userNavigated`. Mientras este flag está activo, `RefreshResults` no fuerza la selección al resultado de tipo Calculator/Converter; en su lugar preserva el ítem seleccionado anteriormente o selecciona el primero de la lista. El flag se resetea a `false` con cada cambio de `SearchText`.

**`OnClosing` — cancel siempre**: `MainWindow.OnClosing` cancela cualquier intento de cierre nativo (`e.Cancel = true`) y llama `Hide()`. Esto cubre tanto el atajo `CloseWindowShortcut` como los cierres originados por macOS al cerrar SettingsWindow.

**SearchBox habilitado según visibilidad**: el handler de `IsVisibleProperty` en `MainWindow` desactiva `SearchBox.IsEnabled` cuando la ventana se oculta y lo reactiva con foco cuando vuelve a mostrarse.

**Gotcha — Window hide vs close**: `Hide()` en Escape (no `Close()`); `Show()` + `Activate()` restaura. Al ocultar la ventana el estado del ViewModel se preserva intacto: texto, resultados y búsquedas en curso continúan; al volver a mostrarla el usuario ve exactamente lo que había. El SettingsWindow evita duplicados: si ya está visible (`IsVisible: true`) lo activa sin crear nada; si no está visible, crea siempre una nueva instancia de `SettingsWindow` con un `SettingsWindowViewModel` (transient) nuevo.

**Gotcha — ALT+Space toggle con foco**: ALT+Space oculta la ventana solo si está visible **y activa** (`window.IsVisible && window.IsActive`). Si está visible pero sin foco (tapada por otra ventana), la trae al frente (`Show()` + `Activate()`) en lugar de ocultarla.

## Global hotkey — implementación

La hotkey global se registra en `App.RegisterGlobalHotKey()` usando `SharpHook.SimpleGlobalHook`. Se usa `SimpleGlobalHook` (síncrono) deliberadamente: `TaskPoolGlobalHook` ejecuta los handlers en otros threads donde `e.SuppressEvent = true` no tiene efecto.

`e.SuppressEvent = true` se activa cuando la combinación coincide, impidiendo que el evento llegue a cualquier otra app (ni a Yottacast ni a la app en foco). En macOS requiere permiso de Accesibilidad; sin él se ignora silenciosamente sin error.

La comparación de teclas usa `KeyNameMap`, un diccionario estático construido en `BuildKeyNameMap()` que cubre A–Z, 0–9, F1–F12 y teclas especiales (Space, Enter, Tab, Backspace, Delete, Escape). Cualquier nombre de tecla no incluido mapea a `KeyCode.VcUndefined`.

## Hotkey capture en Settings

El flujo de captura de hotkey es:

1. Click sobre el área del hotkey → `SettingsWindow.OnHotkeyAreaPointerPressed()` → `vm.StartHotkeyCapture()` (`IsCapturingHotkey = true`). El evento se marca `Handled` para no propagar.
2. `HotkeyDisplayText` (propiedad derivada) muestra `"Press keys…"` mientras `IsCapturingHotkey` es `true`.
3. Click fuera del área → `SettingsWindow.OnPointerPressed()` detecta `IsCapturingHotkey: true` y llama `CancelHotkeyCapture()`, restaurando `HotkeyText` al valor guardado.
4. Al pulsar una tecla durante la captura: si es solo un modificador, se ignora; si es ESC, se cancela; cualquier otra combinación crea un `HotkeyConfig`, lo serializa, lo guarda en `UserSettings` y baja `IsCapturingHotkey`.
