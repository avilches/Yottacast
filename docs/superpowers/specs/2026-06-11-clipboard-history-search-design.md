# Clipboard History Search — Diseño

**Fecha**: 2026-06-11
**Estado**: Aprobado

## Problema

Yottacast no tiene historial de portapapeles. El usuario tiene que recordar lo que copió o buscar en la aplicación de origen. Gestores de launchers como Raycast o Alfred incluyen historial de clipboard como una de sus features más usadas.

## Solución

Capturar en background todo el texto que el usuario copia al portapapeles, persistirlo en disco con límites configurables, y exponerlo como fuente de búsqueda en modo Clipboard. Al activar una entrada, se pega automáticamente en la app anterior (igual que emoji).

---

## Modelo de datos

### `ClipboardHistoryEntry`

```csharp
public record ClipboardHistoryEntry(
    string Text,
    DateTimeOffset CopiedAt,
    int UsageCount,
    DateTimeOffset LastUsedAt
);
```

- `CopiedAt`: timestamp de la última vez que se copió este texto (se actualiza en dedup).
- `UsageCount` / `LastUsedAt`: usados para el bonus de uso con decay.

### Persistencia

- Ruta: `AppPaths.ClipboardHistoryFile` = `ConfigDir/clipboard-history.json`
- Formato: array JSON de entradas, ordenado por recencia (más reciente primero).
- La lista completa se carga en memoria al arrancar. Las escrituras son asíncronas con debounce de 1s para no escribir en cada entrada rápida.
- Nuevas constantes en `AppDefaults`:
  - `ClipboardHistoryMaxEntries = 200`
  - `ClipboardHistoryMaxDays = 30`
  - `ClipboardHistoryHalfLifeDays = 30`

---

## Componentes Core

### `ClipboardHistoryStore`

Servicio registrado en DI. Responsabilidades: mantener la lista en memoria, persistir a disco, aplicar límites.

**`Add(string text)`**:
1. Si el texto ya existe en la lista: actualiza `CopiedAt = now`, mueve la entrada al principio (dedupe).
2. Si no existe: inserta una entrada nueva al principio con `UsageCount = 0`, `LastUsedAt = CopiedAt = now`.
3. Aplica límites: descarta entradas con `CopiedAt < now - MaxDays` días, luego recorta a `MaxEntries`.
4. Persiste (debounced).

**`Remove(string text)`**: elimina la entrada con ese texto exacto y persiste inmediatamente.

**`RecordUsage(string text)`**: incrementa `UsageCount`, actualiza `LastUsedAt = now`, persiste.

**`event Action EntriesChanged`**: se dispara tras `Add`, `Remove` y `RecordUsage` para que `ClipboardHistorySearch` propague `ResultChanged`.

**`IReadOnlyList<ClipboardHistoryEntry> GetAll()`**: devuelve la lista en memoria (ya ordenada por recencia).

### `ClipboardHistorySearch`

`IInstantSearchSource + ISearchModeSource` en `Yottacast.Core/Search/Clipboard/`.

**`IsActiveIn`**:
```csharp
public bool IsActiveIn(SearchMode mode) => mode switch {
    SearchMode.All       => settings.ClipboardSearchVisibility == SearchSourceVisibility.Always,
    SearchMode.Clipboard => settings.ClipboardSearchVisibility == SearchSourceVisibility.ModeOnly,
    _                    => false,
};
```

**`Search(string query, int limit)`**:
- Si `!settings.ClipboardHistoryEnabled` → retorna `[]`.
- Obtiene todas las entradas via `store.GetAll()`.
- Si `query` está vacía: devuelve las primeras `limit` entradas (ya ordenadas por recencia), score = `1000.0 - index` para preservar el orden.
- Si `query` no vacía: filtra por `entry.Text.Contains(query, OrdinalIgnoreCase)`, puntúa y ordena por score descendente.

**Fórmula de score** (con query):

```
matchScore:
  - texto == query (exact):          4.0
  - texto empieza por query:         3.5
  - texto contiene query:            3.0

usageBonus = DecayedBonus(UsageCount, LastUsedAt, ClipboardHistoryHalfLifeDays)
             (mismo helper que LaunchHistory, cap en 0.5)

score = matchScore + usageBonus
```

**Resultado (`ResultItemViewModel`)**:
- `Title`: texto truncado a 120 chars, saltos de línea reemplazados por `·` para mantener una sola línea.
- `Subtitle`: timestamp relativo ("hace 2 min", "ayer", o fecha corta si > 7 días).
- `Category = "Clipboard"`, sin `InfoTag`.

**Acciones**:

| Acción | Hotkey | Footer | Menú | Cierra ventana |
|--------|--------|--------|------|----------------|
| Paste  | Enter  | sí     | sí   | sí             |
| Delete | Delete | sí     | sí   | no             |

