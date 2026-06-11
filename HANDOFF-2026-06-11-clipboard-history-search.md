# Handoff — Clipboard History Search

**Fecha:** 2026-06-11
**Rama:** main (trabajo directo en main, ya pusheado a origin)
**Próxima feature:** `ClipboardHistorySearch` — historial de portapapeles con búsqueda en modo Clipboard

---

## Qué se hizo en esta sesión

Se implementó el sistema de **Search Modes** completo (19 commits, pusheados a origin/main).

**Artefactos generados:**
- Spec: `docs/superpowers/specs/2026-06-11-search-modes-design.md`
- Plan: `docs/superpowers/plans/2026-06-11-search-modes.md`

**Resumen de lo implementado:**
- `SearchSourceVisibility` (Disabled/Always/ModeOnly) y `SearchMode` (All/Files/Clipboard) como tipos base en Core
- `ISearchModeSource` — interfaz que implementan las fuentes con modo dedicado (solo `IsActiveIn(SearchMode)`)
- `GlobalSearch` filtra fuentes por modo activo usando `GetActiveSources<T>` genérico
- `UserDocumentSearch` implementa `ISearchModeSource` (Mode=Files)
- `UserSettings` reemplaza `EnableFileSearch: bool` por `FileSearchVisibility: SearchSourceVisibility` (migración automática desde JSON antiguo); añade `ClipboardSearchVisibility` y `ClipboardHotkey`
- `MainWindowViewModel`: `ActiveMode`, `CycleMode()`, `ResetMode()`, `ActivateMode()`, `ShowModePill`, `ActiveModeName`, `AvailableModes`
- UI: pill de modo activo debajo del search box, Cmd+F cicla modos, Escape vuelve a All, click en pill vuelve a All
- Settings: RadioButtons Off/Always/⌘F only para FileSearch
- App.axaml.cs: hotkey global de Clipboard (condicional, con guard de key-repeat y KeyReleased)

---

## Estado actual relevante para la próxima feature

### Lo que YA existe y la próxima sesión debe conocer

**Ya preparado para Clipboard mode:**
- `SearchMode.Clipboard` ya existe en el enum
- `UserSettings.ClipboardSearchVisibility: SearchSourceVisibility` (default Disabled) — ya en settings.json
- `UserSettings.ClipboardHotkey: string?` — ya en settings.json
- `App.axaml.cs`: ya registra el hotkey global de Clipboard cuando `ClipboardSearchVisibility == ModeOnly && ClipboardHotkey != null`
- `MainWindowViewModel.ActivateMode(SearchMode.Clipboard)` — ya existe y funciona
- `GlobalSearch.GetActiveSources<T>` — ya filtra fuentes con `ISearchModeSource` por modo. Una fuente que implemente `ISearchModeSource.IsActiveIn(Clipboard) == true` ya aparece automáticamente en Clipboard mode

**ClipboardSearch existente (DIFERENTE de lo que se va a construir):**
- `Yottacast.Core/Search/Clipboard/ClipboardSearch.cs` es un `IEmptyStateSource`, NO un `IInstantSearchSource`
- Lee el portapapeles ACTUAL cuando se abre la ventana (detecta URLs y rutas de fichero)
- No tiene nada que ver con historial — es un componente separado que se mantiene

**Ficheros clave de contexto:**
- `Yottacast.Core/Search/Clipboard/ClipboardSearch.cs` — ver su estructura para entender cómo está organizado el namespace
- `Yottacast.Core/Services/ClipboardService.cs` — bridge Core↔Avalonia para leer el portapapeles sin depender de Avalonia
- `Yottacast.Core/AppPaths.cs` — añadir aquí la ruta del fichero de historial de clipboard
- `Yottacast.Core/AppDefaults.cs` — añadir aquí las constantes (max entries, max days)
- `Yottacast.Core.Tests/Search/ClipboardSearchTests.cs` — tests del ClipboardSearch existente, ver estructura para los nuevos tests

**Patrón de fuente instant:** Ver `Yottacast.Core/Search/Application/ApplicationSearch.cs` o `Yottacast.Core/Search/Date/DateSearch.cs` como referencia de `IInstantSearchSource`.

---

## Lo que hay que construir

### Feature: ClipboardHistorySearch

**Objetivo:** Historial de todo lo copiado al portapapeles (texto), buscable en modo Clipboard. Accesible via hotkey global configurable o Cmd+F dentro del launcher.

