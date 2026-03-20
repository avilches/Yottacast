# Issue: Cmd+W desde SettingsWindow cierra toda la aplicación (macOS)

## Síntoma original
Al pulsar Cmd+W con SettingsWindow activa, la aplicación entera se cierra (el proceso termina), en lugar de cerrar solo la ventana de Settings.

## Estado actual del código

**Comportamiento aceptado**: pulsar Cmd+W en SettingsWindow cierra la aplicación (NSMenu intercepta antes que Avalonia; no se lucha contra ello). Pulsar Escape oculta SettingsWindow.

Los siguientes cambios están aplicados en el código:

| Fichero | Cambio aplicado |
|---|---|
| `App.axaml.cs` | `ShutdownMode = OnExplicitShutdown` — el proceso no se cierra por eventos de ventana |
| `SettingsWindow.axaml.cs` | `OnClosing` override: `e.Cancel = true; Hide()` — cancela cualquier cierre nativo y oculta |
| `SettingsWindow.axaml.cs` | `OnKeyDown` para Cmd+W llama `Hide()` en lugar de `Close()` |
| `App.axaml.cs` | `OpenSettings()` reutiliza la instancia oculta en lugar de crear una nueva |
| `MainWindow.axaml.cs` | `OnClosing` override: `e.Cancel = true; Hide()` — MainWindow nunca se cierra, solo se oculta |
| `MainWindow.axaml.cs` | Guard en `OnKeyDown` para Cmd+W: llama `Hide()` antes de `base.OnKeyDown` |
| `AppHandler` / handlers | `CloseWindowModifier` → `CloseWindowShortcut` devuelve `(KeyModifiers, Key)` por plataforma: Mac=`(Meta,W)`, Win=`(Ctrl,F4)`, Linux=`(Ctrl,W)` |

## Causa raíz diagnosticada (pero no resuelta)
El problema está en la capa de **AppKit/NSApplication**, no en Avalonia:

1. Al pulsar Cmd+W, **NSMenu intercepta el evento antes de que Avalonia lo vea** y despacha `performClose:` directamente al NSWindow activo.
2. `performClose:` llama a `windowShouldClose:` en el delegate del NSWindow, que debería mapear al `OnClosing` de Avalonia con `e.Cancel = true`. **Aparentemente esto no funciona** en Avalonia 11.3.12 en macOS, o el cancel no se propaga correctamente al nivel nativo.
3. El NSWindow se cierra. Avalonia lo elimina de su lista de ventanas abiertas.
4. Con MainWindow oculta (no cerrada), macOS llama a `applicationShouldTerminateAfterLastWindowClosed:` en el AppDelegate. Avalonia responde YES por defecto en esta versión → proceso termina.
5. `ShutdownMode.OnExplicitShutdown` en Avalonia no anula esta llamada nativa de AppKit.

## Intentos fallidos

### Intento 1: `setMainMenu: nil` (P/Invoke)
Eliminar el menú de NSApplication para que no haya "Close Window" Cmd+W.
- **Resultado**: **CRASH** — `PAL_SEHException` al pulsar cualquier tecla modificadora. Avalonia crashea internamente al intentar buscar key-equivalents en un menú nil.

### Intento 2: `setMainMenu:` con NSMenu vacío (P/Invoke)
Mismo objetivo, pero pasando un NSMenu vacío (sin ítems) en lugar de nil.
- **Resultado**: **CRASH igual** — mismo `PAL_SEHException` al pulsar Command. El menu vacío sin la estructura esperada por Avalonia también causa el crash.

### Intento 3: `MacOSPlatformOptions.DisableNativeMenus = true`
Opción oficial de Avalonia para deshabilitar el menú nativo.
- **Resultado**: **CRASH igual** — mismo `PAL_SEHException` al pulsar Command. Internamente hace algo equivalente que también rompe el key handling de Avalonia.

> **Patrón**: cualquier manipulación del NSMenu principal causa el mismo crash. Avalonia 11.3.12 asume que el NSMenu existe y tiene cierta estructura al procesar modificadores.

## Próximos enfoques a explorar

### Opción A — Usar `Escape` como atajo de cierre (mínimo riesgo)
Añadir `Escape` como atajo adicional (o sustituto) de Cmd+W para cerrar Settings. Escape no pasa por NSMenu, llega directamente a Avalonia como cualquier otro KeyDown.
- Pros: trivial de implementar, sin riesgos.
- Contras: no es el atajo estándar de macOS para cerrar ventanas.

### Opción B — `class_replaceMethod` via ObjC runtime para `applicationShouldTerminateAfterLastWindowClosed:`
Inyectar directamente en la clase del AppDelegate de Avalonia un método que retorne NO (BOOL 0).
```csharp
// En MacAppHandler.OnFrameworkInitializationCompleted():
var nsApp = ObjcMsgSend(...);
var delegate_ = ObjcMsgSend(nsApp, SelRegisterName("delegate"));
var delegateClass = ObjectGetClass(delegate_);
// Reemplazar el método con una implementación que retorna 0 (NO)
ClassReplaceMethod(delegateClass,
    SelRegisterName("applicationShouldTerminateAfterLastWindowClosed:"),
    &ShouldNotTerminate,   // [UnmanagedCallersOnly] static byte ShouldNotTerminate(...) => 0
    "B@:@");
```
- Pros: ataca la causa raíz (el terminate de AppKit).
- Contras: requiere `unsafe` + `[UnmanagedCallersOnly]`, y añadir P/Invokes para `object_getClass` y `class_replaceMethod`. Riesgo de incompatibilidad si Avalonia actualiza su delegate.

### Opción C — Override `windowShouldClose:` en el NSWindow delegate
Similar a B pero a nivel de ventana: que `windowShouldClose:` retorne siempre NO para SettingsWindow. Requiere obtener el delegate del NSWindow concreto y reemplazar el método.
- Pros: más granular que B.
- Contras: complejidad similar, y hay que obtener el NSWindow nativo desde Avalonia (posible via `ToplevelImpl`).

### Opción D — NSEvent local monitor (interceptar antes que NSMenu)
Registrar un `addLocalMonitorForEventsMatchingMask:handler:` que capture Cmd+W antes de que NSMenu lo procese. Si el handler retorna nil, NSMenu nunca lo ve.
- Pros: intercepta en el sitio correcto del pipeline de eventos.
- Contras: requiere implementar ObjC blocks desde C# (complejo sin librerías auxiliares).

### Opción E — Suprimir en SharpHook global hook
El global hook de SharpHook ya funciona con Accessibility permission (se usa para el hotkey principal). Podemos añadir supresión de Cmd+W cuando SettingsWindow está activa:
```csharp
// En App.axaml.cs RegisterGlobalHotKey():
if (e.Data.KeyCode == KeyCode.VcW &&
    (mask.HasFlag(EventMask.LeftMeta) || mask.HasFlag(EventMask.RightMeta)) &&
    _settingsWindow is { IsVisible: true, IsActive: true }) {
    e.SuppressEvent = true;
    Dispatcher.UIThread.InvokeAsync(() => _settingsWindow?.Hide());
    return;
}
```
- Pros: usa infraestructura ya existente y funcional.
- Contras: requiere Accessibility permission (ya requerida), y `IsActive` en Avalonia puede tener matices en macOS.

## Recomendación
Implementar **Opción A** (Escape) de forma inmediata como solución funcional. Explorar **Opción B** o **E** en paralelo para tener también Cmd+W como atajo nativo correcto.