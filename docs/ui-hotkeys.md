# Hotkeys y atajos de teclado

## 1. Atajo global para mostrar/ocultar el launcher

La aplicacion define un unico atajo de teclado global (por defecto `ALT+Space`) que funciona desde cualquier aplicacion. Su comportamiento depende del estado de la ventana y del modo **Sticky**:

### Modo Sticky (default, `StickyWindow = true`)

| Estado de la ventana          | Accion                                                              |
|-------------------------------|--------------------------------------------------------------------|
| Visible **y** activa (en foco) | Se oculta y devuelve el foco a la app anterior (`OnHide()`)         |
| Visible pero sin foco         | Se oculta sin restaurar foco (no se trajo al frente)               |
| Oculta                        | Se muestra y se activa                                              |

En modo sticky la ventana permanece visible cuando pierde el foco (no se auto-oculta al desactivarse). El hotkey global, sin embargo, siempre oculta la ventana si esta visible, este o no enfocada: cuando esta visible sin foco se hace `Hide()` directo sin `OnHide()` (no habia foco que restaurar); cuando esta visible y enfocada se hace `Hide()` mas `OnHide()` para devolver el foco a la app anterior.

### Modo no-sticky / Alfred-style (`StickyWindow = false`)

| Estado de la ventana | Accion                                         |
|----------------------|------------------------------------------------|
| Visible              | Se oculta y devuelve el foco a la app anterior |
| Oculta               | Se muestra y se activa                         |

En modo no-sticky la ventana se oculta automaticamente en cuanto pierde el foco (comportamiento tipo Alfred). El hotkey siempre oculta si esta visible porque al perder el foco se habrá ocultado antes de que el usuario pueda pulsarlo.

La ventana no se oculta automaticamente cuando quien toma el foco es la propia ventana de Settings de Yottacast. Si el usuario pulsa el hotkey estando la ventana principal o la de Settings en primer plano, la ventana principal se oculta pero Settings permanece abierta - solo el usuario la puede cerrar manualmente.

El setting se configura en Settings → General ("Sticky window") y se persiste en `UserSettings.StickyWindow`.

Invariantes:

- El atajo global nunca llega a otras aplicaciones: se suprime a nivel de sistema operativo. En macOS esto requiere permiso de Accesibilidad; sin el, la supresion se ignora silenciosamente.
- Como mecanismo de respaldo, la ventana principal tambien marca `e.Handled = true` al detectar `Space + Alt` en su handler de teclado, evitando que macOS emita un beep cuando la supresion a nivel OS no esta disponible.
- El hook global usa `SimpleGlobalHook` (sincrono). Esto es deliberado: `TaskPoolGlobalHook` ejecuta handlers en otros threads donde `e.SuppressEvent = true` no tiene efecto.
- El handler usa un flag `_isToggling` para ignorar pulsaciones mientras el toggle anterior esta en curso. Esto evita encolar multiples operaciones show/hide antes de que macOS procese la anterior, lo que puede dejar el NSWindow en un estado donde `makeKeyWindow` ya no funciona.
- Al mostrar la ventana, si `SearchText` está vacío, el modo de búsqueda se resetea siempre a `SearchMode.All`. Si `SearchText` no está vacío, el modo activo se preserva.

La combinacion de teclas configurable se almacena como `HotkeyConfig` (record con flags `Alt`, `Ctrl`, `Shift`, `Meta` y `KeyName`). La comparacion contra el evento del hook usa un diccionario estatico `KeyNameMap` que cubre A-Z, 0-9, F1-F12 y teclas especiales (Space, Enter, Tab, Backspace, Delete, Escape). Cualquier tecla no incluida en el mapa se trata como `KeyCode.VcUndefined` y nunca coincidira.

> **Verificar en:** `App.axaml.cs` (`RegisterGlobalHotKey`, `BuildKeyNameMap`, handler `Deactivated`), `MainWindowViewModel.OnWindowShow()`, `UserSettings.StickyWindow`, `UserSettings.ParsedHotkey`, `HotkeyConfig` en `Yottacast.Core/Platform/HotkeyConfig.cs`

