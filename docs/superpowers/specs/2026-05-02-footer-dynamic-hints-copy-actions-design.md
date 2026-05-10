# Footer dinámico, acciones de copia y shortcut de Settings

## Contexto

El footer de la ventana principal muestra hints estáticos ("↑↓ navigate · ↵ open") y un contador de resultados que no aportan valor real al usuario. Tampoco hay feedback visual cuando se copia algo, ni acceso rápido a Settings desde la UI. Este spec define el rediseño del footer para que sea dinámico, contextual y útil.

---

## Footer rediseñado

El footer pasa a ser **siempre visible** (actualmente `IsVisible="{Binding HasResults}"`).

### Layout

```
[ ⚙  ⌘;  settings ]          [ ↵ open · ⌘C path · Esc clear ]
  (izquierda, siempre)          (derecha, solo cuando HasResults)
```

- **Izquierda**: Button con icono ⚙ + texto del shortcut de Settings. Al pulsar abre Settings (igual que el shortcut de teclado). En macOS: `⌘;  settings`, en Windows/Linux: `Ctrl+;  settings`.
- **Derecha**: `ItemsControl` enlazado a `FooterHints: IReadOnlyList<string>` en el ViewModel. Visible solo cuando `HasResults`.
- **Eliminar**: contador de resultados (`DisplayResultCount`), StackPanels estáticos de emoji/normal, TextBlock de test.

### Hints por tipo de resultado seleccionado

| Tipo | Hints |
|---|---|
| `ResultItemViewModel` con `OnCopy != null` (Apps, Archivos) | `↵ open` · `⌘C path` · `Esc clear` |
| `ResultItemViewModel` sin `OnCopy` (Web Search) | `↵ open` · `Esc clear` |
| `CalculatorResultItemViewModel` | `↵ copy` · `⌘C copy` · `Esc clear` |
| `ConversionResultItemViewModel` | `↵ copy` · `⌘C copy` · `Esc clear` |
| `DictionaryResultViewModel` | `↵ open` · `⌘C definition` · `Esc clear` |
| `EmojiGridResultViewModel` | `⌘C copy` · `↵ paste` · `⌘⇧F fav` · `Esc clear` |
| Sin selección / null | `Esc clear` |

Los símbolos `⌘`, `⇧` se obtienen de `MetaSymbol` y `ShiftSymbol` (platform-aware). No se muestran flechas de navegación en ningún caso.

> **Verificar en:** `MainWindow.axaml` (footer layout), `MainWindowViewModel.cs` (`FooterHints`).

---

## Acciones de copia

### Comportamiento unificado de Cmd+C

**Cmd+C nunca cierra la ventana**, independientemente del tipo de resultado. Solo copia al portapapeles y muestra el hint de feedback. Cerrar la ventana es exclusivo de Enter.

`PasteAfterActivate` (flag ya existente en `BaseResultItemViewModel`) sigue gestionando el paste-after-close del emoji al pulsar Enter. No se toca.

### Nueva propiedad en `BaseResultItemViewModel`

```csharp
string? CopiedMessage { get; init; }  // mensaje a mostrar en SearchHint tras copiar
```

### Qué copia cada tipo con Cmd+C

| Tipo | Qué copia | `CopiedMessage` |
|---|---|---|
| Apps (`ApplicationSearch`) | Path del bundle (`app.Path`) | `"Path copied!"` |
| Archivos (`UserDocumentSearch`) | Path del fichero | `"Path copied!"` |
| Calculadora | Resultado numérico | `"Result copied!"` |
| Conversor | Celda seleccionada (igual que Enter) | `"Result copied!"` |
| Diccionario | Primera definición (`Definitions[0].Definition`) | `"Definition copied!"` |
| Emoji | El emoji (sin paste) | `"Emoji copied!"` |
| Web Search | *(sin OnCopy, Cmd+C no hace nada)* | — |

El handler en `MainWindow.axaml.cs` (tunnel phase, Cmd+C):
1. Invoca `OnCopy`
2. Si `CopiedMessage` no es null → llama `vm.ShowCopiedMessage(CopiedMessage)`
3. **No cierra la ventana** (eliminar las llamadas a `Hide()` / `CleanAndSaveHistory` del handler de copia)

### Feedback "Copied"

`MainWindowViewModel.ShowCopiedMessage(string msg)`:
- Asigna `SearchHint = msg`
- Cancela cualquier timer previo y lanza uno nuevo de 1.5 s que asigna `SearchHint = null`

El usuario ve el mensaje en el área debajo del input (donde ya aparecen hints de error de calculadora). El timer se cancela si el usuario vuelve a copiar antes de que expire.

> **Verificar en:** `MainWindowViewModel.cs` (`ShowCopiedMessage`), `MainWindow.axaml.cs` (handler Cmd+C).

---

## Shortcut de Settings: Cmd+, → Cmd+;

El handler actual en `MainWindow.axaml.cs`:
```csharp
case Key.OemComma when e.KeyModifiers.HasFlag(KeyModifiers.Meta):
```
Cambia a `Key.OemSemicolon`. El Cog button del footer también abre Settings vía `(Application.Current as App)?.OpenSettings()`, manejado como `Click` event handler en el code-behind (sin command en el ViewModel — la lógica ya vive en `App.axaml.cs`).

