# Historial de búsquedas

Yottacast guarda un historial de las búsquedas realizadas para permitir al usuario recuperar queries anteriores sin tener que reescribirlas. El historial se persiste en un fichero JSON independiente de los settings del usuario.

---

## 1. Qué se guarda y cuándo

Una entrada de historial se crea cada vez que el usuario completa una acción sobre una búsqueda o descarta el campo de texto. Concretamente, el historial se actualiza en los siguientes casos:

- **Enter** — el usuario selecciona un resultado y lo activa.
- **Cmd+C / Ctrl+C** — el usuario copia el resultado sin pegarlo.
- **Escape** — el usuario descarta la búsqueda actual (con o sin búsqueda en curso).
- **Clic / tap** — el usuario activa un resultado con el ratón.

El punto único de guardado es `MainWindowViewModel.CleanAndSaveHistory(actionName)`, que registra la query y limpia el campo de texto. Las queries en blanco o vacías se ignoran y no generan entrada.

Cada entrada contiene:

| Campo | Tipo | Descripción |
|---|---|---|
| `query` | string | Texto que el usuario había escrito |
| `actionName` | string? | Nombre de la acción ejecutada (`null` si fue Escape o no hay resultado) |
| `timestamp` | DateTime | Momento en que se registró la entrada |

> **Verificar en:** `HistoryService.Add()` — `Yottacast.Core/Services/HistoryService.cs`. `MainWindowViewModel.CleanAndSaveHistory()` — `Yottacast/ViewModels/MainWindowViewModel.cs`. Llamadas en `MainWindow.axaml.cs` (Enter, Escape, Cmd+C, tap).

---

## 2. Persistencia

El historial se almacena en un fichero JSON separado de `settings.json`:

| Plataforma | Ruta |
|---|---|
| macOS | `~/Library/Application Support/Yottacast/history.json` |
| Windows | `%APPDATA%\Yottacast\history.json` |

La ruta está centralizada en `AppPaths.HistoryFile`. El fichero se escribe en cada `Add()` y `Clear()`. Si el fichero no existe al arrancar, el historial comienza vacío sin error. Si la escritura falla (permisos, disco lleno), el error se loguea y la sesión continúa sin interrupciones.

**Invariantes:**
- El directorio padre se crea automáticamente si no existe.
- Si el fichero está corrupto o contiene JSON inválido, el historial se inicializa vacío y el error se loguea en Warning.
- El historial nunca bloquea el arranque ni la búsqueda.

> **Verificar en:** `HistoryService.Load()`, `HistoryService.Save()` — `Yottacast.Core/Services/HistoryService.cs`. Ruta en `AppPaths.HistoryFile` — `Yottacast.Core/AppPaths.cs`.

---

## 3. Límite de entradas

El número máximo de entradas se controla con `UserSettings.HistoryMaxItems` (valor por defecto: `AppDefaults.HistoryMaxItems = 100`, máximo configurable: 100). Cuando se supera el límite al añadir una entrada, las más antiguas se eliminan hasta quedar dentro del límite.

> **Verificar en:** `HistoryService.Add()` — trimming con `RemoveRange`. Constante en `AppDefaults.HistoryMaxItems` — `Yottacast.Core/AppDefaults.cs`.

---

## 4. Navegación por el historial

El usuario puede recuperar búsquedas anteriores desde el campo de texto usando las teclas de flecha:

| Tecla | Comportamiento |
|---|---|
| `↑` (sin resultados navegados) | Retrocede al historial (entrada más reciente primero) |
| `Ctrl+↑` | Retrocede al historial siempre, aunque el usuario haya navegado la lista de resultados |
| `Ctrl+↓` | Avanza hacia entradas más recientes del historial |
| `↑` (con resultados navegados) | Sube por la lista de resultados (comportamiento original) |
| `↓` | Baja por la lista de resultados (nunca navega historial) |

Después de navegar al historial, el cursor se posiciona al final del texto. Editar el texto mientras se navega el historial resetea el índice de navegación al siguiente cambio manual, de modo que las teclas de flecha vuelven al historial desde el principio.

**Invariantes:**
- `Ctrl+↑` siempre navega historial, independientemente del estado de la lista de resultados.
- `↓` nunca navega historial; siempre navega la lista de resultados.
- Editar el texto (cualquier cambio no originado por navegación de historial) resetea el índice de navegación.

> **Verificar en:** `MainWindowViewModel.NavigateHistoryBack/Forward()`, campo `_historyNavIndex`, flag `_navigatingHistory`, propiedad `UserNavigated` — `Yottacast/ViewModels/MainWindowViewModel.cs`. Manejo de `Key.Up`/`Key.Down` en `MainWindow.OnKeyDown()` — `Yottacast/Views/MainWindow.axaml.cs`.

---

## 5. Activar y desactivar el historial

El historial se puede desactivar completamente desde Settings → History. Cuando está desactivado (`EnableHistory = false`), `HistoryService.Add()` ignora todas las llamadas y no escribe nada al disco. El fichero existente no se borra al desactivar.

> **Verificar en:** guard al inicio de `HistoryService.Add()` — `Yottacast.Core/Services/HistoryService.cs`. Toggle en `SettingsWindowViewModel.OnEnableHistoryChanged()` — `Yottacast/ViewModels/SettingsWindowViewModel.cs`.

---

## 6. Settings — sección History

La ventana de Settings expone una sección History con los siguientes controles:

- **Toggle Enable/Disable** — activa o desactiva el guardado del historial.
- **Max history items** — número máximo de entradas a conservar (1–100). El cambio tiene efecto en la próxima entrada añadida; no recorta el historial existente retroactivamente.
- **Clear history** — elimina todas las entradas del historial y persiste el fichero vacío.
- **History log** — visor en tiempo real de las entradas guardadas, ordenadas de más reciente a más antigua, con formato `[yyyy-MM-dd HH:mm:ss] "query" → acción`. Se actualiza automáticamente al añadir o borrar entradas mediante el evento `HistoryService.Changed`.

> **Verificar en:** `SettingsWindowViewModel` (propiedades `EnableHistory`, `HistoryMaxItems`, `HistoryDisplayText`, `ClearHistoryCommand`, `OnHistoryChanged`, `BuildHistoryDisplayText`) — `Yottacast/ViewModels/SettingsWindowViewModel.cs`. Panel History — `Yottacast/Views/SettingsWindow.axaml`.
