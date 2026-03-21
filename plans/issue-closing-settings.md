# Issue: Cmd+W desde SettingsWindow — historial y solución definitiva

## Comportamiento actual (resuelto)

- Cmd+W en SettingsWindow → cierra la ventana y sale la app (comportamiento aceptado; NSMenu lo intercepta antes que Avalonia)
- Escape → no está implementado como atajo (SettingsWindow no tiene `OnKeyDown`)
- Abrir Settings vía Cmd+, → crea una nueva instancia cada vez (no se reutiliza)

## Causa raíz del problema original

Al pulsar Cmd+W con SettingsWindow activa:
1. **NSMenu intercepta el evento** antes de que Avalonia lo vea y despacha `performClose:` al NSWindow activo.
2. `performClose:` → `windowShouldClose:` en el delegate. El cancel de `OnClosing` (Avalonia) **no se propaga al nivel nativo** en Avalonia 11.3.12 / macOS 16.
3. El NSWindow se cierra nativamente aunque Avalonia crea estar cancelándolo.
4. macOS llama a `applicationShouldTerminateAfterLastWindowClosed:` → el proceso termina.

## La regla más importante: `ShutdownMode.OnExplicitShutdown` rompe el key handling de ventanas secundarias

**`desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown` causa PAL_SEHException (SIGABRT, exit 134) al pulsar CUALQUIER tecla en cualquier ventana secundaria (no-MainWindow) en Avalonia 11.3.12 + macOS 16.**

Este fue el hallazgo más costoso. El proceso:
1. Se añadió `ShutdownMode.OnExplicitShutdown` para evitar que el proceso terminara cuando SettingsWindow se cerraba.
2. Esto cambió el AppDelegate de Avalonia internamente y rompió el pipeline de key events para ventanas secundarias.
3. El crash ocurría antes de llegar a ningún código C# (crash nativo en Avalonia's ObjC delegate).

**Nunca usar `ShutdownMode.OnExplicitShutdown`.** El proceso se mantiene vivo mientras MainWindow esté "hidden" (no cerrada), porque `OnLastWindowClose` (default) solo actúa cuando una ventana se *cierra* (`Window.Close()`), no cuando se *oculta* (`Window.Hide()`).

## Reglas derivadas para SettingsWindow

### NO hacer
- ❌ `desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown` — rompe key events en ventanas secundarias
- ❌ `OnClosing` con `e.Cancel = true` en SettingsWindow — crea estado inconsistente entre Avalonia y AppKit; el cancel no se propaga al nivel nativo en Avalonia 11.3.12
- ❌ `SystemDecorations="None"` sin `TransparencyLevelHint="Transparent"` en una ventana secundaria — PAL_SEHException en cualquier tecla
- ❌ Manipular `NSApp mainMenu` (setMainMenu:nil, vacío, o DisableNativeMenus) — PAL_SEHException; Avalonia 11.3.12 asume que el NSMenu existe y tiene estructura

### SÍ hacer
- ✅ Crear SettingsWindow fresca en cada apertura (no reutilizar con singleton)
- ✅ Dejar que Cmd+W cierre la ventana y salga la app — comportamiento aceptado
- ✅ Mantener `MainWindow.OnClosing` con `e.Cancel = true; Hide()` — para MainWindow (ventana principal, `desktop.MainWindow`) esto sí funciona
- ✅ `NSApplicationActivationPolicyAccessory` solo — no tocar el NSMenu

## Intentos fallidos documentados

| Intento | Resultado |
|---|---|
| `setMainMenu: nil` (P/Invoke) | CRASH — PAL_SEHException al pulsar cualquier modificador |
| `setMainMenu:` con NSMenu vacío | CRASH igual |
| `MacOSPlatformOptions.DisableNativeMenus = true` | CRASH igual |
| `ShutdownMode.OnExplicitShutdown` | PAL_SEHException en **cualquier tecla** en ventanas secundarias |
| `OnClosing` con `e.Cancel = true` en SettingsWindow | Estado inconsistente Avalonia/AppKit → crash posterior |
| `SystemDecorations="None"` sin `TransparencyLevelHint` | PAL_SEHException en cualquier tecla |
| `SystemDecorations="None"` con `TransparencyLevelHint="Transparent"` | PAL_SEHException en cualquier tecla (no resuelve el problema de ventana secundaria) |
| Supresión de Meta keys en SharpHook (`_settingsWindowActive`) | No resuelve el crash; además Activated/Deactivated no son fiables en Avalonia 11.3.12/macOS 16 |

## Por qué MainWindow no crashea y SettingsWindow sí

MainWindow funciona con key events porque:
- Es `desktop.MainWindow` — recibe tratamiento especial del AppDelegate de Avalonia
- Tiene `SystemDecorations="None"` + `TransparencyLevelHint="Transparent"` — configuración testeada

SettingsWindow es una ventana secundaria con decoraciones nativas. En Avalonia 11.3.12 + macOS 16 + `NSApplicationActivationPolicyAccessory`, las ventanas secundarias decoradas tienen problemas con el pipeline de key events cuando se altera el AppDelegate (via `ShutdownMode`) o el NSMenu.

## Estado del código relevante

```
App.axaml.cs:       Sin ShutdownMode override (usa OnLastWindowClose por defecto)
                    OpenSettings() crea new SettingsWindow cada vez
App.axaml.cs:       MacAppHandler.OnFrameworkInitializationCompleted() → solo setActivationPolicy:1
MainWindow.axaml.cs: OnClosing con e.Cancel=true; Hide() — funciona para MainWindow
SettingsWindow.axaml.cs: Sin OnClosing, sin OnKeyDown override
```
