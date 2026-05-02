# Sticky Window Always-On-Top + Settings Survives Hotkey

## Objetivo

Dos mejoras independientes al comportamiento de ventanas:

1. **StickyWindow = always on top**: cuando `StickyWindow = true`, la ventana de búsqueda flota por encima de todas las demás ventanas del SO. El usuario puede interactuar normalmente con otras apps; Yottacast permanece visible encima.
2. **Settings sobrevive al hotkey**: cuando el hotkey oculta la ventana principal y la ventana de settings está abierta, settings no se cierra — en su lugar recibe el foco.

---

## Feature 1 — StickyWindow como always on top

### Comportamiento

| `StickyWindow` | `Topmost` de la ventana principal |
|---|---|
| `true` | `true` — flota sobre todas las demás ventanas |
| `false` | `false` — comportamiento normal de z-order |

El cambio se aplica en dos momentos:
- **Al arrancar**: tras crear `MainWindow`, se asigna `Topmost` según el valor actual de `StickyWindow`.
- **En tiempo real**: cuando el usuario cambia el toggle de Sticky en Settings, `Topmost` se actualiza inmediatamente sin reiniciar.

En Avalonia, `Window.Topmost = true` mapea a `NSFloatingWindowLevel` en macOS y al flag `WS_EX_TOPMOST` en Windows. No roba el foco — solo afecta al z-order visual.

### Invariantes

- `Topmost` siempre refleja el valor actual de `StickyWindow`. No hay estado desincronizado.
- Cambiar `StickyWindow` en Settings actualiza `Topmost` inmediatamente.

### Ficheros afectados

- `Yottacast/App.axaml.cs` — asigna `Topmost` al crear `mainWindow` y se suscribe a cambios de `StickyWindow`.
- `Yottacast.Core/Services/UserSettings.cs` — añadir evento `StickyWindowChanged` para notificar cambios en tiempo real.
- `Yottacast/ViewModels/SettingsWindowViewModel.cs` — disparar `StickyWindowChanged` al cambiar `StickyWindow`.

> **Verificar en:** `App.axaml.cs` — asignación inicial de `Topmost` y suscripción al evento. `UserSettings.StickyWindowChanged`. `SettingsWindowViewModel.OnStickyWindowChanged`.

---

## Feature 2 — Settings sobrevive al hotkey

### Causa del bug

En `App.axaml.cs`, cuando el hotkey oculta la ventana principal, se llama a `AppHandler.Instance.OnHide()`. En macOS, `OnHide()` llama a `activateWithOptions:` sobre la app que tenía el foco antes de abrir Yottacast, lo que desactiva Yottacast. Al desactivarse, la ventana de settings desaparece.

### Fix

En el bloque del hotkey handler que oculta la ventana principal, comprobar si settings está abierto:

- **Settings NO está abierto** → comportamiento actual: `window.Hide()` + `AppHandler.Instance.OnHide()`.
- **Settings SÍ está abierto** → `window.Hide()` + `_settingsWindow!.Activate()`. No se llama a `OnHide()`, por lo que la app anterior no recupera el foco y settings permanece visible y activo.

### Invariantes

- Si settings está abierto cuando el hotkey oculta la ventana principal, settings recibe el foco y permanece visible.
- Si settings NO está abierto, el comportamiento es idéntico al actual.
- `_previousApp` en `MacAppHandler` queda con un valor válido (no se limpia), por lo que el siguiente `OnHide()` puede restaurar el foco correctamente.

### Ficheros afectados

- `Yottacast/App.axaml.cs` — modificar el bloque del hotkey handler que llama a `OnHide()`.

> **Verificar en:** `App.axaml.cs` — bloque `if (window.IsVisible) { ... else { window.Hide(); OnHide(); } }` dentro de `RegisterGlobalHotKey`.
