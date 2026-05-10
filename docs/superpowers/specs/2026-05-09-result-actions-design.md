# Result Actions System — Spec

**Fecha:** 2026-05-09

## Contexto

Actualmente cada resultado de búsqueda expone sus acciones como propiedades discretas (`OnActivate`, `OnCopy`, `OnToggleFavorite`, `PasteAfterActivate`, `CopiedMessage`, `CopiedMessageProvider`) en `BaseResultItemViewModel`. La UI las procesa caso a caso en `MainWindow.axaml.cs` y el footer se genera mediante un switch de tipos en `MainWindowViewModel.FooterHints`.

Este diseño no escala: añadir una nueva acción requiere tocar el ViewModel base, el procesador de hotkeys y el generador de hints. Las sources no pueden declarar acciones arbitrarias.

El objetivo es un modelo de **lista de acciones declarativas** por resultado, donde cada source define completamente qué acciones expone, qué hotkeys tienen, cómo se muestran y qué efectos tienen al ejecutarse.

---

## Modelo de datos: `ResultAction`

```csharp
// Yottacast.Core/ViewModels/ResultAction.cs
public sealed class ResultAction
{
    public required string Label { get; init; }       // "Open", "Copy path", "Define alias"
    public KeyGesture? Hotkey { get; init; }           // null = sin hotkey directo
    public bool ShowInFooter { get; init; }            // muestra hint en la barra inferior
    public bool ShowInMenu { get; init; }              // aparece en el overlay de opciones (Tab)
    public bool ClosesMenu { get; init; }              // cierra el overlay tras ejecutar
    public bool ClosesWindow { get; init; }            // oculta Yottacast tras ejecutar
    public bool PasteAfterClose { get; init; }         // simula Cmd+V tras cerrar (antes PasteAfterActivate)
    public Func<string?>? HintProvider { get; init; }  // mensaje dinámico tras ejecutar ("Path copied!"), null = no mostrar
    public required Action Execute { get; init; }      // callback de la acción
}
```

`KeyGesture` es el tipo de Avalonia (`Avalonia.Input.KeyGesture`). Para hotkeys dependientes de plataforma (Cmd/Ctrl), las sources usan `AppHandler.Instance` para obtener los modificadores correctos, igual que ahora.

---

## Cambios en `BaseResultItemViewModel`

Se eliminan:
- `OnActivate`, `OnCopy`, `OnToggleFavorite`
- `CopiedMessage`, `CopiedMessageProvider`
- `PasteAfterActivate`

Se añade:
```csharp
public IReadOnlyList<ResultAction> Actions { get; init; } = [];
```

Se mantienen sin cambios:
- `Score`, `Title` (datos de ordenación y display)
- `OnLeft`, `OnRight`, `OnUp`, `OnDown` (navegación interna de grids/celdas — ortogonal a las acciones)
- `BypassLimit`

---

## Footer dinámico

`MainWindowViewModel.FooterHints` se genera automáticamente desde `SelectedResult?.Actions`:

```
Actions.Where(a => a.ShowInFooter)  →  formateados como "↵ Open", "⌘C Copy path"
+ si Actions.Any(a => a.ShowInMenu) →  "Tab  Options"
+ siempre                           →  "Esc  clear"
```

Desaparece el switch de tipos. Cada source controla sus propios hints declarando `ShowInFooter = true` en las acciones que quiere mostrar.

El formato del hint de una acción en el footer:
- Con hotkey: `"⌘C  Copy path"` (símbolo de tecla + espacios + label)
- Sin hotkey: `ShowInFooter` solo tiene efecto si la acción tiene `Hotkey`. Acciones sin hotkey solo deberían tener `ShowInMenu = true`.
- La primera acción con `ShowInFooter = true` cuyo `Hotkey` es Enter se muestra como `"↵  Open"`

---

## Overlay de opciones

### Apertura
Se abre con **Tab** si `SelectedResult?.Actions.Any(a => a.ShowInMenu) == true`. Si no hay acciones con `ShowInMenu`, Tab no hace nada.

### Contenido
Muestra `Actions.Where(a => a.ShowInMenu)` como lista vertical:
```
┌─────────────────────────────┐
│  Actions                    │
│  ► Open              ↵      │
│    Copy path         ⌘C     │
│    Define alias             │
│    Move to Trash            │
└─────────────────────────────┘
```
Cada fila: label a la izquierda, hotkey (si existe) a la derecha. La acción enfocada se resalta.

### Navegación
| Tecla | Comportamiento |
|---|---|
| ↑ / ↓ | Mueve foco entre acciones del overlay |
| Enter | Ejecuta la acción enfocada |
| Esc | Cierra overlay, devuelve foco a resultados |
| Tab | Cierra overlay (toggle) |
| Hotkey directo (ej. ⌘C) | Ejecuta la acción sin necesidad de abrir el overlay |

Mientras el overlay está abierto, ↑↓ no navegan la lista de resultados — se interceptan para el overlay.