---

## 2. Comportamiento de la tecla Escape (ventana principal)

Escape aplica esta logica en cascada, evaluada en este orden de prioridad:

| Condicion                                        | Accion                                                   |
|--------------------------------------------------|----------------------------------------------------------|
| Hay un editor/preview abierto (`IsEditorOpen`)    | Si hay dialogo de cambios sin guardar, lo cancela; en otro caso cierra el editor (`RequestClose`) |
| Hay un menu de opciones abierto (`IsOptionsMenuOpen`) | Cierra el menu                                         |
| Hay una busqueda diferida en curso (`IsSearching`) | Cancela la busqueda diferida y limpia el texto           |
| El campo de texto no esta vacio                   | Limpia el texto                                          |
| El campo de texto esta vacio y no hay busqueda    | Oculta la ventana                                        |

Invariantes:

- Escape nunca cierra la aplicacion, solo oculta la ventana (salvo el caso de cerrar editor/menu, que no llega a ocultar).
- El modo de busqueda activo (All / Files / Clipboard) no cambia al pulsar Escape; persiste hasta que el usuario lo cambie explicitamente.
- Al ocultar con Escape, el estado del ViewModel se preserva intacto (resultados, busquedas pendientes). Al volver a mostrar la ventana, el usuario ve el estado tal como lo dejo.

> **Verificar en:** `MainWindow.axaml.cs` (`OnKeyDown`, case `Key.Escape`). El caso de cerrar el editor en modo edicion se intercepta antes, en `OnTunnelKeyDown` (`if (e.Key == Key.Escape)` dentro del bloque `isEditMode`).

---

## 3. Navegacion de resultados con flechas

### Flechas arriba/abajo

Las teclas arriba y abajo navegan la lista de resultados con wrapping circular: del ultimo item se vuelve al primero y viceversa.

Invariantes:

- La navegacion se procesa en la fase **bubble** del evento de teclado.
- Antes de que el bubble handler actue, la fase **tunnel** consulta al item seleccionado: si este tiene un handler `OnUp`/`OnDown` y devuelve `true`, la tecla se consume ahi y la navegacion de lista no ocurre. Si devuelve `false`, el flujo continua normalmente.
- Al navegar con flechas se activa un flag `_userNavigated` que impide que `RefreshResults` fuerce la seleccion automatica al resultado de tipo Calculator/Converter. Mientras este flag esta activo, se preserva el item previamente seleccionado (si aun existe en la lista) o se selecciona el primero.
- El flag `_userNavigated` se resetea a `false` cada vez que cambia `SearchText`.

### Flechas izquierda/derecha

Las teclas izquierda y derecha se interceptan en la fase **tunnel** antes de que lleguen al TextBox. Si el item seleccionado tiene un handler `OnLeft`/`OnRight`, se invoca y se asigna `e.Handled` a su valor de retorno (`e.Handled = onLeft()`/`onRight()`), igual que `Key.Up`/`Key.Down`. Como `OnLeft`/`OnRight` son `Func<bool>?`, si el item consume la tecla (`true`) esta no llega al TextBox y el caret del SearchBox no se mueve.

> **Verificar en:** `MainWindow.axaml.cs` (`OnTunnelKeyDown` switch de navegacion de grid, `OnKeyDown`, `SelectNext`), `MainWindowViewModel.cs` (`NotifyUserNavigated`, `RefreshResults`, `OnSearchTextChanged`), `BaseResultItemViewModel` (`OnLeft`, `OnRight`, `OnUp`, `OnDown`, todos `Func<bool>?`)

---

## 4. Activacion de un resultado (Enter y click)

Al pulsar Enter o hacer click/tap sobre un resultado seleccionado:

1. Se ejecuta la accion `Enter` del item seleccionado.
2. Se limpia el texto de busqueda.
3. Se oculta la ventana.
4. Si el item tiene `PasteAfterActivate = true`, ademas se devuelve el foco a la app anterior (`AppHandler.OnHide`) y se simula un pegado (`SimulatePasteAsync`).

**Ejecutar sin cerrar**: con la accion `Enter` disponible, `Cmd+Enter` (macOS) / `Ctrl+Enter` (Windows/Linux) ejecuta la accion sin ocultar la ventana (`AsKeepOpen()`). Lo mismo aplica al `Cmd/Ctrl+doble-click` (ver seccion de raton).

Invariantes:

- `PasteAfterActivate` solo lo usan items de tipo emoji. Tras copiar el emoji al portapapeles, el launcher se oculta y lo pega automaticamente en la app destino.
- El detector de `Cmd/Ctrl+Enter` en `OnKeyDown` (case `Key.Return`) y el de `Cmd/Ctrl+doble-click` en `OnResultsDoubleTapped` comprueban el modificador via `AppHandler.Instance.MetaKeyModifier` (Cmd = Meta en macOS, Ctrl en Windows/Linux), nunca con `KeyModifiers.Meta` hardcodeado.

> **Estado: incompleto** - `SimulatePasteAsync` solo lo overridean `MacAppHandler` (Cmd+V) y `WindowsAppHandler` (Ctrl+V). En Linux se hereda el no-op de la clase base (`AppHandler.SimulatePasteAsync => Task.CompletedTask`), por lo que el paste automatico de emoji no funciona en Linux.

> **Verificar en:** `MainWindow.axaml.cs` (`OnKeyDown` case `Key.Return`, `OnResultsDoubleTapped`), `BaseResultItemViewModel.PasteAfterActivate`, `AppHandler.SimulatePasteAsync` y overrides en `MacAppHandler.cs` / `WindowsAppHandler.cs`, `EmojiSearch` en `Yottacast.Core/Search/Emoji/EmojiSearch.cs`

---

## 5. Atajo para cerrar la ventana

Cada plataforma define un atajo para "cerrar ventana":

| Plataforma | Atajo       |
|------------|-------------|
| macOS      | Cmd+W       |
| Windows    | Ctrl+F4     |
| Linux      | Ctrl+W      |

La ventana principal intercepta este atajo y lo redirige a `Hide()` en lugar de cerrar la aplicacion. Ademas, `OnClosing` cancela siempre cualquier intento de cierre nativo (`e.Cancel = true`) y llama `Hide()`. Esto cubre tanto el atajo de teclado como cierres originados por el sistema operativo (por ejemplo, macOS envia `performClose:` a la MainWindow cuando se cierra la SettingsWindow).

Invariantes:

- La ventana principal nunca se destruye durante la vida de la aplicacion. Solo se oculta y se vuelve a mostrar.
- `Hide()` preserva el estado completo; `Show()` + `Activate()` restaura la ventana con el ViewModel intacto.

> **Verificar en:** `MainWindow.axaml.cs` (`OnKeyDown` primer bloque con `CloseWindowShortcut`, `OnClosing`), `AppHandler.cs` (`CloseWindowShortcut`), `MacAppHandler.cs`, `WindowsAppHandler.cs`, `LinuxAppHandler.cs`

---

## 6. Salir de la aplicacion (Cmd+Q en macOS)

En macOS, `Cmd+Q` cierra la aplicacion completamente. macOS intercepta este atajo a nivel de `NSApplication` (via `applicationShouldTerminate:`) antes de que llegue al handler de teclado de la ventana. Avalonia traduce esta señal al evento `ShutdownRequested` del lifetime, que llama `Environment.Exit(0)` para terminar el proceso.

Invariantes:

- `Cmd+Q` termina el proceso; no se puede deshacer ni recuperar la ventana.
- El resto de atajos de cierre (`Cmd+W`, `Escape`) siguen ocultando la ventana sin cerrar la aplicacion.

