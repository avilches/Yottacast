# Plan de mejoras de usabilidad — Yottacast

> Análisis basado en el código actual (rama `main`, marzo 2026).
> Referencia de comparación: Alfred 5, Raycast, Spotlight (macOS 14+), PowerToys Run.

---

## 1. Diagnóstico general

Yottacast tiene una base sólida: pipeline de búsqueda en dos fases, debounce, spinner de estado, temas intercambiables, emoji picker con paste-back, calculadora/conversor. Lo que falta son los refinamientos que distinguen a un launcher "terminado" de uno "funcional":

- Sin historial de búsquedas anteriores.
- Sin acciones secundarias por resultado (solo `Enter` = acción principal).
- Sin preview/detalle del ítem seleccionado.
- Sin posibilidad de mover la ventana o anclarla en otra posición.
- Sin feedback visual al activar un resultado (la ventana desaparece inmediatamente).
- Shortcuts de teclado limitados (faltan Tab, Home/End, PgUp/PgDn, Cmd+K).
- Búsqueda de URLs directas y navegación no implementada.
- Score numérico visible en producción (ruido visual).
- Footer hint `⌘K actions` hardcodeado sin implementar.
- Sin soporte de acciones secundarias por item (abrir en Finder, copiar ruta, etc.).
- `ResultItemViewModel.Shortcut` nunca se asigna desde ninguna fuente.
- `WindowsAppHandler.OnShow()` / `OnHide()` son no-ops (sin gestión de foco en Windows).

---

## 2. Mejoras priorizadas

Las mejoras se agrupan en seis áreas. Dentro de cada área, el orden es de mayor a menor impacto UX.

---

### A. Navegación por teclado

#### A1. Teclas adicionales de navegación
**Problema**: solo `↑`/`↓` navegan la lista. No hay `Home`/`End`, `PgUp`/`PgDn`, ni `Tab` como alternativa a `↓`.

**Referencia**: Alfred y Raycast soportan `Tab`/`Shift+Tab` para navegar resultados, y `Cmd+↑`/`Cmd+↓` para saltar al primero/último.

**Ficheros a tocar**:
- `Yottacast/Views/MainWindow.axaml.cs` — `OnKeyDown`, método `SelectNext`

**Implementación**:
```csharp
case Key.Tab:
    SelectNext(vm, e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : +1);
    e.Handled = true;
    break;

case Key.Home:
case Key.Up when e.KeyModifiers.HasFlag(KeyModifiers.Meta):
    if (vm.Results.Count > 0) vm.SelectedResult = vm.Results[0];
    e.Handled = true;
    break;

case Key.End:
case Key.Down when e.KeyModifiers.HasFlag(KeyModifiers.Meta):
    if (vm.Results.Count > 0) vm.SelectedResult = vm.Results[^1];
    e.Handled = true;
    break;
```

**Complejidad**: baja. Solo `MainWindow.axaml.cs`.

---

#### A2. Acción secundaria con Cmd+Enter / Ctrl+Enter
**Problema**: `Enter` ejecuta la única acción disponible por resultado. No hay forma de abrir en Finder, copiar ruta, revelar en terminal, etc.

**Referencia**: Raycast tiene un panel completo de acciones (Cmd+K). Alfred abre un menú de acciones con Cmd+Enter, que en apps incluye "Reveal in Finder", "Open with…", "Copy path".

**Notas del código**: `docs/ui-themes-keyboard.md` documenta que el footer hint `⌘K actions` es un placeholder sin handler.

**Ficheros a tocar**:
- `Yottacast.Core/ViewModels/ResultItemViewModel.cs` — añadir `IReadOnlyList<ResultAction> SecondaryActions`
- `Yottacast/Views/MainWindow.axaml.cs` — handler para `Key.Return` con `Meta` o panel `Cmd+K`
- `Yottacast/Views/MainWindow.axaml` — UI del panel de acciones (popup o panel lateral)
- `Yottacast.Core/Search/ApplicationSearch.cs`, `UserDocumentSearch.cs` — poblar acciones secundarias

**Boceto de modelo**:
```csharp
// En ResultItemViewModel.cs
public record ResultAction(string Label, string Icon, Action Execute);
public IReadOnlyList<ResultAction> SecondaryActions { get; init; } = [];
```