### Estado en ViewModel
`MainWindowViewModel` expone:
```csharp
public bool IsOptionsMenuOpen { get; private set; }
public int OptionsMenuSelectedIndex { get; private set; }
public IReadOnlyList<ResultAction> OptionsMenuActions { get; }
// derivado de SelectedResult?.Actions.Where(a => a.ShowInMenu)
```

---

## Procesamiento de hotkeys en `MainWindow`

El handler actual (caso por caso) se sustituye por un bucle genérico en la fase tunnel:

```csharp
// Iterar acciones del resultado seleccionado
foreach (var action in vm.SelectedResult?.Actions ?? [])
{
    if (action.Hotkey != null && MatchesGesture(e, action.Hotkey))
    {
        ExecuteAction(action);
        e.Handled = true;
        return;
    }
}
```

`ExecuteAction(action)`:
1. Llama `action.Execute()`
2. Si `action.ClosesMenu` → cierra overlay
3. Si `action.ClosesWindow` → llama `Hide()`, `AppHandler.Instance.OnHide()`
4. Si `action.PasteAfterClose` → `SimulatePasteAsync()`
5. Si `action.HintProvider?.Invoke()` devuelve texto → `vm.ShowCopiedMessage(msg)`

Tab se procesa por separado (abre/cierra overlay), antes del bucle de acciones.

---

## Migración de sources

| Source | Acciones |
|---|---|
| **Apps** | Open (Enter, footer, menu, closesWindow) · Copy path (⌘C, footer, menu, hint="Path copied!") |
| **Calculator** | Copy result (Enter, footer, closesWindow, paste, hint="Result copied!") · Copy result (⌘C, footer, hint="Result copied!") |
| **Conversion** | Copy value (Enter, footer, closesWindow, paste) · Copy value (⌘C, footer) |
| **Emoji** | Paste (Enter, footer, menu, closesWindow, paste, hint dinámico) · Copy (⌘C, footer, menu, hint dinámico) · Favorite (⌘⇧F, footer, menu, closesMenu=false, closesWindow=false) |
| **WebSearch** | Open in browser (Enter, footer, closesWindow) |
| **Dictionary** | Open Wiktionary (Enter, footer, menu, closesWindow) · Copy definition (⌘C, footer, menu, hint="Definition copied!") |
| **Files** | Open (Enter, footer, menu, closesWindow) · Copy path (⌘C, footer, menu, hint="Path copied!") |

> Nota: en Calculator/Conversion, Enter y ⌘C ejecutan la misma acción lógica (copiar). Se pueden definir como dos `ResultAction` distintas con el mismo `Execute`.

---

## Archivos afectados

**Nuevo:**
- `Yottacast.Core/ViewModels/ResultAction.cs`

**Modificados en Core:**
- `Yottacast.Core/ViewModels/BaseResultItemViewModel.cs` — eliminar propiedades discretas, añadir `Actions`
- `Yottacast.Core/Search/Application/ApplicationSearch.cs`
- `Yottacast.Core/Search/Calculator/CalculatorSearch.cs`
- `Yottacast.Core/Search/Emoji/EmojiSearch.cs`
- `Yottacast.Core/Search/WebSearch/WebSearchSource.cs`
- `Yottacast.Core/Search/Dictionary/DictionarySource.cs`
- `Yottacast.Core/Search/Files/UserDocumentSearch.cs`

**Modificados en UI:**
- `Yottacast/ViewModels/MainWindowViewModel.cs` — `FooterHints` derivado de `Actions`, estado del overlay
- `Yottacast/Views/MainWindow.axaml.cs` — bucle genérico de hotkeys, Tab abre overlay
- `Yottacast/Views/MainWindow.axaml` — overlay de opciones, footer generado dinámicamente

**Docs actualizados:**
- `docs/result-viewmodels.md` — reemplazar sección de callbacks con `Actions`
- `docs/ui-main-window.md` — documentar overlay y footer dinámico

---

## Verificación

1. Ejecutar la app y buscar "Code" → resultado de VS Code seleccionado:
   - Footer muestra `↵ Open`, `⌘C Copy path`, `Tab Options`, `Esc clear`
   - Pulsar ⌘C → hint "Path copied!" aparece en search hint, ventana no se cierra
   - Pulsar Enter → app se lanza, ventana se cierra
   - Pulsar Tab → overlay muestra "Open (↵)", "Copy path (⌘C)"
   - En overlay: ↓ mueve foco, Enter ejecuta, Esc cierra overlay

2. Buscar `:smile` → emoji seleccionado:
   - Footer muestra `↵ paste`, `⌘C copy`, `⌘⇧F fav`, `Tab Options`, `Esc clear`
   - Pulsar Enter → emoji copiado y pegado, ventana se cierra
   - Pulsar ⌘⇧F → favorito toggleado, overlay NO se cierra, ventana NO se cierra

3. Calcular `2+3`:
   - Footer muestra `↵ copy`, `⌘C copy`, `Esc clear`
   - No hay `Tab Options` (Calculator y Conversion no usan ShowInMenu — sus acciones son idénticas y el footer ya las cubre)

4. Ejecutar `cd Yottacast.Core.Tests && dotnet test` → todos los tests pasan.