> **Verificar en:** `MainWindow.axaml.cs` (`OnKeyDown`), footer Button click handler.

---

## Cambios por archivo

### `Yottacast.Core/ViewModels/BaseResultItemViewModel.cs`
- + `string? CopiedMessage { get; init; }`
- Eliminar `CloseOnCopy` (no existe en el diseño final)

### `Yottacast.Core/Search/Application/ApplicationSearch.cs`
- + `ClipboardService clipboard` en constructor (DI lo inyecta automáticamente)
- `CreateResultItem()`: + `OnCopy = () => clipboard.CopyText(app.Path)`, `CopiedMessage = "Path copied!"`

### `Yottacast.Core/Search/UserDocuments/UserDocumentSearch.cs`
- + `ClipboardService clipboard` en constructor
- En creación del `ResultItemViewModel`: + `OnCopy = () => clipboard.CopyText(path)`, `CopiedMessage = "Path copied!"`

### `Yottacast.Core/Search/Calculator/CalculatorSearch.cs`
- `CalculatorResultItemViewModel`: + `OnCopy` (copia `captured`), `CopiedMessage = "Result copied!"`
- `ConversionResultItemViewModel`: + `OnCopy` (copia celda seleccionada igual que `OnActivate`), `CopiedMessage = "Result copied!"`

### `Yottacast.Core/Search/Dictionary/DictionarySource.cs`
- `DictionaryResultViewModel`: + `OnCopy = () => clipboard.CopyText(defs[0].Definition)`, `CopiedMessage = "Definition copied!"`. Verificar que `DictionarySource` tiene `ClipboardService` inyectado (añadir si no).

### `Yottacast.Core/Search/Emoji/EmojiSearch.cs`
- Al `EmojiGridResultViewModel`: + `CopiedMessage = "Emoji copied!"` (ya tiene `OnCopy`; el comportamiento de paste sigue en `PasteAfterActivate`)

### `Yottacast/ViewModels/MainWindowViewModel.cs`
- Eliminar: `DisplayResultCount`, `EmojiCopyShortcut`, `EmojiFavShortcut`
- Añadir: `IReadOnlyList<string> FooterHints` como propiedad computed (getter puro derivado de `SelectedResult`)
- Añadir: `void ShowCopiedMessage(string msg)` con timer de 1.5 s cancelable
- `OnSelectedResultChanged`: notificar `nameof(FooterHints)` además de `nameof(IsEmojiMode)`
- Añadir `_copiedMsgCts: CancellationTokenSource?` para el timer del mensaje

### `Yottacast/Views/MainWindow.axaml`
- Footer `Border`: eliminar `IsVisible="{Binding HasResults}"`
- `Grid`: columna izquierda → Button (⚙ + shortcut Settings), columna derecha → `ItemsControl` con `IsVisible="{Binding HasResults}"`
- Eliminar: result count TextBlock, StackPanel emoji mode, StackPanel normal mode, TextBlock test
- El `ItemsControl` itera `FooterHints` con un `DataTemplate` de `TextBlock` con el mismo estilo que los hints actuales

### `Yottacast/Views/MainWindow.axaml.cs`
- Handler Cmd+C: eliminar `Hide()` y `CleanAndSaveHistory`; añadir llamada a `vm.ShowCopiedMessage(result.CopiedMessage)` si procede
- Settings shortcut: `Key.OemComma` → `Key.OemSemicolon`
- Añadir `OnSettingsButtonClick` event handler para el Button del footer

### Docs
- `docs/ui-hotkeys.md`: sección 7 — Cmd+, → Cmd+;; añadir tabla de acciones de copia
- `docs/ui-main-window.md`: actualizar sección footer
- `docs/result-viewmodels.md`: añadir `CopiedMessage` en tabla de propiedades Base

---

## Tests a actualizar

- `ApplicationSearchTests.cs`: verificar `OnCopy != null`, `CopiedMessage == "Path copied!"`
- `CalculatorSearchTests.cs` / `UnitConverterSearchTests.cs`: verificar `OnCopy != null`, `CopiedMessage == "Result copied!"`
- `EmojiSearchTests.cs`: verificar `CopiedMessage == "Emoji copied!"`

---

## Verificación end-to-end

1. `cd Yottacast && dotnet run`
2. Abrir el launcher → footer siempre visible con ⚙ settings a la izquierda
3. Teclear una query → hints derechos aparecen según tipo de resultado seleccionado
4. Con app seleccionada: Cmd+C → ventana **no** se cierra, aparece "Path copied!" ~1.5 s
5. Con calculadora: Cmd+C → ventana **no** se cierra, aparece "Result copied!" ~1.5 s; Enter → cierra
6. Con emoji: Cmd+C → ventana **no** se cierra, aparece "Emoji copied!"; Enter → cierra + pega
7. Pulsar ⚙ con el ratón → abre Settings
8. Pulsar Cmd+; → abre Settings
9. `cd Yottacast.Core.Tests && dotnet test` — todos los tests pasan