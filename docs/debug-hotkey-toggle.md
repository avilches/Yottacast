# Debug: hotkey show/hide se atasca tras pulsar varias veces

## Estado actual (2026-04-21)

Fixes implementados:
1. `_isToggling` + `try/finally` — evita que el flag quede bloqueado si hay excepción
2. `_hotkeyDown` — requiere soltar la tecla antes de aceptar el siguiente toggle (guard simple: `if (_hotkeyDown) return;`)
3. `IsSelf` por PID — evita capturar Yottacast como `_previousApp` cuando macOS no ha procesado la activación anterior
4. `activateWithOptions:` eliminado de `ShowWindow` — solo se llama en `OnHide`; hacerlo en Show causaba que macOS activara la app anterior antes de `makeKeyWindow`, haciendo que `makeKeyWindow` fallara silenciosamente y la ventana dejara de recibir teclas

---

## Causa raíz identificada

### Flujo normal de toggle

```
Hook thread → _isToggling=false check → _isToggling=true → InvokeAsync()
UI thread   → Hide()/ShowWindow() → _isToggling=false
```

### Por qué se atasca

`_isToggling` se queda a `true` de forma permanente. Esto ocurre si el lambda del `InvokeAsync` lanza una excepción antes de llegar a `_isToggling = false`. `InvokeAsync` sin `await` swallows la excepción silenciosamente — el flag nunca se limpia.

**Sospechoso principal**: `ShowWindow()` en `MacAppHandler` hace varias llamadas a ObjC (`GetFrontmostApp`, `activateWithOptions:`, `makeKeyWindow`) y Avalonia (`window.Show()`). Bajo key-repeat rápido, macOS puede estar en un estado transitorio donde alguna de estas falla o produce un comportamiento inesperado que resulta en excepción en la capa de Avalonia.

**Fix pendiente**: envolver el cuerpo del lambda en `try/finally` para garantizar que `_isToggling = false` siempre se ejecute:

```csharp
Dispatcher.UIThread.InvokeAsync(() => {
    try {
        var window = desktop.MainWindow;
        if (window is null) return;
        if (window.IsVisible) {
            window.Hide();
            AppHandler.Instance.OnHide();
        } else {
            AppHandler.Instance.ShowWindow(window);
        }
    } finally {
        _isToggling = false;
    }
});
```

---

## Cambios implementados (todos en main)

### 1. `_isToggling` guard — `App.axaml.cs`

Reemplazó un debounce de 300ms por un flag que bloquea nuevos toggles mientras el anterior está en curso. El hook thread lee/escribe el flag (`volatile bool`), el UI thread lo limpia al terminar.

```csharp
private volatile bool _isToggling = false;
// hook: if (_isToggling) return; _isToggling = true; InvokeAsync(() => { ...; _isToggling = false; });
```

**Por qué no debounce**: el debounce ignora pulsaciones arbitrariamente durante 300ms. El flag solo bloquea mientras la operación está en curso (más correcto).

### 2. `_positionDirty` — `MainWindow.axaml.cs`

`SavePosition()` (llamada en cada hide) solo escribe a disco si el usuario arrastró la ventana. Sin drag → cero I/O en disco en cada toggle.

- `UpdatePositionInMemory()` marca `_positionDirty = true` solo cuando la posición cambia
- `_positionDirty = false` tras `ApplyPositionOnShow` (reposicionar en show no es movimiento del usuario)
- `SavePosition()` comprueba el flag antes de llamar a `_settings.Save()`

**Motivación**: el fichero de settings puede estar en iCloud. Escribirlo en cada hide (potencialmente 30 veces/segundo con key-repeat) saturaba la sincronización.

### 3. `activateWithOptions:` solo en `OnHide` — `MacAppHandler.cs`

`ShowWindow` NO llama a `activateWithOptions:`. Solo captura el frontmost app y llama a `makeKeyWindow`. La restauración de foco ocurre únicamente en `OnHide`.

**Por qué**: llamar `activateWithOptions:(previousApp)` antes de `makeKeyWindow` activa esa app, que a su vez intenta hacer key su propia ventana. La solicitud de key window llega al window server de macOS después de la nuestra, ganando la carrera. Resultado: nuestra ventana está visible pero no recibe teclado. El usuario lo percibe como "no funciona" porque el hotkey siguiente hace hide (ventana visible) y el siguiente show... está vacío.

**Trade-off aceptado**: los semáforos de la app anterior pueden ponerse grises mientras Yottacast está abierto (Avalonia llama `activateIgnoringOtherApps:YES` en `window.Show()`). Es preferible a la ventana sin foco de teclado.

### 4. `IsSelf` con PID — `MacAppHandler.cs`

En `ShowWindow`, antes de capturar `_previousApp`, se comprueba si la app frontmost es Yottacast mismo. Si lo es, no se sobreescribe `_previousApp` — se mantiene el valor anterior (la app real del usuario).

```csharp
var frontmost = GetFrontmostApp();
if (!IsSelf(frontmost)) {
    if (_previousApp != IntPtr.Zero) ObjcRelease(_previousApp);
    _previousApp = ObjcRetain(frontmost);
}
```

`IsSelf` compara el PID del proceso con el de la app via `processIdentifier` (retorna `int`):

```csharp
private static bool IsSelf(IntPtr app) {
    if (app == IntPtr.Zero) return false;
    return ObjcMsgSendInt(app, SelRegisterName("processIdentifier")) == Environment.ProcessId;
}
```

**Por qué PID y no `isEqual:`**: `isEqual:` retorna `BOOL` (1 byte en Objective-C). En ARM64, `objc_msgSend` con retorno `bool` en P/Invoke tiene problemas de marshaling — puede devolver garbage. El PID retorna `int` (pid_t = 32 bits), que se mapea limpiamente a `int` en C#.

**Motivación**: al hacer toggle rápido, `activateWithOptions:` de `OnHide()` es asíncrono en el window server de macOS. `GetFrontmostApp()` puede devolvernos Yottacast como app frontmost antes de que macOS procese la activación. Si capturamos Yottacast como `_previousApp`, el siguiente `OnHide()` activa Yottacast en vez de la app real, dejando los semáforos grises.

---

## Lo que NO hay que tocar

- `ApplyPositionOnShow()` se llama en cada show. No hay I/O de disco — solo lee `_settings.WindowX/Y` de memoria. Optimizarlo para saltárselo cuando "no cambia la pantalla" rompió el toggle rápido (causa desconocida, posiblemente interacción con Avalonia internals). No merece la pena.
- `UserSettings.Save()` es síncrono. El único caller problemático era `SavePosition()`, que ya está protegido con `_positionDirty`.

---

## Archivos relevantes

| Fichero | Qué contiene |
|---|---|
| `Yottacast/App.axaml.cs` | `_isToggling` + `RegisterGlobalHotKey` |
| `Yottacast/Services/MacAppHandler.cs` | `ShowWindow`, `OnHide`, `_previousApp` |
| `Yottacast/Views/MainWindow.axaml.cs` | `ApplyPositionOnShow`, `SavePosition`, `_positionDirty` |
| `Yottacast.Core/Services/UserSettings.cs` | `Save()` |
| `docs/ui-hotkeys.md` | Invariante del `_isToggling` documentado |
| `docs/user-settings.md` | Comportamiento del guardado de posición documentado |
