# Historial del portapapeles (Clipboard History)

Yottacast captura automáticamente el texto que el usuario copia al portapapeles y lo almacena en un historial local. El historial es buscable y permite pegar entradas anteriores directamente en la app activa.

---

## 1. Captura de texto copiado

Un monitor de portapapeles (`IClipboardMonitor`) se ejecuta en background desde el arranque. Hace polling cada 500 ms y dispara el evento `TextCopied` cuando detecta texto nuevo.

| Plataforma | Mecanismo |
|---|---|
| macOS | `NSPasteboard.generalPasteboard.changeCount` via P/Invoke Objective-C |
| Windows | `OpenClipboard / GetClipboardData(CF_UNICODETEXT)` via Win32 |

El monitor solo captura texto plano (`NSStringPboardType` en macOS, `CF_UNICODETEXT` en Windows). Imágenes, ficheros u otros tipos se ignoran.

Las implementaciones concretas son `MacClipboardMonitor` y `WindowsClipboardMonitor` en `Yottacast/Services/`. Los selectores Objective-C se cachean como `static readonly` para evitar llamadas repetidas a `sel_registerName` en cada tick.

> **Verificar en:** `IClipboardMonitor` - `Yottacast.Core/Services/IClipboardMonitor.cs`. `MacClipboardMonitor` - `Yottacast/Services/MacClipboardMonitor.cs`. `WindowsClipboardMonitor` - `Yottacast/Services/WindowsClipboardMonitor.cs`. Constante de intervalo en `AppDefaults.ClipboardMonitorIntervalMs`.

---

## 2. Store: persistencia y límites

`ClipboardHistoryStore` mantiene la lista en memoria y la persiste en JSON.

**Ruta del fichero:**

| Plataforma | Ruta |
|---|---|
| macOS | `~/Library/Application Support/Yottacast/clipboard-history.json` |
| Windows | `%APPDATA%\Yottacast\clipboard-history.json` |

La ruta está centralizada en `AppPaths.ClipboardHistoryFile`.

**Deduplicación:** si el mismo texto se copia de nuevo, la entrada existente se mueve al principio de la lista con el `CopiedAt` actualizado. No se crea un duplicado.

**Límites aplicados en cada `Add`:**
- Entradas con `CopiedAt` anterior al corte (`now - MaxDays`) se eliminan.
- Si la lista supera `MaxEntries`, se eliminan las entradas sobrantes del final (las más antiguas).

`MaxEntries` y `MaxDays` son propiedades públicas mutables del store, inicializadas a `AppDefaults.ClipboardHistoryMaxEntries` (200) y `AppDefaults.ClipboardHistoryMaxDays` (30).

**Wiring de límites desde Settings:** los valores de `UserSettings.ClipboardHistoryMaxEntries` / `ClipboardHistoryMaxDays` se propagan al store en `App.SetupClipboardMonitor`: una vez al arranque y de nuevo en cada `SearchSettingsChanged`. Tras copiar los valores se invoca `store.ApplyLimitsNow()`, que recorta las entradas que sobran inmediatamente (sin esperar al siguiente `Add`) y persiste/notifica `EntriesChanged` solo si algo cambió. Al bajar `MaxEntries` o `MaxDays` en Settings, el historial se acorta en caliente.

**Persistencia:**
- `Add` y `RecordUsage` usan guardado con debounce de 1 segundo (`AppDefaults.ClipboardHistoryDebounceMs`).
- `Remove` cancela cualquier debounce pendiente (`CancelPendingSave`) y llama `FlushAsync()` directamente, sin debounce, para garantizar que el borrado sobrevive a un crash y no es pisado por un guardado debounced anterior.
- Todas las escrituras a disco se serializan con un `SemaphoreSlim` interno: dos `SaveAsync` concurrentes (p. ej. un `Add` debounced y un flush inmediato de `Remove`) nunca escriben el fichero `*.tmp` compartido a la vez.
- La escritura usa fichero temporal (`*.tmp`) con `File.Move(..., overwrite: true)` para evitar corrupción.
- Si el fichero no existe al arrancar, el historial comienza vacío sin error.
- Si el JSON está corrupto, se loguea en Warning y el historial arranca vacío.