Acciones típicas por categoría:
- App: "Open", "Reveal in Finder", "Copy path", "Open in Terminal"
- File: "Open", "Reveal in Finder", "Copy path", "Copy filename"
- URL/Web: "Open in browser", "Copy URL"
- Calculator: "Copy result" (ya funciona), "Copy as formatted"

**Complejidad**: alta. Requiere modelo nuevo, UI de panel de acciones, y poblar acciones en cada fuente.

---

#### A3. Navegación circular vs. parada en los extremos
**Problema**: `SelectNext` en `MainWindow.axaml.cs` usa módulo: al llegar al último resultado y pulsar `↓`, salta al primero. Alfred para en los extremos (no circula). Raycast también para, pero con scroll suave.

La navegación circular puede confundir — el usuario pensaba que había llegado al final y aparece el primero de nuevo.

**Ficheros a tocar**:
- `Yottacast/Views/MainWindow.axaml.cs` — método `SelectNext` (línea 102-108)

**Implementación**:
```csharp
private static void SelectNext(MainWindowViewModel vm, int delta) {
    if (vm.Results.Count == 0) return;
    vm.NotifyUserNavigated();
    var current = vm.SelectedResult is null ? -1 : vm.Results.IndexOf(vm.SelectedResult);
    var next = Math.Clamp(current + delta, 0, vm.Results.Count - 1);
    vm.SelectedResult = vm.Results[next];
}
```

Alternativa: mantener circular pero añadir un feedback visual (un breve flash en el primer/último ítem).

**Complejidad**: baja.

---

### B. Historial y persistencia de búsquedas

#### B1. Historial de queries recientes
**Problema**: cada vez que el launcher se abre, el campo está vacío. Alfred y Raycast recuerdan las últimas búsquedas y el usuario puede navegar con `↑` para recuperarlas (inline history, como un shell).

**Referencia**: Alfred mantiene un historial de navegación por queries recientes accesible con `↑` cuando el campo está vacío.

**Ficheros a tocar**:
- `Yottacast.Core/Services/UserSettings.cs` — añadir `List<string> SearchHistory { get; set; }`
- `Yottacast/ViewModels/MainWindowViewModel.cs` — lógica de historial inline
- `Yottacast/Views/MainWindow.axaml.cs` — `OnKeyDown`: `↑` cuando el campo está vacío navega el historial

**Boceto de implementación**:
```csharp
// En MainWindowViewModel.cs
private int _historyIndex = -1;

private void NavigateHistory(int delta) {
    var history = _settings.SearchHistory;
    if (history.Count == 0) return;
    _historyIndex = Math.Clamp(_historyIndex + delta, 0, history.Count - 1);
    SearchText = history[_historyIndex];
}

// Guardar al activar un resultado con Enter
private void RecordToHistory(string query) {
    if (string.IsNullOrWhiteSpace(query)) return;
    var h = _settings.SearchHistory;
    h.Remove(query);          // evitar duplicados
    h.Insert(0, query);
    if (h.Count > 50) h.RemoveAt(h.Count - 1);
    _settings.Save();
}
```

En `MainWindow.axaml.cs`:
```csharp
case Key.Up when string.IsNullOrEmpty(vm.SearchText) && vm.Results.Count == 0:
    vm.NavigateHistory(-1);
    e.Handled = true;
    break;
```

**Complejidad**: media. Toca UserSettings (nuevo campo serializado), ViewModel y la vista.

---

### C. Feedback visual y animaciones

#### C1. Animación de entrada/salida de la ventana
**Problema**: la ventana aparece y desaparece instantáneamente (Show/Hide sincrónicos). Alfred y Raycast tienen una animación de escala+fade (≈120ms) que hace el launcher sentirse nativo.

**Restricción Avalonia**: no se puede animar `RenderTransform` con keyframes CSS (gotcha documentado en `CLAUDE.md`). Se puede animar `Opacity` y `ScaleTransform.ScaleX`/`ScaleY` como propiedades `double`.

**Ficheros a tocar**:
- `Yottacast/Views/MainWindow.axaml` — añadir animaciones de entrada en el `Border` raíz
- `Yottacast/Views/MainWindow.axaml.cs` — trigger de animación al mostrar/ocultar

