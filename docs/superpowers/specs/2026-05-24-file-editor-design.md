# Editor de texto inline — Spec de diseño

**Fecha**: 2026-05-24  
**Feature**: Editar ficheros de texto/código directamente desde los resultados de búsqueda con Cmd+E, usando un panel AvaloniaEdit que aparece a la derecha del buscador.

---

## Arquitectura general

El editor se implementa como un panel horizontal que se suma al layout de `MainWindow` sin alterar la columna del buscador. Todo el estado del editor vive en `MainWindowViewModel`; la lógica de I/O y validación vive en `FileEditorService` (Core, sin UI).

Componentes nuevos:

| Componente | Proyecto | Propósito |
|---|---|---|
| `EditorPanelView.axaml` + `.cs` | `Yottacast` | Vista del panel editor |
| `EditorPanelViewModel.cs` | `Yottacast.Core` | Estado del editor (contenido, dirty, autosave) |
| `FileEditorService.cs` | `Yottacast.Core` | Lectura/escritura de ficheros y validación texto/binario |
| `UnsavedChangesOverlay` | inline en `EditorPanelView` | Overlay de confirmación (no ventana separada) |

Cambios en existentes:

- `MainWindowViewModel` — nuevas propiedades `IsEditorOpen`, `EditorPanel`
- `MainWindow.axaml` — reestructurado como Grid horizontal de 2 columnas
- `UserSettings` — tres nuevos campos
- `AppDefaults` — cinco nuevas constantes
- `ActionHotkey` — nueva constante `MetaE`
- `ResultItemViewModel` — acción `Cmd+E Editar` en ficheros con extensión editable
- `Yottacast.csproj` — dependencia `AvaloniaEdit`
- `SettingsWindow.axaml` — nueva sección "Editor"

---

## Layout y dimensiones

### Ventana principal

`MainWindow` pasa de tener un `Border` directo a un `Grid` horizontal de 2 columnas:

```
Window (SizeToContent="WidthAndHeight")
└── Grid (columnas: Auto, Auto)
    ├── Col 0: Border (Margin=28) — buscador existente, sin cambios internos
    │          Width={DynamicResource Theme.Window.Width}
    └── Col 1: EditorPanelView — IsVisible={Binding IsEditorOpen}
               Width=AppDefaults.EditorWidth
               Height=AppDefaults.EditorHeight
```

Cuando `IsEditorOpen=false`, `EditorPanelView` es invisible y la columna colapsa a ancho 0. La ventana tiene el mismo ancho que antes. Al abrir el editor, la ventana se ensancha instantáneamente en `EditorWidth`.

La propiedad `Width` del `Window` se elimina (ya no está hardcodeada); en su lugar, se usa `SizeToContent="WidthAndHeight"` para que el ancho sea la suma de las columnas.

### Constantes nuevas en `AppDefaults`

```csharp
EditorWidth = 680;                      // Ancho del panel editor (px)
EditorHeight = 640;                     // Alto fijo (≈ buscador a máxima altura)
EditorMaxFileSizeMb = 5;                // Límite de tamaño para abrir
EditorBinaryDetectionBytes = 8_192;     // Bytes a leer para detectar binario
```

### Layout interno del panel editor

```
EditorPanelView (Width=EditorWidth, Height=EditorHeight)
├── Header bar (40px)
│   ├── [Nombre del fichero]  (truncado, alineado izquierda)
│   └── [Guardar] button      (solo visible si AutoSave=false, Cmd+S)
├── TextEditor (AvaloniaEdit, fill remaining height)
└── Status bar (24px)
    └── "Lín X, Col Y"
```

El `EditorPanelView` no tiene marco propio (se apoya en el borde visual del `Border` principal del buscador) pero sí un separador vertical izquierdo con `1px` del mismo color que el divisor horizontal.

---

## Hotkey Cmd+E — flujo de apertura

1. Se añade `ActionHotkey.MetaE = new("E", ActionModifiers.Meta)` en `ActionHotkey`.
2. `FileSearchSource` (la fuente de ficheros) comprueba al construir cada `ResultItemViewModel` si la extensión está en `UserSettings.FileEditorExtensions`. Si lo está, añade una `ResultAction` con `Hotkey = ActionHotkey.MetaE, ShowInFooter = true, Execute = () => {}` (no-op — la lógica real está en MainWindow para no acoplar Core a la UI del editor).
3. En `MainWindow.axaml.cs`, dentro de `OnTunnelKeyDown`, se añade un bloque específico para `ActionHotkey.MetaE`:
   - Si `IsEditorOpen` → intentar cerrar (ver flujo de cierre).
   - Si `!IsEditorOpen` y el resultado seleccionado es un fichero → intentar abrir.
4. **Validación antes de abrir** (delegada a `FileEditorService`):
   - ¿La extensión está en la lista? → si no, ignorar silenciosamente.
   - ¿El fichero es < `EditorMaxFileSizeMb`? → si no, mostrar hint "El archivo es demasiado grande para el editor".
   - ¿El contenido parece texto? (primeros `EditorBinaryDetectionBytes` bytes sin nulls ni bytes de control no-UTF8) → si no, mostrar hint "El archivo parece ser binario".