**Orden de arranque:** `App` espera (`await`) a `ClipboardHistoryStore.LoadAsync()` antes de iniciar el monitor de portapapeles (`LoadClipboardThenStartMonitorAsync`). Así se evita una carrera en la que el primer poll del monitor haría `Add(textoActual)` y a continuación `LoadAsync` reemplazaría toda la lista, descartando esa entrada.

**Invariantes:**
- El directorio padre se crea automáticamente si no existe.
- El store nunca bloquea el arranque ni la búsqueda.
- `GetAll()` devuelve una copia inmutable de la lista (thread-safe via `Lock`).

> **Verificar en:** `ClipboardHistoryStore` (incluido `ApplyLimitsNow`, `CancelPendingSave`, `_saveGate`) - `Yottacast.Core/Search/Clipboard/ClipboardHistoryStore.cs`. Wiring de límites y orden de arranque en `App.SetupClipboardMonitor` y `App.LoadClipboardThenStartMonitorAsync` - `Yottacast/App.axaml.cs`. Constantes en `AppDefaults.ClipboardHistoryMaxEntries`, `ClipboardHistoryMaxDays`, `ClipboardHistoryDebounceMs`. Ruta en `AppPaths.ClipboardHistoryFile`.

---

## 3. Búsqueda y scoring

`ClipboardHistorySearch` implementa `IInstantSearchSource` y `ISearchModeSource`. Las entradas se filtran y ordenan en memoria sin I/O.

**Con query vacía:** se devuelven todas las entradas en orden de recencia (`score = ClipboardHistoryUnfilteredBaseScore - índice`), respetando el límite de `limit` si se especifica.

**Con query no vacía:** se filtran las entradas que contienen la query (case-insensitive) y se preserva el orden de recencia del store (las más recientes primero). El score sigue siendo `ClipboardHistoryUnfilteredBaseScore - índice` sobre la sublista filtrada; el orden de fecha no se altera.

**Formato en UI:**
- `Title`: texto de la entrada con saltos de línea sustituidos por `·`. No hay truncación en C#; el truncado visual lo gestiona AXAML con `TextTrimming="CharacterEllipsis"`.
- `Subtitle`: `"From clipboard, {tiempo relativo}"` (ej. `"From clipboard, 3h ago"`). En modo solo portapapeles el subtítulo no se muestra en la lista; la información aparece en la barra de estado del panel de preview.
- `Category`: vacío (`""`). Los items de portapapeles no muestran categoría ni score de debug en la columna derecha (`ScoreDisplayText` y `ScoreTooltipText` devuelven `""`).

> **Verificar en:** `ClipboardHistorySearch.Search()`, `BuildResult()` - `Yottacast.Core/Search/Clipboard/ClipboardHistorySearch.cs`. `ClipboardResultItemViewModel` - `Yottacast.Core/ViewModels/ClipboardResultItemViewModel.cs`.

---

## 4. Acciones

Cada entrada del historial expone tres acciones:

| Acción | Hotkey | Comportamiento |
|---|---|---|
| **Paste** | Enter | Copia el texto al portapapeles, registra uso (`RecordUsage`), cierra la ventana y simula Cmd+V / Ctrl+V en la app anterior (`PasteAfterClose = true`) |
| **Preview** | Cmd+P | Abre o cierra el panel de preview para este item. La acción tiene `Execute = () => {}` (no-op); la lógica real la gestiona el handler de teclado en `MainWindow.axaml.cs` |
| **Delete** | Supr | Elimina la entrada del historial (`store.Remove`), sin cerrar la ventana; la lista se refresca automáticamente vía `EntriesChanged → ResultChanged` |