**Boceto (AXAML)**:
```xml
<Border.Styles>
    <Style Selector="Border.launcher-root">
        <Style.Animations>
            <Animation Duration="0:0:0.12" FillMode="Forward" Easing="CubicEaseOut">
                <KeyFrame Cue="0%">
                    <Setter Property="Opacity" Value="0"/>
                    <Setter Property="RenderTransform" Value="scale(0.96)"/>
                </KeyFrame>
                <KeyFrame Cue="100%">
                    <Setter Property="Opacity" Value="1"/>
                    <Setter Property="RenderTransform" Value="scale(1)"/>
                </KeyFrame>
            </Animation>
        </Style.Animations>
    </Style>
</Border.Styles>
```

Nota: `RenderTransform` en `Border` (no en la `Window`) debería funcionar porque el gotcha afecta al tipo `ITransform` en keyframes CSS; las propiedades escalares `ScaleTransform.ScaleX` sí son animables. Habrá que verificar en runtime. Alternativa segura: solo animar `Opacity`.

**Complejidad**: media. El riesgo está en las restricciones de animación de Avalonia.

---

#### C2. Feedback visual al activar un resultado
**Problema**: al pulsar `Enter`, la ventana desaparece sin confirmación visual. El usuario no sabe si la acción se ejecutó o si hubo un error.

**Referencia**: Raycast muestra un breve flash del ítem seleccionado antes de cerrar. Alfred no hace flash pero cierra con un fade suave.

**Ficheros a tocar**:
- `Yottacast/Views/MainWindow.axaml` — estilo de flash en `ListBoxItem:selected` (animación de Opacity o Background)
- `Yottacast/Views/MainWindow.axaml.cs` — pequeño delay (≈80ms) entre la activación y el `Hide()`

**Boceto**:
```csharp
case Key.Return:
    if (vm.SelectedResult is { OnActivate: { } action } result) {
        action();
        // Brief visual confirmation before hiding
        await Task.Delay(80);
        vm.SearchText = "";
        Hide();
        // ... resto igual
    }
    break;
```

**Nota**: convertir `OnKeyDown` a `async` en Avalonia no es problemático (void async).

**Complejidad**: baja.

---

#### C3. Indicador de "no listo aún" al arranque
**Problema**: al arrancar, el usuario puede escribir inmediatamente pero `ApplicationSearch` devuelve vacío hasta que `WhenReady()` completa (el scan de `mdfind` puede tardar 1-3 segundos). No hay ningún indicador de que los resultados de apps no están listos aún.

**Referencia**: Spotlight muestra el spinner hasta que los resultados están completos. Raycast muestra inmediatamente los resultados de cache y el spinner mientras sigue indexando.

**Ficheros a tocar**:
- `Yottacast/ViewModels/MainWindowViewModel.cs` — exponer `bool IsStarting` mientras `globalSearch.WhenReady()` no haya completado
- `Yottacast/Views/MainWindow.axaml` — mostrar el spinner también cuando `IsStarting && !string.IsNullOrEmpty(SearchText)`
- `App.axaml.cs` — inyectar `GlobalSearch` en el ViewModel o agregar señal de ready

**Complejidad**: baja-media.

---

#### C4. Animación suave al reordenar resultados
**Problema**: `RefreshResults()` llama `Results.Clear()` seguido de `foreach (...) Results.Add(item)`. El `ListBox` re-renderiza completamente con cada cambio de snapshot (puede ocurrir varias veces durante la fase deferred). Visualmente los ítems "saltan" de posición.

**Referencia**: Raycast usa transiciones suaves cuando los resultados se reordenan. Alfred no reordena durante la búsqueda (muestra resultados finales).

**Solución**: en lugar de `Clear()` + `Add()`, usar un diff mínimo:
- Añadir ítems nuevos al final.
- Eliminar ítems que ya no estén.
- Reordenar (sin animación en la primera versión; en una segunda, `ItemsControl` con `ItemsPresenter` animado).

**Ficheros a tocar**:
- `Yottacast/ViewModels/MainWindowViewModel.cs` — método `RefreshResults`

**Boceto**:
```csharp
private void RefreshResults() {
    var merged = BuildMergedList();
    // Remove items no longer in results
    for (int i = Results.Count - 1; i >= 0; i--)
        if (!merged.Contains(Results[i])) Results.RemoveAt(i);
    // Insert/move items to correct positions
    for (int i = 0; i < merged.Count; i++) {
        var idx = Results.IndexOf(merged[i]);
        if (idx < 0) Results.Insert(i, merged[i]);
        else if (idx != i) Results.Move(idx, i);
    }
    // ... selección igual que antes
}
```

**Complejidad**: baja (el diff) a media (si se añaden transiciones CSS).

