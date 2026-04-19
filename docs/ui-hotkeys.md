# Hotkeys y atajos de teclado

## 1. Atajo global para mostrar/ocultar el launcher

La aplicacion define un unico atajo de teclado global (por defecto `ALT+Space`) que funciona desde cualquier aplicacion. Su comportamiento depende del estado de la ventana:

| Estado de la ventana          | Accion                                           |
|-------------------------------|--------------------------------------------------|
| Visible **y** activa (en foco) | Se oculta y devuelve el foco a la app anterior   |
| Visible pero sin foco         | Se trae al frente y se activa                    |
| Oculta                        | Se muestra y se activa                           |

Invariantes:

- El atajo global nunca llega a otras aplicaciones: se suprime a nivel de sistema operativo. En macOS esto requiere permiso de Accesibilidad; sin el, la supresion se ignora silenciosamente.
- Como mecanismo de respaldo, la ventana principal tambien marca `e.Handled = true` al detectar `Space + Alt` en su handler de teclado, evitando que macOS emita un beep cuando la supresion a nivel OS no esta disponible.
- El hook global usa `SimpleGlobalHook` (sincrono). Esto es deliberado: `TaskPoolGlobalHook` ejecuta handlers en otros threads donde `e.SuppressEvent = true` no tiene efecto.

La combinacion de teclas configurable se almacena como `HotkeyConfig` (record con flags `Alt`, `Ctrl`, `Shift`, `Meta` y `KeyName`). La comparacion contra el evento del hook usa un diccionario estatico `KeyNameMap` que cubre A-Z, 0-9, F1-F12 y teclas especiales (Space, Enter, Tab, Backspace, Delete, Escape). Cualquier tecla no incluida en el mapa se trata como `KeyCode.VcUndefined` y nunca coincidira.

> **Verificar en:** `App.axaml.cs` (`RegisterGlobalHotKey`, `BuildKeyNameMap`), `UserSettings.ParsedHotkey`, `HotkeyConfig` en `Yottacast.Core/Platform/HotkeyConfig.cs`

---

## 2. Comportamiento de la tecla Escape (ventana principal)

Escape tiene tres niveles de accion, evaluados en este orden de prioridad:

| Condicion                                        | Accion                                                   |
|--------------------------------------------------|----------------------------------------------------------|
| Hay una busqueda diferida en curso (`IsSearching`) | Cancela la busqueda diferida y limpia el texto           |
| El campo de texto no esta vacio                   | Limpia el texto                                          |
| El campo de texto esta vacio y no hay busqueda    | Oculta la ventana                                        |

Invariantes:

- Escape nunca cierra la aplicacion, solo oculta la ventana.
- Al ocultar con Escape, el estado del ViewModel se preserva intacto (resultados, busquedas pendientes). Al volver a mostrar la ventana, el usuario ve el estado tal como lo dejo.

> **Verificar en:** `MainWindow.axaml.cs` (`OnKeyDown`, case `Key.Escape`)

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

Las teclas izquierda y derecha se interceptan en la fase **tunnel** antes de que lleguen al TextBox. Si el item seleccionado tiene un handler `OnLeft`/`OnRight`, se invoca y, si devuelve `true`, la tecla queda consumida (el cursor del TextBox no se mueve). Si no hay handler o devuelve `false`, el TextBox procesa la tecla normalmente.

> **Verificar en:** `MainWindow.axaml.cs` (`OnTunnelKeyDown`, `OnKeyDown`, `SelectNext`), `MainWindowViewModel.cs` (`NotifyUserNavigated`, `RefreshResults`, `OnSearchTextChanged`), `BaseResultItemViewModel` (`OnLeft`, `OnRight`, `OnUp`, `OnDown`)

---

## 4. Activacion de un resultado (Enter y click)

Al pulsar Enter o hacer click/tap sobre un resultado seleccionado:

1. Se ejecuta `OnActivate` del item.
2. Se limpia el texto de busqueda.
3. Se oculta la ventana.
4. Si el item tiene `PasteAfterActivate = true`, ademas se devuelve el foco a la app anterior (`AppHandler.OnHide`) y se simula un pegado (`SimulatePasteAsync`, que envia Cmd+V en macOS o Ctrl+V en Windows).

Invariantes:

- Si el item no tiene `OnActivate`, no ocurre ninguna accion.
- `PasteAfterActivate` solo lo usan items de tipo emoji. Tras copiar el emoji al portapapeles, el launcher se oculta y lo pega automaticamente en la app destino.