> **Verificar en:** acciones en `ClipboardHistorySearch.BuildResult()`. Comportamiento de `PasteAfterClose` en `AppHandler`. `store.Remove()` dispara `EntriesChanged` que propaga `ResultChanged` a `MainWindowViewModel`.

---

## 5. Modos de búsqueda

`ClipboardHistorySearch` implementa `ISearchModeSource` con `IsActiveIn(mode)`:

La propiedad `ClipboardSearchVisibility` (en `UserSettings`) es de tipo `SearchSourceVisibility`, cuyos valores son `Always` / `ModeOnly` / `Disabled`:

| `ClipboardSearchVisibility` (tipo `SearchSourceVisibility`) | `SearchMode.All` | `SearchMode.Clipboard` |
|---|---|---|
| `Always` | activo | inactivo |
| `ModeOnly` | inactivo | activo |
| `Disabled` | inactivo | inactivo |

Cuando la visibilidad es `Always` y la query está vacía, las entradas reciben score `1000 - índice`, lo que las sitúa por encima de la mayoría de otras fuentes. Este comportamiento es intencionado cuando el usuario activa el modo `Always`.

La hotkey dedicada (`UserSettings.ClipboardHotkey`) puede configurarse para activar directamente `SearchMode.Clipboard` sin pasar por el modo `All`.

> **Verificar en:** `ClipboardHistorySearch.IsActiveIn()`. Registro del modo en `App.axaml.cs`. Hotkey dedicada en `AppHandler`.

---

## 6. Settings

La sección Clipboard History en Settings expone:

| Propiedad | Valor por defecto | Descripción |
|---|---|---|
| `ClipboardSearchVisibility` (tipo `SearchSourceVisibility`) | `Disabled` | Visibilidad: `Disabled` (Off), `Always` (modo All), `ModeOnly` (solo modo Clipboard) |
| `ClipboardHotkey` | `null` | Hotkey dedicada para activar el modo Clipboard; `null` = sin hotkey dedicada |
| `ClipboardHistoryMaxEntries` | `200` | Número máximo de entradas a conservar; se aplica al store en caliente |
| `ClipboardHistoryMaxDays` | `30` | Días máximos que se conserva una entrada; se aplica al store en caliente |

El monitor de portapapeles solo captura cuando `ClipboardSearchVisibility != Disabled`. Con `Disabled`, el monitor se para y no se almacena nada nuevo.

`ClipboardHistoryMaxEntries` y `ClipboardHistoryMaxDays` se editan en Settings, se persisten en `UserSettings` y se propagan al `ClipboardHistoryStore` (a `store.MaxEntries` / `store.MaxDays`) en cada `SearchSettingsChanged`, aplicándose de inmediato vía `store.ApplyLimitsNow()`. Ver detalle en la sección 2.

**Nota:** cambiar la hotkey dedicada tiene efecto inmediato - el handler lee `settings.ParsedClipboardHotkey` en cada evento del hook global, sin necesidad de reiniciar.

> **Verificar en:** `UserSettings` - `Yottacast.Core/Services/UserSettings.cs`. `SearchSourceVisibility` (tipo de `ClipboardSearchVisibility`) - `Yottacast.Core/Search/SearchSourceVisibility.cs`. `SettingsWindowViewModel` (propiedades `ClipboardSearchVisibility`, `ClipboardHistoryMaxEntries`, `ClipboardHistoryMaxDays`, estado de captura de hotkey) - `Yottacast/ViewModels/SettingsWindowViewModel.cs`. Panel Clipboard History - `Yottacast/Views/Settings/SettingsClipboardView.axaml`.

---

## 7. Panel de preview

Al seleccionar cualquier item de portapapeles, el panel de preview se abre automáticamente mostrando el texto completo. El texto siempre wrappea (sin scroll horizontal).

**Barra de estado:** en la parte inferior del panel se muestra el número de palabras, el número de caracteres y la hora exacta de copia (ej. `"42 words · 287 chars · Copied Today 16:45"`). Esta información no se muestra en el item de la lista; solo aparece en la barra de estado del preview.