**Comportamiento:**
- Captura todo lo que el usuario copia (texto) en background, incluso cuando el launcher está oculto
- Almacena hasta X entradas (configurable, ej. default 200) y no más antiguas de X días (configurable, ej. default 30)
- Persiste en disco: `AppPaths.ClipboardHistoryFile` (añadir a AppPaths) — formato JSON
- Al buscar en modo Clipboard: filtra por texto de la query (substring o fuzzy), ordenado por recencia
- Cada entrada: el texto copiado + timestamp + (opcional) source app
- Al activar una entrada: copia al portapapeles + oculta ventana (como el resultado de calculadora)
- Si la query está vacía en modo Clipboard: muestra las N entradas más recientes

**Settings necesarios (en UserSettings + UI):**
- `ClipboardHistoryEnabled: bool` (default false — opt-in)
- `ClipboardHistoryMaxEntries: int` (default 200)
- `ClipboardHistoryMaxDays: int` (default 30)
- `ClipboardSearchVisibility` ya existe (Off/Always/⌘F only)
- `ClipboardHotkey` ya existe

**Captura del portapapeles:**
- En macOS: `NSPasteboard` via P/Invoke o polling periódico (Spotlight no cubre portapapeles). Avalonia no expone eventos de cambio de clipboard. La estrategia habitual es polling (ej. cada 500ms) comparando el `changeCount` de `NSPasteboard.generalPasteboard`
- En Windows: `AddClipboardFormatListener` o polling
- La captura debe hacerse desde la capa de UI (Yottacast/Services/AppHandler o un servicio nuevo) ya que requiere acceso a Avalonia/P/Invoke de UI. Core solo define la interfaz y el almacenamiento

**Arquitectura sugerida:**
```
Yottacast.Core/Search/Clipboard/
  ClipboardHistorySearch.cs    ← IInstantSearchSource + ISearchModeSource
  ClipboardHistoryStore.cs     ← almacenamiento en memoria + persistencia JSON
  ClipboardHistoryEntry.cs     ← record (Text, Timestamp, ?)

Yottacast/Services/
  ClipboardMonitor.cs          ← polling de NSPasteboard (macOS) / Win API
  (o integrado en MacAppHandler/WindowsAppHandler)
```

**ISearchModeSource para la nueva fuente:**
```csharp
public bool IsActiveIn(SearchMode mode) => mode switch {
    SearchMode.All      => settings.ClipboardSearchVisibility == SearchSourceVisibility.Always,
    SearchMode.Clipboard => settings.ClipboardSearchVisibility == SearchSourceVisibility.ModeOnly,
    _ => false,
};
```

**Settings UI:** Añadir sección "Clipboard History" en SettingsWindow con:
- Toggle ClipboardHistoryEnabled
- Si enabled: RadioButtons Off/Always/⌘F only (ya hay patrón en FileSearch)
- Configurador de hotkey global (mismo patrón que el hotkey principal)
- Max entries y max days

---

## Lecciones aprendidas en esta sesión

- **El `ISearchModeSource` NO tiene `Mode` property** — solo `IsActiveIn(SearchMode)`. Se eliminó en la primera review porque `GlobalSearch` nunca la usaba. No re-añadir.
- **`GlobalSearch.GetActiveSources<T>` es el punto central de filtrado** — no poner lógica de filtrado en las fuentes (salvo la ya en `SearchAsync` de UserDocumentSearch que se eliminó). Si una fuente no debe ejecutarse en un modo, GlobalSearch no la llama.
- **La pill de modo usa `Foreground="White"` hardcodeado** — no `Theme.Window.Background` que da bajo contraste en light themes.
- **`OnSearchSettingsChanged` debe ejecutar el reset de modo ANTES del early-return por SearchText vacío** — bug sutil: si el usuario cambia settings con search vacío y modo activo, el modo quedaba "atascado".
- **Pill click requiere handler de `Tapped` explícito** — sin él, el click llega a `OnRootPointerPressed` y abre window drag.
- **El hotkey global de Clipboard necesita guard de key-repeat** (`_clipboardHotkeyDown`) y `KeyReleased` handler, igual que el hotkey principal. Sin esto, se acumulan dispatches en el UI thread al mantener pulsada la tecla.

---

## Suggested Skills

```
superpowers:brainstorming          ← PRIMERO: diseñar ClipboardHistorySearch antes de tocar código
superpowers:writing-plans          ← después del brainstorming
superpowers:subagent-driven-development  ← para ejecutar el plan
superpowers:test-driven-development     ← para cada tarea de implementación
superpowers:verification-before-completion  ← antes de dar por terminado
```

---

## Tests actuales

1284 passed, 0 failed, 1 skipped (el skip es manual/visual para System Settings anchors).

```bash
cd Yottacast.Core.Tests && dotnet test   # suite completa
cd Yottacast.Ipc.Tests && dotnet test    # IPC tests
```