---

### D. Comportamiento del launcher al mostrar/ocultar

#### D1. Limpiar el texto al ocultar (configurable)
**Problema**: el estado se preserva cuando la ventana se oculta (documentado en `docs/ui-themes-keyboard.md`). Esto puede ser útil (retomar una búsqueda) o molesto (hay que limpiar manualmente al abrir de nuevo). Alfred y Raycast limpian el texto al cerrar por defecto, con opción de conservarlo.

**Ficheros a tocar**:
- `Yottacast.Core/Services/UserSettings.cs` — añadir `bool ClearOnHide { get; set; } = true`
- `Yottacast/Views/MainWindow.axaml.cs` — en `Hide()`: `if (vm?.Settings.ClearOnHide == true) vm.SearchText = ""`
- `Yottacast/ViewModels/SettingsWindowViewModel.cs` — exponer el toggle
- `Yottacast/Views/SettingsWindow.axaml` — añadir checkbox

**Complejidad**: baja.

---

#### D2. Posición de la ventana: centrada en pantalla activa vs. recordada
**Problema**: `WindowStartupLocation="CenterScreen"` en `MainWindow.axaml` siempre centra en la pantalla primaria. En setups multi-monitor, Alfred y Raycast aparecen en la pantalla donde está el cursor, o en la pantalla donde se lanzó el hotkey.

**Ficheros a tocar**:
- `Yottacast/Views/MainWindow.axaml` — cambiar `WindowStartupLocation` a `Manual`
- `Yottacast/Services/MacAppHandler.cs` / `WindowsAppHandler.cs` — en `OnShow()`, calcular la posición correcta (pantalla del cursor)
- `Yottacast/Services/AppHandler.cs` — añadir método abstracto `Point GetTargetPosition(Window)`

**Boceto (macOS)**:
```csharp
// En MacAppHandler.OnShow() — tras activar la app:
// Usar NSScreen.mainScreen (la pantalla con el cursor activo)
// y centrar la ventana en ella.
```

En Avalonia: `Screens.ScreenFromPoint(cursor)` da la pantalla activa.

**Complejidad**: media. Requiere P/Invoke en macOS para obtener la posición del cursor antes de que Avalonia tome el foco, o usar `Screens.ScreenFromWindow` al mostrar.

---

#### D3. Posición vertical configurable
**Problema**: el launcher siempre aparece centrado verticalmente. Alfred y Spotlight aparecen en la mitad superior de la pantalla (posición más natural para el ojo). Raycast permite elegir la posición Y.

**Ficheros a tocar**:
- `Yottacast.Core/Services/UserSettings.cs` — añadir `double WindowPositionY` (0.0–1.0, fracción de altura de pantalla)
- `Yottacast/Services/AppHandler.cs` — método de posicionamiento que lea el setting

**Complejidad**: baja (una vez resuelto D2).

---

### E. Shortcuts adicionales y acciones contextuales

#### E1. Cmd+K — panel de acciones
**Problema**: el footer ya muestra el hint `⌘K actions` (hardcodeado en `MainWindow.axaml`, línea 253) pero no hay ningún handler. Es un placeholder visual sin implementar.

**Referencia**: Raycast tiene un panel de acciones completo con Cmd+K que muestra todas las acciones secundarias del ítem seleccionado. Alfred usa Cmd+Enter para la acción alternativa.

**Prerequisito**: A2 (modelo de acciones secundarias).

**Ficheros a tocar**:
- `Yottacast/Views/MainWindow.axaml.cs` — handler `Key.K` con `KeyModifiers.Meta`
- `Yottacast/Views/MainWindow.axaml` — panel de acciones (popup o overlay)

**Boceto de UI**: un `Popup` anclado al ítem seleccionado, con una lista de `ResultAction`. Teclas: `↑↓` navegan, `Enter` ejecuta, `Esc` cierra.

**Complejidad**: alta (depende de A2 y requiere diseño de UI del panel).

---

#### E2. Cmd+C — copiar el título/ruta del resultado seleccionado
**Problema**: no hay shortcut para copiar información del resultado sin activarlo.

**Referencia**: Alfred copia el path de apps/archivos con Cmd+C. Raycast copia el resultado de calculadora con Cmd+C.

