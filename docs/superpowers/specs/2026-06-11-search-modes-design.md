# Search Modes — Diseño

**Fecha**: 2026-06-11
**Estado**: Aprobado

## Problema

Actualmente todas las fuentes de búsqueda están activas simultáneamente en el modo normal. Algunos usuarios quieren buscar solo ficheros sin ruido de otras fuentes; otros quieren todo mezclado. No hay forma de cambiar este comportamiento sin deshabilitar fuentes permanentemente.

Además, el portapapeles (fuente futura) necesita acceso rápido desde cualquier aplicación, lo que requiere un hotkey global propio.

## Solución

Introducir el concepto de **modos de búsqueda**. Cada fuente que lo soporte puede configurarse en tres estados: siempre activa, solo en su modo dedicado, o deshabilitada. El launcher puede cambiar de modo con `Cmd+F` sin salir ni perder el texto escrito.

---

## Modelo de datos

### `SearchSourceVisibility` (nuevo enum en `Yottacast.Core`)

```csharp
public enum SearchSourceVisibility
{
    Disabled,  // nunca aparece
    Always,    // siempre activa en modo All (comportamiento actual)
    ModeOnly,  // solo activa cuando su modo está seleccionado
}
```

### `SearchMode` (nuevo enum en `Yottacast.Core`)

```csharp
public enum SearchMode
{
    All,        // modo por defecto: todas las fuentes Always activas
    Files,      // solo UserDocumentSearch (si ModeOnly)
    Clipboard,  // solo ClipboardHistorySearch (si ModeOnly, futura)
}
```

### Cambios en `UserSettings`

| Propiedad | Tipo | Default | Notas |
|---|---|---|---|
| `FileSearchEnabled` | ~~`bool`~~ eliminado | — | Reemplazado por `FileSearchVisibility` |
| `FileSearchVisibility` | `SearchSourceVisibility` | `Always` | Controla cuándo aparecen los ficheros |
| `ClipboardSearchVisibility` | `SearchSourceVisibility` | `Disabled` | Para la futura Clipboard History |
| `ClipboardHotkey` | `HotkeyConfig?` | `null` | Hotkey global para abrir en modo Clipboard directamente |

**Migración**: usuarios existentes con `FileSearchEnabled = true` → `FileSearchVisibility = Always`; con `false` → `Disabled`.

---

## Comportamiento por configuración

| `FileSearchVisibility` | Ficheros en All | Pill Files | Incluido en Cmd+F |
|---|---|---|---|
| `Disabled` | No | No | No |
| `Always` | Sí (mezclado) | No | No |
| `ModeOnly` | No | Sí (cuando activo) | Sí |

Mismo esquema para `ClipboardSearchVisibility` / modo Clipboard.

---

## Estado de modo en `MainWindowViewModel`

### Propiedades nuevas

- `ActiveMode: SearchMode` — modo activo. Empieza en `All`.
- `AvailableModes: IReadOnlyList<SearchMode>` — modos en estado `ModeOnly` (orden: Files, Clipboard). Se recalcula cuando cambian los settings.
- `ShowModePill: bool` — `true` cuando `ActiveMode != All`.
- `ActiveModeName: string` — nombre localizable del modo activo ("Files", "Clipboard").

### Comando `CycleMode()`

Cicla el modo activo en el orden: `All → [modos ModeOnly en orden] → All`. Si no hay modos ModeOnly configurados, no hace nada.

Cuando `ActiveMode` cambia, se relanza la búsqueda con el mismo texto y el nuevo modo.

---

## Filtrado en `GlobalSearch`

`SearchInstant` y `SearchDeferredAsync` reciben un parámetro `SearchMode mode`.

**Regla de inclusión de fuentes:**

- Fuentes sin modo (Apps, Calculator, Web, Emoji, SystemSettings, etc.): activas solo en `All`.
- `UserDocumentSearch`: activa en `All` si `FileSearchVisibility = Always`; activa en `Files` si `ModeOnly`.
- `ClipboardHistorySearch` (futura): activa en `All` si `ClipboardSearchVisibility = Always`; activa en `Clipboard` si `ModeOnly`.