- **Paste**: `ClipboardService.CopyText(text)` + ocultar ventana + restaurar app anterior + simular Cmd+V con delay (igual que emoji).
- **Delete**: `store.Remove(text)`. Como `EntriesChanged` dispara `ResultChanged`, el ViewModel refresca la lista automáticamente sin cerrar la ventana.

**Lifecycle**:
```csharp
public void Start() => store.EntriesChanged += OnEntriesChanged;
public Task WhenReady() => Task.CompletedTask;
public Task Stop() { store.EntriesChanged -= OnEntriesChanged; return Task.CompletedTask; }
private void OnEntriesChanged() => ResultChanged?.Invoke();
public event Action? ResultChanged;
```

---

## Captura del portapapeles

### `IClipboardMonitor` (interfaz en Core)

```csharp
public interface IClipboardMonitor
{
    event Action<string> TextCopied;
    void Start();
    void Stop();
}
```

### `MacClipboardMonitor` (en `Yottacast/Services/`)

- Polling cada 500ms via `System.Threading.PeriodicTimer`.
- Llama a `NSPasteboard.generalPasteboard.changeCount` via P/Invoke. Si el count difiere del anterior: lee el texto y dispara `TextCopied`.
- Ignora texto nulo o vacío.
- Solo activo cuando `ClipboardHistoryEnabled == true`.

### `WindowsClipboardMonitor` (en `Yottacast/Services/`)

- `AddClipboardFormatListener` via P/Invoke a Win32 con un HWND de la ventana principal.
- Escucha el mensaje `WM_CLIPBOARDUPDATE` y dispara `TextCopied` si el contenido es texto.

### Arranque

`MacAppHandler.Initialize()` y `WindowsAppHandler.Initialize()` crean el monitor correspondiente, lo arrancan, y conectan `monitor.TextCopied += text => store.Add(text)`, todo condicionado a `settings.ClipboardHistoryEnabled`. El store no conoce al monitor; el AppHandler es el pegamento. Cuando settings cambia (`SearchSettingsChanged`), AppHandler para y reinicia el monitor si `ClipboardHistoryEnabled` ha cambiado.

---

## Settings

### Propiedades nuevas/renombradas en `UserSettings`

- `EnableClipboard` (existente, no controlaba nada) se renombra a `ClipboardHistoryEnabled: bool = false`. La clave JSON `"enableClipboard"` se mantiene para compatibilidad.
- `ClipboardHistoryMaxEntries: int = 200` (nueva, clave `"clipboardHistoryMaxEntries"`).
- `ClipboardHistoryMaxDays: int = 30` (nueva, clave `"clipboardHistoryMaxDays"`).
- `ClipboardSearchVisibility` ya existe.
- `ClipboardHotkey` ya existe.

### UI — sección Clipboard en `SettingsWindow.axaml`

La sección existente (que solo tenía el toggle placeholder) se expande:

```
[Toggle] Clipboard History  (On/Off)
Descripción: "Capture everything you copy and search it from Clipboard mode."

[visible si enabled:]
  RadioButtons:  Off | Always | ⌘F only
  (misma estructura que la sección FileSearch)

  [visible si ModeOnly:]
    Hotkey configurador (mismo patrón que hotkey principal)

  ───────────────────────
  Max entries:   [NumericUpDown  1-1000]
  Keep for (days): [NumericUpDown  1-365]
```

---

## Tests

Archivos nuevos en `Yottacast.Core.Tests/`:

**`ClipboardHistoryStoreTests`**:
- `Add` inserta entrada nueva.
- `Add` con texto duplicado deduplica y mueve al principio con timestamp actualizado.
- `Add` aplica `MaxEntries` (descarta entradas antiguas sobrantes).
- `Add` aplica `MaxDays` (descarta entradas fuera del rango).
- `Remove` elimina la entrada correcta.
- `RecordUsage` incrementa `UsageCount` y `LastUsedAt`.
- `EntriesChanged` se dispara tras `Add`, `Remove`, `RecordUsage`.

**`ClipboardHistorySearchTests`**:
- Query vacía devuelve las N más recientes.
- Query no vacía filtra por `Contains`.
- Score: exact > startsWith > contains.
- Bonus de uso incrementa el score.
- `ClipboardHistoryEnabled = false` devuelve `[]`.
- `IsActiveIn` devuelve true/false según `ClipboardSearchVisibility` y modo.
- `ResultChanged` se dispara cuando el store cambia.

> **Verificar en**: `Yottacast.Core/Search/Clipboard/ClipboardHistorySearch.cs`, `Yottacast.Core/Search/Clipboard/ClipboardHistoryStore.cs`, `Yottacast/Services/MacClipboardMonitor.cs`