> **Verificar en:** `MainWindow.axaml.cs` (`OnKeyDown` case `Key.Return`, `OnResultsTapped`), `BaseResultItemViewModel.PasteAfterActivate`, `EmojiSearch` en `Yottacast.Core/Search/Emoji/EmojiSearch.cs`

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

## 6. Abrir preferencias (Cmd+,)

Pulsar `Cmd+,` mientras la ventana principal esta visible abre la ventana de preferencias. Si la SettingsWindow ya esta visible, simplemente se activa (se trae al frente) sin crear una nueva instancia. Si no esta visible, se crea una nueva instancia de `SettingsWindow` con un `SettingsWindowViewModel` transient.

> **Verificar en:** `MainWindow.axaml.cs` (`OnKeyDown` case `Key.OemComma`), `App.axaml.cs` (`OpenSettings`)

---

## 7. Control del SearchBox segun visibilidad

Cuando la ventana principal se oculta, el SearchBox se desactiva (`IsEnabled = false`). Cuando se muestra de nuevo, se reactiva y recibe el foco automaticamente. Adicionalmente, al ocultar la ventana se desactiva el flag `IsAltPressed` del ViewModel para evitar estados residuales.

> **Verificar en:** `MainWindow.axaml.cs` (`OnPropertyChanged` para `IsVisibleProperty`)

---

## 8. Ocultacion automatica del cursor del raton

Mientras el usuario escribe, el cursor del raton se oculta automaticamente para no distraer. Se restaura cuando el usuario mueve el raton a una posicion diferente de la que tenia al ocultarse. El sistema rastrea la posicion en coordenadas de pantalla para distinguir movimientos reales de movimientos sinteticos causados por cambios de tamano de la ventana (cuando aparecen o desaparecen resultados).

Invariantes:

- Solo las teclas no-modificadoras ocultan el cursor.
- Si el cursor esta oculto, los eventos de movimiento del raton sobre la lista de resultados no seleccionan items (se ignoran hasta que el cursor se restaure).

> **Verificar en:** `MainWindow.axaml.cs` (`HideCursor`, `ShowCursor`, `TrackOrShowCursor`, `OnResultsPointerMoved`), `AppHandler.cs` (`HideCursor`, `ShowCursor`), `MacAppHandler.cs` (usa `NSCursor.setHiddenUntilMouseMoves`)

---

## 9. Captura de hotkey en preferencias

El flujo para que el usuario cambie el atajo global desde la ventana de preferencias es:

1. Click sobre el area del hotkey: inicia la captura (`IsCapturingHotkey = true`). El texto cambia a "Press keys...".
2. Click fuera del area: cancela la captura y restaura el valor guardado.
3. Pulsar una tecla durante la captura: si es solo un modificador (Alt, Ctrl, Shift, Meta), se ignora. Si es Escape, se cancela. Cualquier otra combinacion construye un `HotkeyConfig`, lo serializa, lo guarda en `UserSettings` y finaliza la captura.

**Nota:** El metodo `ProcessKeyCapture` en `SettingsWindowViewModel` implementa la logica del paso 3, pero actualmente no esta conectado desde la vista (`SettingsWindow` no invoca este metodo en ningun handler de teclado). El paso 3 no esta funcional hasta que se conecte.

> **Verificar en:** `SettingsWindow.axaml.cs` (`OnHotkeyAreaPointerPressed`, `OnPointerPressed`), `SettingsWindowViewModel.cs` (`StartHotkeyCapture`, `CancelHotkeyCapture`, `ProcessKeyCapture`)

---

## Resumen de atajos

| Atajo                        | Contexto               | Accion                                    |
|------------------------------|------------------------|-------------------------------------------|
| ALT+Space (configurable)     | Global                 | Mostrar/ocultar el launcher               |
| Escape                       | Ventana principal       | Cancelar busqueda / limpiar / ocultar     |
| Flecha arriba / abajo        | Ventana principal       | Navegar resultados (circular)             |
| Flecha izquierda / derecha   | Ventana principal       | Delegada al item si tiene handler         |
| Enter                        | Ventana principal       | Activar resultado seleccionado            |
| Cmd+, (macOS)                | Ventana principal       | Abrir preferencias                        |
| Cmd+W / Ctrl+F4 / Ctrl+W    | Ventana principal       | Ocultar ventana (no cerrar)               |