En un modo específico (Files, Clipboard) solo corren las fuentes de ese modo. Las fuentes `Always` no corren en modos específicos — el modo es exclusivo.

---

## UI: pill de modo activo

La pill aparece **debajo del campo de búsqueda** únicamente cuando `ActiveMode != All`. Muestra el nombre del modo activo con el color de acento del tema. Al hacer click en la pill vuelve a `All`.

Cuando `ActiveMode == All` el layout es idéntico al actual: no hay elementos visuales extra.

---

## Teclado

| Shortcut | Contexto | Efecto |
|---|---|---|
| `Cmd+F` | Launcher abierto | Cicla modos: All → ModeOnly1 → ModeOnly2 → All |
| `Escape` (1er nivel) | `ActiveMode != All` | Vuelve a `All` (antes de limpiar texto) |
| `Escape` (resto) | Comportamiento actual | Cancela búsqueda / limpia texto / oculta |
| Click en pill activa | Launcher abierto | Vuelve a `All` |
| Hotkey global Clipboard | Global | Abre launcher en modo `Clipboard` directamente |

La cadena de Escape queda: `IsSearching` → `ActiveMode != All` → `SearchText no vacío` → `SearchText vacío` → ocultar ventana.

---

## Settings UI

El toggle binario de cada fuente se reemplaza por un **segmented control** de tres opciones: `Off / Always / ⌘F only`. Cada fuente mantiene su control en su propia sección de Settings.

El campo `Clipboard Hotkey` (captura de tecla, igual que la hotkey global principal) aparece en la sección de Clipboard y solo es editable cuando `ClipboardSearchVisibility == ModeOnly`. Si Clipboard está en `Always` no existe un "modo Clipboard" al que saltar, por lo que el hotkey no tiene sentido.

---

## Scope de esta implementación

**En scope:**
- Enums `SearchSourceVisibility` y `SearchMode`
- Migración de `FileSearchEnabled` → `FileSearchVisibility`
- `ClipboardSearchVisibility` y `ClipboardHotkey` en UserSettings (estructura, sin fuente implementada)
- Lógica de modo en `MainWindowViewModel` (`ActiveMode`, `CycleMode`, `ShowModePill`)
- Filtrado por modo en `GlobalSearch`
- Pill de modo activo en `MainWindow.axaml`
- Handler `Cmd+F` en `MainWindow.axaml.cs`
- Cadena de Escape ampliada
- Hotkey global de Clipboard en `App.axaml.cs` (registro condicional)
- Settings UI: segmented control para `FileSearchVisibility`
- Tests: `MainWindowViewModel` (CycleMode, AvailableModes), `GlobalSearch` (filtrado por modo)

**Fuera de scope (trabajo futuro):**
- Implementación de `ClipboardHistorySearch` (fuente de historial de portapapeles)
- Settings UI para Clipboard (la sección aún no existe)

---

## Invariantes

- En modo `All` el layout es idéntico al actual. Nada cambia visualmente para usuarios que no configuren modos.
- `CycleMode()` no hace nada si `AvailableModes` está vacío.
- El texto de búsqueda se preserva al cambiar de modo.
- La hotkey global de Clipboard solo se registra si `ClipboardSearchVisibility == ModeOnly` y `ClipboardHotkey != null`.
- En un modo específico, las fuentes `Always` no corren. El modo es exclusivo.
- Escape en modo activo vuelve a `All` antes de limpiar texto.

> **Verificar en (cuando esté implementado):** `SearchSourceVisibility.cs`, `SearchMode.cs`, `UserSettings.cs` (FileSearchVisibility, ClipboardSearchVisibility, ClipboardHotkey), `MainWindowViewModel.cs` (ActiveMode, CycleMode, AvailableModes, ShowModePill), `GlobalSearch.cs` (SearchInstant, SearchDeferredAsync con parámetro mode), `MainWindow.axaml` (pill de modo), `MainWindow.axaml.cs` (Cmd+F, Escape ampliado), `App.axaml.cs` (registro hotkey Clipboard).
