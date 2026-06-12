# Spec: Escape behavior, pill "All", y footer hint

**Fecha:** 2026-06-12

---

## Objetivo

Tres cambios coordinados en la barra de pills de modo y el comportamiento de Escape:

1. Escape deja de resetear el modo activo; sólo borra texto o cierra la ventana.
2. Se añade un pill "All" explícito y cada pill es individualmente clickable.
3. El hint "⌘F cycle" se mueve al footer (al lado de Settings), eliminándolo del área de pills.

---

## 1. Comportamiento de Escape

### Estado actual

El handler de Escape en `MainWindow.axaml.cs` tiene esta jerarquía:

1. Editor abierto → cerrar editor
2. Menú de opciones abierto → cerrarlo
3. Búsqueda diferida en curso → cancelarla + limpiar texto
4. `ShowModePill == true` → resetear modo a All (bug: ignora si hay texto)
5. Texto no vacío → limpiar texto
6. Texto vacío → ocultar ventana

### Nuevo comportamiento

Eliminar el paso 4. Nueva jerarquía:

1. Editor abierto → cerrar editor
2. Menú de opciones abierto → cerrarlo
3. Búsqueda diferida en curso → cancelarla + limpiar texto
4. Texto no vacío → limpiar texto
5. Texto vacío → ocultar ventana

El modo activo (All / Files / Clipboard) no cambia al pulsar Escape. Persiste hasta que el usuario lo cambie via Cmd+F o click en un pill.

### Invariante

"La primera pulsación de Escape borra el texto si hay texto; la segunda cierra la ventana."

> **Verificar en:** `MainWindow.axaml.cs` — `OnKeyDown`, case `Key.Escape`.

---

## 2. Pill "All" y clicks individuales

### Pills visibles

El área de pills sólo es visible cuando `ShowModePill == true` (hay al menos un modo disponible: Files o Clipboard en modo ModeOnly). Cuando es visible, muestra:

| Pill | Activo cuando | Visible |
|---|---|---|
| All | `_activeMode == SearchMode.All` | Siempre que `ShowModePill` |
| Files | `_activeMode == SearchMode.Files` | `HasFilesMode` |
| Clipboard | `_activeMode == SearchMode.Clipboard` | `HasClipboardMode` |

El pill "All" aparece primero (izquierda), seguido de los demás.

### Rotación con Cmd+F

Sin cambios en la lógica: `CycleMode()` rota All → modes[0] → modes[1] → All. Ahora que existe el pill "All", la rotación es visible en la UI.

### Clicks individuales

Cada pill es clickable de forma independiente:
- Click en "All" → activa `SearchMode.All`
- Click en "Files" → activa `SearchMode.Files`
- Click en "Clipboard" → activa `SearchMode.Clipboard`

**Implementación**: los tres `Border` de pills tienen `x:Name` en AXAML. El handler `OnModePillTapped` identifica cuál fue pulsado recorriendo `e.Source` hacia arriba hasta encontrar el `Border` con nombre conocido.

### Propiedad nueva en ViewModel

```csharp
public bool AllModeActive => _activeMode == SearchMode.All;
```

El setter de `ActiveMode` dispara `OnPropertyChanged(nameof(AllModeActive))` junto con las demás notificaciones.

> **Verificar en:** `MainWindowViewModel.cs` — `AllModeActive`, `ActiveMode.set`. `MainWindow.axaml.cs` — `OnModePillTapped`. `MainWindow.axaml` — `AllModePill`, `FilesModePill`, `ClipboardModePill`.

---

## 3. Estilo de los pills

### Estado actual

- Inactivo: `Opacity="0.4"`, sin fondo, sin borde
- Activo: `Opacity="1"`, fondo `Theme.Results.SelectionBar.Color` (azul de acento)

### Nuevo estilo

- Inactivo: `Opacity="0.35"`, sin fondo, sin borde (igual)
- Activo: `Opacity="1"`, sin fondo de relleno, borde `1px` con `Theme.Search.Color`

Apariencia "outlined": texto a plena opacidad con borde sutil del color del texto. No requiere nuevos tokens de tema.

Los estilos AXAML afectados son `.mode-chip` y `.mode-chip.active` en `MainWindow.axaml`.

---

## 4. Hint en el footer

### Cambio

Eliminar de la zona de pills el `<TextBlock Text="⌘F to cycle · Esc to exit" .../>`.

Añadir en el lado izquierdo del footer el texto `⌘F  cycle`, visible sólo cuando `ShowModePill == true`.

El footer izquierdo pasa de un único `TextBlock` (Settings) a un `StackPanel` horizontal con:
1. Settings text (siempre visible)
2. `⌘F  cycle` text (visible sólo si `ShowModePill`)

Ambos comparten el mismo `Foreground` y `FontSize` del footer (`Theme.Footer.Color`, `Theme.Footer.Size`).

> **Verificar en:** `MainWindow.axaml` — sección footer. `MainWindowViewModel.cs` — `ShowModePill` (propiedad existente, sin cambios).

---

## 5. Documentación a actualizar

- `docs/ui-main-window.md` — sección 5 (jerarquía Escape) y sección 11 (footer: añadir mención al hint de cycle).

---

## Archivos afectados

| Archivo | Tipo de cambio |
|---|---|
| `Yottacast/Views/MainWindow.axaml.cs` | Fix Escape, fix OnModePillTapped |
| `Yottacast/ViewModels/MainWindowViewModel.cs` | AllModeActive + notificación |
| `Yottacast/Views/MainWindow.axaml` | Pill All, x:Names, estilos, footer hint |
| `docs/ui-main-window.md` | Actualizar secciones 5 y 11 |

No hay tests unitarios afectados (comportamiento de UI/code-behind no cubierto por `Yottacast.Core.Tests/`).