**Ficheros a tocar**:
- `Yottacast/Views/MainWindow.axaml.cs` — handler `Key.C` con `KeyModifiers.Meta`
- `Yottacast.Core/ViewModels/ResultItemViewModel.cs` — añadir `string? CopyableValue` (el path o el valor a copiar)
- `Yottacast.Core/Services/ClipboardService.cs` — ya existe `CopyText`

**Boceto**:
```csharp
case Key.C when e.KeyModifiers.HasFlag(KeyModifiers.Meta):
    if (vm.SelectedResult?.CopyableValue is { } val) {
        _clipboardService.CopyText(val);
        // Opcional: feedback visual breve
    }
    e.Handled = true;
    break;
```

**Complejidad**: baja.

---

#### E3. URL directa — abrir URLs pegadas directamente
**Problema**: si el usuario pega una URL completa (`https://...`), no hay resultado que la abra directamente. Solo aparece la búsqueda de Google.

**Referencia**: Alfred, Raycast y Spotlight detectan URLs y ofrecen "Open URL" como primera opción.

**Ficheros a tocar**:
- `Yottacast.Core/Search/` — nueva `UrlSearch.cs` (ISearchSource instant)
- `App.axaml.cs` / `BuildServices()` — registrar `UrlSearch` como `ISearchSource`

**Boceto**:
```csharp
public class UrlSearch(BrowserDiscovery browserDiscovery, UserSettings settings) : ISearchSource {
    public bool IsInstant => true;
    public async IAsyncEnumerable<IReadOnlyList<ResultItemViewModel>> SearchAsync(
        string query, int limit, CancellationToken ct) {
        if (!Uri.TryCreate(query, UriKind.Absolute, out var uri)
            || (uri.Scheme != "http" && uri.Scheme != "https"))
            yield break;
        var url = query;
        yield return [new ResultItemViewModel {
            Icon = "🌐",
            Title = $"Open {uri.Host}",
            Subtitle = url,
            Category = "URL",
            Score = 10,  // por encima del resultado de Google
            OnActivate = () => browserDiscovery.OpenUrl(url, settings.ActiveBrowser!),
        }];
    }
    // ...
}
```

**Complejidad**: baja.

---

### F. Gestión del foco y comportamiento de plataforma

#### F1. Windows: restaurar foco al ocultar
**Problema**: `WindowsAppHandler.OnShow()` y `OnHide()` son métodos vacíos. En Windows no se captura la app frontmost antes de mostrar el launcher, ni se restaura al ocultar. Esto rompe el flujo de uso: tras usar el launcher, el foco no vuelve al editor o al navegador donde estaba el usuario.

**Referencia**: `MacAppHandler` resuelve esto correctamente con `NSWorkspace.frontmostApplication` y `activateWithOptions:`.

**Ficheros a tocar**:
- `Yottacast/Services/WindowsAppHandler.cs` — implementar `OnShow()` (capturar HWND del foreground window) y `OnHide()` (restaurar con `SetForegroundWindow`)

**Boceto (Windows P/Invoke)**:
```csharp
[DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
[DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);

private IntPtr _previousHwnd = IntPtr.Zero;

public override void OnShow() {
    _previousHwnd = GetForegroundWindow();
}

public override void OnHide() {
    if (_previousHwnd != IntPtr.Zero)
        SetForegroundWindow(_previousHwnd);
    _previousHwnd = IntPtr.Zero;
}
```

**Complejidad**: baja. Patrón idéntico al de `MacAppHandler`.

---

#### F2. Accesibilidad: anunciar el resultado seleccionado
**Problema**: no hay soporte de accesibilidad (screen readers). Al cambiar el ítem seleccionado con `↑`/`↓`, ningún anuncio llega a VoiceOver/Narrator.

**Referencia**: Spotlight tiene soporte completo de VoiceOver en macOS.

**Ficheros a tocar**:
- `Yottacast/Views/MainWindow.axaml` — `AutomationProperties.Name` en cada `ListBoxItem`
- `Yottacast/Views/MainWindow.axaml.cs` — `SelectNext` podría emitir un evento de accesibilidad

**Complejidad**: media. Requiere investigar la API de accesibilidad de Avalonia 11.

---

#### F3. Foco perdido al abrir SettingsWindow
**Problema**: `App.OpenSettings()` (línea 82-95 de `App.axaml.cs`) muestra `SettingsWindow` sin gestionar el foco del launcher. En macOS, la SettingsWindow tiene decoraciones nativas pero no hay ningún mecanismo que garantice que el foco vuelva al launcher si el usuario cierra Settings con ESC.