> **Verificar en:** `App.axaml.cs` (handler de `desktop.ShutdownRequested`), `MacAppHandler.cs` (`QuitShortcut`)

---

## 7. Abrir preferencias (Cmd+,)

Pulsar `Cmd+,` mientras la ventana principal esta visible abre la ventana de preferencias. Si la SettingsWindow ya esta visible, simplemente se activa (se trae al frente) sin crear una nueva instancia. Si no esta visible, se crea una nueva instancia de `SettingsWindow` con un `SettingsWindowViewModel` transient.

> **Verificar en:** `MainWindow.axaml.cs` (`OnKeyDown` case `Key.OemComma`), `App.axaml.cs` (`OpenSettings`)

---

## 8. Copiar resultado (Cmd+C)

`Cmd+C` (macOS) / `Ctrl+C` (Windows/Linux) copia el valor del resultado seleccionado **sin cerrar la ventana**. Aparece un mensaje breve en el área de `SearchHint` durante 1.5 s.

No existe un handler dedicado al copy. La accion fluye por el loop generico de action-hotkeys al final de `OnTunnelKeyDown`: cada `ResultAction` con `Hotkey` se compara contra el evento y la accion de copia matchea `ActionHotkey.MetaC`. `AppHandler.CopyShortcut` no es un handler sino una propiedad `(KeyModifiers, Key)` (Cmd+C en macOS, Ctrl+C en el resto) de la que se deriva `MetaKeyModifier`. El copy se bloquea si hay texto seleccionado en el SearchBox (para no robar el Cmd+C de copiar texto del campo).

| Tipo | Qué copia | Mensaje |
|---|---|---|
| Apps | Path del bundle | "Path copied!" |
| Archivos | Path del fichero | "Path copied!" |
| Calculadora | Resultado numérico | "Result copied!" |
| Conversor | Celda seleccionada | "Result copied!" |
| Diccionario | Primera definición | "Definition copied!" |
| Emoji | El emoji (sin paste) | "Emoji {char} copied!" (ej. "Emoji 😀 copied!") |

> **Verificar en:** `MainWindow.axaml.cs` (`OnTunnelKeyDown`, loop generico de action-hotkeys), `AppHandler.cs` (`CopyShortcut`, `MetaKeyModifier`), `ActionHotkey.cs` (`MetaC`), `MainWindowViewModel.cs` (`ShowCopiedMessage`).

---

## 9. Control del SearchBox segun visibilidad

Cuando la ventana principal se oculta, el SearchBox se desactiva (`IsEnabled = false`). Cuando se muestra de nuevo, se reactiva y recibe el foco automaticamente. Adicionalmente, al ocultar la ventana se desactiva el flag `IsAltPressed` del ViewModel para evitar estados residuales.

> **Verificar en:** `MainWindow.axaml.cs` (`OnPropertyChanged` para `IsVisibleProperty`)

---

## 10. Ocultacion automatica del cursor del raton

Mientras el usuario escribe, el cursor del raton se oculta automaticamente para no distraer. Se restaura cuando el usuario mueve el raton a una posicion diferente de la que tenia al ocultarse. El sistema rastrea la posicion en coordenadas de pantalla para distinguir movimientos reales de movimientos sinteticos causados por cambios de tamano de la ventana (cuando aparecen o desaparecen resultados).

Invariantes:

- Solo las teclas no-modificadoras ocultan el cursor.
- Si el cursor esta oculto, los eventos de movimiento del raton sobre la lista de resultados no seleccionan items (se ignoran hasta que el cursor se restaure).

> **Verificar en:** `MainWindow.axaml.cs` (`HideCursor`, `ShowCursor`, `TrackOrShowCursor`, `OnTunnelPointerMoved`, `OnResultsPointerMovedForDrag`, `OnOptionsMenuPointerMoved`), `AppHandler.cs` (`HideCursor`, `ShowCursor`), `MacAppHandler.cs` (usa `NSCursor.setHiddenUntilMouseMoves`)

---