**Cmd+P como toggle:** Cmd+P cierra el panel si está abierto, o lo abre si está cerrado, tanto para items de portapapeles como para ficheros de texto. Si el usuario cierra con Cmd+P y luego navega a un nuevo item de portapapeles, el panel se reabre automáticamente.

**Cierre por selección nula:** navegar a un resultado que no es de portapapeles ni fichero de texto cierra el panel.

> **Verificar en:** `EditorPanelViewModel.LoadTextContent()` (wordWrap, clipboardStatusText) - `Yottacast.Core/ViewModels/EditorPanelViewModel.cs`. `EditorPanelView.axaml` (Row 3 clipboard status bar). `MainWindowViewModel.OnSelectedResultChanged()` - auto-apertura del preview. Handler Cmd+P unificado en `MainWindow.axaml.cs`.

---

## 8. Comportamiento en modo solo portapapeles

Cuando `ClipboardSearchVisibility = ModeOnly` y el usuario activa el modo Clipboard (`SearchMode.Clipboard`):

- **Lista de resultados:** el subtítulo (`"From clipboard, …"`) no se muestra y el icono se oculta; los items son de una sola línea muy compacta, con la altura ajustada al texto. En clipboard-mode el `ListBoxItem` y su `ContentPresenter` interno quedan con `MinHeight=0` y sin margen, y la altura de la fila la fija el `MinHeight` del Grid de fila (`Grid.result-row`) desde el token de tema `Theme.Results.ClipboardMode.RowHeight`; el título usa el `FontSize` del token `Theme.Results.ClipboardMode.Title.Size`. La altura se controla desde el Grid interno (no desde el `Padding` del item) porque la fila seleccionada sobreescribe su `Padding` con la `ContentPadding` de la barra de seleccion, asi que el Grid aplica por igual a filas seleccionadas y no seleccionadas. Todo esto se aplica vía los estilos `ListBox.clipboard-mode ...` (mismo mecanismo que la clase enlazada a `ClipboardModeActive`), no por bindings por item. Detalles a tener en cuenta: (1) tanto el icono como el subtítulo se ocultan con estilos `ListBox.clipboard-mode ...`, NO con bindings `IsVisible="{Binding ... ElementName=RootWindow}"` por item: ese tipo de binding no resuelve `RootWindow` desde dentro del namescope del `DataTemplate`, así que el subtítulo quedaba visible pero vacío y reservaba una línea entera de altura (la causa real del padding sobrante); (2) el `MinHeight` del `ContentPresenter` interno del `ListBoxItem` (que el `ControlTheme` de Fluent fija por su cuenta) se anula con un selector `/template/`, porque bajar solo el `MinHeight` del item no basta; (3) el `FontSize` del título, la visibilidad del subtítulo y el `MinHeight` de la fila (`Grid.result-row`) se controlan desde estilos (`controls|HighlightTextBlock.result-title` / `.result-subtitle` / `Grid.result-row`), NO como valores locales en el `DataTemplate`, para que el override de clipboard-mode pueda cambiarlos (en Avalonia un valor local puesto como atributo en el XAML gana sobre cualquier `Setter` de estilo; el `MinHeight="44"` local de la fila era justamente lo que mantenía el item alto pese a todo lo demás).
- **Hotkey global:** si el usuario pulsa la hotkey global de la app con la ventana visible y en modo Clipboard, en vez de ocultar la ventana se cambia al modo All. Esto permite salir rápidamente al modo normal sin perder el foco.

> **Verificar en:** `MainWindow.axaml` (estilos `ListBox.clipboard-mode ...` para `ListBoxItem`, `ContentPresenter` via `/template/`, `Grid.result-row`, `Grid.result-icon`, `controls|HighlightTextBlock.result-title` y `.result-subtitle`; binding `Classes.clipboard-mode` en la `ListBox`). `App.axaml.cs` (bloque de hotkey global: `mainVm.ClipboardModeActive → mainVm.ResetMode()`).