5. Si pasa todas las validaciones: `vm.OpenEditor(path)` → `IsEditorOpen = true`, `EditorPanel` carga el fichero.

---

## Flujo de guardado y cierre

### Autosave activado
- Cmd+E para cerrar → guarda automáticamente → cierra el panel.
- Cmd+S → guarda sin cerrar.

### Autosave desactivado
- Cmd+S → guarda sin cerrar (botón Guardar visible en header).
- Cmd+E para cerrar con cambios pendientes → muestra `UnsavedChangesOverlay`.
- Cmd+E para cerrar sin cambios → cierra directamente.

### UnsavedChangesOverlay
Overlay semitransparente sobre el `EditorPanelView` con un card centrado:

```
┌────────────────────────────────┐
│ Hay cambios sin guardar        │
│ en «nombre_fichero.txt»        │
│                                │
│ [Esc — Seguir editando]        │
│ [No guardar — cerrar]          │
│ [⌘E — Guardar y cerrar]        │
└────────────────────────────────┘
```

- **Esc**: cierra el overlay, el editor sigue abierto.
- **No guardar**: cierra el editor descartando cambios.
- **Cmd+E** (o clic en "Guardar y cerrar"): guarda y cierra.

El overlay se implementa como una segunda capa (`Grid.Row=0` que cubre todo) dentro de `EditorPanelView`, con `IsVisible={Binding ShowUnsavedDialog}` en `EditorPanelViewModel`.

---

## Syntax highlighting

`AvaloniaEdit` incluye `HighlightingManager` con definiciones integradas para los lenguajes más comunes (C#, Python, JavaScript, XML, HTML, CSS, JSON, Markdown, etc.).

Al cargar un fichero:
```csharp
editor.SyntaxHighlighting = HighlightingManager.Instance
    .GetDefinitionByExtension(Path.GetExtension(filePath));
// null si no hay definición → editor sin colorear (válido)
```

No se necesita ninguna configuración adicional — la detección es automática por extensión.

---

## Settings

### Nuevos campos en `UserSettings`

```csharp
public bool EnableFileEditor { get; set; } = true;
public bool FileEditorAutoSave { get; set; } = false;
public List<string> FileEditorExtensions { get; set; } = [
    "txt", "md", "markdown", "log", "csv",
    "cs", "fs", "vb",
    "py", "rb", "go", "rs", "java", "kt", "swift", "c", "cpp", "h",
    "js", "ts", "jsx", "tsx", "vue",
    "json", "yaml", "yml", "toml", "ini", "cfg", "conf", "config", "env",
    "xml", "html", "htm", "css", "scss", "less",
    "sh", "bash", "zsh", "fish", "ps1",
    "gitignore", "gitattributes", "editorconfig", "dockerfile"
];
```

Si `EnableFileEditor = false`, el hint de Cmd+E no aparece en el footer y el hotkey es ignorado.

### Nueva sección "Editor" en SettingsWindow

Ubicación: entre "FileSearch" y la siguiente sección. Controles:

- Toggle `EnableFileEditor` — "Editor de texto"
- Toggle `FileEditorAutoSave` — "Autoguardar al cerrar" (solo visible si editor habilitado)
- `ItemsControl` editable con la lista de extensiones — "Extensiones editables" (solo visible si editor habilitado)

---

## Registro en el contenedor DI

`FileEditorService` se registra como singleton en el contenedor DI junto con los demás servicios de Core. `EditorPanelViewModel` se instancia por `MainWindowViewModel` (no se registra en DI — vive como propiedad del VM padre).

---

## Tests

Los tests nuevos van en `Yottacast.Core.Tests/`:

- `FileEditorServiceTests.cs` — validación de extensión, detección binario, lectura/escritura.

No se añaden tests de UI (el panel de AvaloniaEdit no tiene lógica de negocio propia que justifique tests de integración de UI en este momento).

---

## Ficheros involucrados

**Nuevos:**
- `Yottacast/Views/EditorPanelView.axaml` + `.cs`
- `Yottacast.Core/ViewModels/EditorPanelViewModel.cs`
- `Yottacast.Core/Services/FileEditorService.cs`
- `Yottacast.Core.Tests/FileEditorServiceTests.cs`

**Modificados:**
- `Yottacast/Yottacast.csproj` — añadir `AvaloniaEdit`
- `Yottacast/Views/MainWindow.axaml` — Grid horizontal 2 columnas
- `Yottacast/Views/MainWindow.axaml.cs` — handler Cmd+E
- `Yottacast/Views/SettingsWindow.axaml` — sección Editor
- `Yottacast.Core/ViewModels/ResultAction.cs` — `ActionHotkey.MetaE`
- `Yottacast.Core/ViewModels/MainWindowViewModel.cs` — `IsEditorOpen`, `EditorPanel`
- `Yottacast.Core/UserSettings.cs` — tres campos nuevos
- `Yottacast.Core/AppDefaults.cs` — cinco constantes nuevas
- `Yottacast.Core/Search/FileSearchSource.cs` (o equivalente) — acción MetaE en resultados fichero