## 11. Captura de hotkey en preferencias

El campo hotkey muestra siempre 4 badges de modificadores (⌃/Ctrl, ⌥/Alt, ⇧, ⌘/Meta con simbolos especificos por OS) y el nombre de la tecla. Los badges activos (el modificador forma parte del hotkey guardado) se muestran con opacidad plena; los inactivos, atenuados.

El flujo de captura:

1. **Inicio** - Click sobre el campo: borde se pone rojo, todos los badges se apagan, texto central pasa a "Press a modifier…". Aparece un botón ✕ para cancelar. El color de captura (`#FF3B30`) esta hardcodeado en `SettingsWindow.axaml` (la ventana de Settings no usa el sistema de temas; ver `docs/ui-themes.md`).
2. **Modificador pulsado** - El badge correspondiente se ilumina en tiempo real y el texto pasa a "Press a key…". Si se sueltan todos los modificadores, vuelve a "Press a modifier…".
3. **Tecla pulsada** - Con al menos un modificador sostenido, se construye el `HotkeyConfig`, se valida (no prohibido), se guarda y termina la captura.
4. **Cancelacion** - Escape, click en el botón ✕, o click fuera del campo: cancela sin guardar.

Combinaciones prohibidas (ignoradas silenciosamente, no se pueden capturar):
- macOS: `Meta+Q` (Cmd+Q = salir), `Meta+W` (Cmd+W = cerrar ventana)
- Windows: `Ctrl+F4`, `Alt+F4`
- Linux: `Ctrl+W`

Si el usuario pulsa el mismo hotkey que ya tenia, el hook global detecta que Settings esta capturando y no suprime el evento, dejando que llegue a `SettingsWindow.OnKeyDown`.

**Auto-reparacion al inicio**: si el hotkey guardado en JSON coincide con una combinacion prohibida (p. ej. el usuario edito el fichero a mano), se reemplaza por `HotkeyConfig.Default` antes de registrar el hook global.

> **Verificar en:** `SettingsWindow.axaml.cs` (`OnKeyDown`, `OnKeyUp`, `OnPointerPressed`), `Yottacast/Views/Settings/SettingsGeneralView.axaml.cs` (`OnHotkeyAreaPointerPressed`), `SettingsWindowViewModel.cs` (`StartHotkeyCapture`, `CancelHotkeyCapture`, `UpdateCapturingModifiers`, `ProcessKeyCapture`, `BadgeXxxActive`, `HotkeyKeyText`), `AppHandler.cs` (`ForbiddenHotkeys`, `IsForbidden`, `XxxSymbol`), `App.axaml.cs` (auto-repair antes de `RegisterGlobalHotKey`)

---

## Resumen de atajos

| Atajo                        | Contexto               | Accion                                    |
|------------------------------|------------------------|-------------------------------------------|
| ALT+Space (configurable)     | Global                 | Mostrar/ocultar el launcher               |
| Escape                       | Ventana principal       | Cancelar busqueda / limpiar / ocultar     |
| Flecha arriba / abajo        | Ventana principal       | Navegar resultados (circular)             |
| Flecha izquierda / derecha   | Ventana principal       | Delegada al item si tiene handler         |
| Cmd+Opt+← / Cmd+Opt+→       | Ventana principal       | Navegar modos (All / Files / Clipboard)   |
| Enter                        | Ventana principal       | Activar resultado seleccionado            |
| Cmd+, (macOS)                | Ventana principal       | Abrir preferencias                        |
| Cmd+C / Ctrl+C              | Ventana principal       | Copiar valor del resultado seleccionado (sin cerrar) |
| Cmd+W / Ctrl+F4 / Ctrl+W    | Ventana principal       | Ocultar ventana (no cerrar)               |
| Cmd+Q (macOS)               | Ventana principal       | Cerrar la aplicacion completamente        |
| Cmd+Shift+F / Ctrl+Shift+F  | Modo emoji              | Marcar/desmarcar emoji como favorito      |