**SettingsWindow.OnClosing** cancela el cierre y oculta, pero no reactiva el launcher.

**Ficheros a tocar**:
- `Yottacast/Views/SettingsWindow.axaml.cs` — en `OnClosing`, notificar al launcher para que reactive el foco
- `Yottacast/Views/MainWindow.axaml.cs` — método `OnSettingsWindowClosed`

**Complejidad**: baja.

---

## 3. Comparativa con Alfred / Raycast / Spotlight

| Función | Alfred 5 | Raycast | Spotlight | Yottacast actual |
|---|---|---|---|---|
| Búsqueda de apps | si | si | si | si |
| Búsqueda de ficheros | si | si | si | si |
| Calculadora | si | si | si | si |
| Conversor de unidades | si | si | no | si |
| Emoji picker | si (via :) | si (via :) | no | si (via :) |
| URL directa | si | si | si | no (mejora E3) |
| Historial inline (↑) | si | no | no | no (mejora B1) |
| Frecuency boost | si | si | si | no (ver plans/scoring.md) |
| Acciones secundarias (Cmd+K) | si | si | no | no (mejora A2 + E1) |
| Preview del ítem | si (grande) | si | si | no |
| Restaurar foco al cerrar | si | si | si | mac: si / win: no (mejora F1) |
| Animación entrada/salida | si | si | si | no (mejora C1) |
| Score visible | no | no | no | si (ver plans/scoring.md §7) |
| Tab para navegar | si | no | no | no (mejora A1) |
| Home/End | no | no | no | no (mejora A1) |
| Posición en pantalla activa | si | si | si | no (mejora D2) |
| Limpiar al ocultar (config.) | si | si | si | no (mejora D1) |
| Copiar ruta/valor (Cmd+C) | si | si | no | no (mejora E2) |
| Accesibilidad | si | parcial | si | no (mejora F2) |
| Temas | si (marketplace) | si (marketplace) | no | si (local) |

---

## 4. Orden de implementación recomendado

Agrupado por sprint, priorizando impacto UX vs. esfuerzo:

**Sprint 1 — Quick wins (1-2 días)**
1. A1: Teclas Tab, Home, End
2. A3: Parar en extremos (no circular)
3. C2: Flash de feedback al activar
4. F1: Restaurar foco en Windows

**Sprint 2 — Funciones de calidad de vida (3-5 días)**
6. E3: Detector de URL directa
7. B1: Historial inline de queries
8. D1: Limpiar texto al ocultar (con toggle en Settings)
9. C4: Diff incremental en RefreshResults
10. E2: Cmd+C para copiar valor del resultado

**Sprint 3 — Features diferenciales (1-2 semanas)**
11. C1: Animación entrada/salida de ventana
12. D2: Posición en pantalla con cursor
13. C3: Indicador "arranque en curso"

**Sprint 4 — Funciones avanzadas (2-4 semanas)**
15. A2 + E1: Modelo de acciones secundarias + panel Cmd+K
16. F2: Accesibilidad (VoiceOver/Narrator)
17. Preview del ítem seleccionado (panel lateral)

---

## 5. Notas de implementación transversales

- **Regla de plataforma**: todo el código OS-específico de UI va en `AppHandler` y subclases. El código de `MainWindow.axaml.cs` no debe contener `OperatingSystem.IsMacOS()`. Ver `CLAUDE.md`.
- **Animaciones Avalonia**: no animar `ITransform` con keyframes CSS. Usar propiedades `double` (`ScaleTransform.ScaleX`, `Opacity`). Ver gotcha en `CLAUDE.md`.
- **`ResultItemViewModel` es inmutable** (`init`). Para añadir `SecondaryActions` o `CopyableValue`, añadir propiedades `init` sin cambiar el patrón.
- **Shortcut `Shortcut`**: la propiedad `ResultItemViewModel.Shortcut` existe pero nunca se asigna. Al implementar acciones secundarias, asignar el shortcut del primero (ej. `"⌘K"`) para que el badge sea visible.
- **Score del Google item y URL directa**: ver `plans/scoring.md` para los rangos de score recomendados. El item de URL directa debe tener score mayor que Google para aparecer por encima.
- **`_userNavigated`**: al implementar el historial (B1), establecer `_userNavigated = false` al navegar el historial (el usuario está refinando la query, no eligiendo un resultado), y `true` solo al navegar los resultados con `↑`/`↓`.
