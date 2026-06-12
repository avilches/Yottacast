# Escape / Pills / Footer Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Corregir el comportamiento de Escape (que solo borra texto / cierra ventana), añadir pill "All", hacer los pills individualmente clickables con estilo sutil, y mover el hint "⌘F cycle" al footer.

**Architecture:** Cambios en tres capas: ViewModel (nueva propiedad `AllModeActive` + `CycleShortcutText`), AXAML (pills y footer), y code-behind (Escape y `OnModePillTapped`). No hay nuevos tokens de tema: los estilos de pill usan `Theme.Search.Color` con `BorderThickness`.

**Tech Stack:** Avalonia 11, CommunityToolkit.Mvvm, C# 13, .NET 9

**Spec:** `docs/superpowers/specs/2026-06-12-escape-pills-footer-redesign.md`

---

## Task 1: Fix Escape — eliminar el branch de reset de modo

**Files:**
- Modify: `Yottacast/Views/MainWindow.axaml.cs` (~línea 609)

- [ ] **Eliminar el `else if (vm.ShowModePill)` del case Escape**

Localizar el bloque (líneas ~609-631) y sustituirlo por:

```csharp
case Key.Escape:
    if (vm.IsEditorOpen) {
        if (vm.EditorPanel.ShowUnsavedDialog)
            vm.EditorPanel.CancelUnsavedDialog();
        else
            vm.EditorPanel.RequestClose();
        e.Handled = true;
        break;
    }
    if (vm.IsOptionsMenuOpen) {
        vm.CloseOptionsMenu();
    } else if (vm.IsSearching) {
        vm.CancelDeferredSearch();
        vm.CleanAndSaveHistory(null);
    } else if (!string.IsNullOrEmpty(vm.SearchText)) {
        vm.CleanAndSaveHistory(null);
    } else {
        Hide();
    }
    e.Handled = true;
    break;
```

- [ ] **Verificar manualmente**

Activar Files mode (Cmd+F), escribir texto, pulsar Escape → texto se borra (modo sigue en Files).
Pulsar Escape de nuevo → ventana se cierra.
Abrir ventana → modo sigue en Files (persiste).

- [ ] **Commit**

```bash
git add Yottacast/Views/MainWindow.axaml.cs
git commit -m "fix: escape no resetea el modo activo, solo borra texto o cierra ventana"
```

---

## Task 2: ViewModel — AllModeActive y CycleShortcutText

**Files:**
- Modify: `Yottacast/ViewModels/MainWindowViewModel.cs`

- [ ] **Añadir AllModeActive y CycleShortcutText junto a las propiedades similares (líneas ~185-196)**

Localizar el bloque:
```csharp
public bool ShowModePill => AvailableModes.Count > 0;

public bool HasFilesMode     => AvailableModes.Contains(SearchMode.Files);
public bool HasClipboardMode => AvailableModes.Contains(SearchMode.Clipboard);
public bool FilesModeActive     => _activeMode == SearchMode.Files;
public bool ClipboardModeActive => _activeMode == SearchMode.Clipboard;
```

Sustituirlo por:
```csharp
public bool ShowModePill => AvailableModes.Count > 0;

public bool HasFilesMode      => AvailableModes.Contains(SearchMode.Files);
public bool HasClipboardMode  => AvailableModes.Contains(SearchMode.Clipboard);
public bool AllModeActive       => _activeMode == SearchMode.All;
public bool FilesModeActive     => _activeMode == SearchMode.Files;
public bool ClipboardModeActive => _activeMode == SearchMode.Clipboard;

public string CycleShortcutText => $"{MetaSymbol}F  cycle";
```

- [ ] **Añadir `OnPropertyChanged(nameof(AllModeActive))` en el setter de ActiveMode**

Localizar el setter de `ActiveMode` (~línea 170) y añadir la notificación:

```csharp
private set {
    if (_activeMode == value) return;
    _activeMode = value;
    OnPropertyChanged();
    OnPropertyChanged(nameof(ShowModePill));
    OnPropertyChanged(nameof(ActiveModeName));
    OnPropertyChanged(nameof(AllModeActive));        // <-- añadir
    OnPropertyChanged(nameof(FilesModeActive));
    OnPropertyChanged(nameof(ClipboardModeActive));
    if (!string.IsNullOrWhiteSpace(SearchText)) {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        _userNavigated = false;
        _ = SearchAsync(SearchText.Trim(), _cts.Token);
    }
}
```

- [ ] **Compilar**

```bash
dotnet build Yottacast/Yottacast.csproj
```
Expected: Build succeeded, 0 errors.

- [ ] **Commit**

```bash
git add Yottacast/ViewModels/MainWindowViewModel.cs
git commit -m "feat: AllModeActive y CycleShortcutText en MainWindowViewModel"
```

---

## Task 3: AXAML pills — pill "All", x:Names, eliminar hint inline

**Files:**
- Modify: `Yottacast/Views/MainWindow.axaml` (sección "Mode chips", ~líneas 299-321)
- Modify: `Yottacast/Views/MainWindow.axaml.cs` (añadir `using`, reescribir `OnModePillTapped`)

### 3a — Actualizar la sección de pills en AXAML

Localizar el bloque `<!-- ── Mode chips -->` y sustituirlo por:

```xml
<!-- ── Mode chips (visibles cuando hay modos disponibles) ── -->
<Border DockPanel.Dock="Top"
        IsVisible="{Binding ShowModePill}"
        Padding="12,0,12,6"
        Tapped="OnModePillTapped">
    <StackPanel Orientation="Horizontal" Spacing="6">
        <Border x:Name="AllModePill"
                Classes="mode-chip"
                Classes.active="{Binding AllModeActive}">
            <TextBlock Text="All" FontSize="12"/>
        </Border>
        <Border x:Name="FilesModePill"
                Classes="mode-chip"
                Classes.active="{Binding FilesModeActive}"
                IsVisible="{Binding HasFilesMode}">
            <TextBlock Text="Files" FontSize="12"/>
        </Border>
        <Border x:Name="ClipboardModePill"
                Classes="mode-chip"
                Classes.active="{Binding ClipboardModeActive}"
                IsVisible="{Binding HasClipboardMode}">
            <TextBlock Text="Clipboard" FontSize="12"/>
        </Border>
    </StackPanel>
</Border>
```

Nota: el `<TextBlock Text="⌘F to cycle · Esc to exit" .../>` que había antes **se elimina** de este bloque.

### 3b — Actualizar OnModePillTapped en code-behind

- [ ] **Añadir `using Yottacast.Core.Search;` a los imports de `MainWindow.axaml.cs`**

Localizar el bloque de usings (~línea 13) y añadir después de `using Yottacast.Core;`:

```csharp
using Yottacast.Core;
using Yottacast.Core.Search;   // <-- añadir
using Yottacast.Core.Services;
```

- [ ] **Reescribir `OnModePillTapped` (~línea 820)**

Sustituir el método completo:

```csharp
private void OnModePillTapped(object? sender, TappedEventArgs e) {
    if (DataContext is not MainWindowViewModel vm) return;
    var source = e.Source as Visual;
    while (source != null) {
        if (source == AllModePill)      { vm.ResetMode();                          break; }
        if (source == FilesModePill)    { vm.ActivateMode(SearchMode.Files);       break; }
        if (source == ClipboardModePill){ vm.ActivateMode(SearchMode.Clipboard);   break; }
        source = source.GetVisualParent();
    }
    e.Handled = true;
}
```

- [ ] **Compilar**

```bash
dotnet build Yottacast/Yottacast.csproj
```
Expected: Build succeeded, 0 errors.

- [ ] **Verificar manualmente**

Con Files y Clipboard en ModeOnly:
- Aparecen los pills: All · Files · Clipboard
- Click en "Files" → pill Files se activa, búsqueda cambia a Files mode
- Click en "All" → pill All se activa, vuelve al modo All
- Cmd+F rota entre All → Files → Clipboard → All y los pills reflejan el estado

- [ ] **Commit**

```bash
git add Yottacast/Views/MainWindow.axaml Yottacast/Views/MainWindow.axaml.cs
git commit -m "feat: pill All, x:Names, OnModePillTapped con click individual"
```

---

## Task 4: Estilos sutiles para los pills

**Files:**
- Modify: `Yottacast/Views/MainWindow.axaml` (sección `<Window.Styles>`, ~líneas 53-66)

- [ ] **Sustituir los tres bloques de estilos `mode-chip`**

Localizar:
```xml
<!-- Mode chip styles -->
<Style Selector="Border.mode-chip">
    <Setter Property="CornerRadius" Value="12"/>
    <Setter Property="Padding" Value="10,4"/>
    <Setter Property="Opacity" Value="0.4"/>
</Style>
<Style Selector="Border.mode-chip.active">
    <Setter Property="Background" Value="{DynamicResource Theme.Results.SelectionBar.Color}"/>
    <Setter Property="Opacity" Value="1"/>
</Style>
<Style Selector="Border.mode-chip.active TextBlock">
    <Setter Property="Foreground" Value="White"/>
    <Setter Property="FontWeight" Value="Medium"/>
</Style>
```

Sustituir por:
```xml
<!-- Mode chip styles -->
<Style Selector="Border.mode-chip">
    <Setter Property="CornerRadius" Value="12"/>
    <Setter Property="Padding" Value="10,4"/>
    <Setter Property="BorderThickness" Value="0"/>
    <Setter Property="Opacity" Value="0.35"/>
</Style>
<Style Selector="Border.mode-chip.active">
    <Setter Property="BorderBrush" Value="{DynamicResource Theme.Search.Color}"/>
    <Setter Property="BorderThickness" Value="1"/>
    <Setter Property="Opacity" Value="1"/>
</Style>
<Style Selector="Border.mode-chip.active TextBlock">
    <Setter Property="FontWeight" Value="Medium"/>
</Style>
```

Efecto: inactivo = texto tenue (0.35 opacidad, sin borde); activo = texto completo + borde sutil de 1px del color del texto de búsqueda.

- [ ] **Verificar visualmente**

Abrir Yottacast con modos disponibles:
- Pills inactivos se ven tenues, sin relleno
- Pill activo tiene borde delgado y texto a plena opacidad
- El contraste no es gritón

- [ ] **Commit**

```bash
git add Yottacast/Views/MainWindow.axaml
git commit -m "style: pills de modo sutiles (outlined, sin fondo de acento)"
```

---

## Task 5: Footer — hint de ciclo al lado de Settings

**Files:**
- Modify: `Yottacast/Views/MainWindow.axaml` (sección footer, ~líneas 368-402)

- [ ] **Cambiar el lado izquierdo del footer**

Localizar en el footer:
```xml
<!-- Left: Settings shortcut hint -->
<TextBlock Grid.Column="0"
           Text="{Binding SettingsShortcutText}"
           Foreground="{DynamicResource Theme.Footer.Color}"
           FontSize="{DynamicResource Theme.Footer.Size}"
           VerticalAlignment="Center"/>
```

Sustituir por:
```xml
<!-- Left: Settings + optional cycle hint -->
<StackPanel Grid.Column="0"
            Orientation="Horizontal"
            Spacing="16"
            VerticalAlignment="Center">
    <TextBlock Text="{Binding SettingsShortcutText}"
               Foreground="{DynamicResource Theme.Footer.Color}"
               FontSize="{DynamicResource Theme.Footer.Size}"/>
    <TextBlock Text="{Binding CycleShortcutText}"
               Foreground="{DynamicResource Theme.Footer.Color}"
               FontSize="{DynamicResource Theme.Footer.Size}"
               IsVisible="{Binding ShowModePill}"/>
</StackPanel>
```

- [ ] **Compilar**

```bash
dotnet build Yottacast/Yottacast.csproj
```
Expected: Build succeeded, 0 errors.

- [ ] **Verificar manualmente**

Sin modos configurados: footer solo muestra "⌘,  settings". Sin hint de cycle.
Con Files/Clipboard en ModeOnly: footer muestra "⌘,  settings    ⌘F  cycle".

- [ ] **Commit**

```bash
git add Yottacast/Views/MainWindow.axaml
git commit -m "feat: hint Cmd+F cycle en footer, visible solo cuando hay modos"
```

---

## Task 6: Actualizar docs/ui-main-window.md

**Files:**
- Modify: `docs/ui-main-window.md`

- [ ] **Actualizar sección 5 — Tecla Escape**

Localizar la subsección "Tecla Escape -- jerarquía de tres niveles" y actualizar la descripción:

```markdown
### Tecla Escape — jerarquía

El handler de ESC aplica esta lógica en cascada:

1. Si hay un editor abierto: cierra el editor (o cancela el diálogo de cambios sin guardar).
2. Si hay un menú de opciones abierto: lo cierra.
3. Si hay una búsqueda diferida en curso (`IsSearching == true`): cancela la fase diferida **y limpia el texto de búsqueda**.
4. Si el texto no está vacío: limpia el texto.
5. Si el texto ya está vacío: oculta la ventana.

El modo activo (All / Files / Clipboard) no cambia al pulsar Escape. Persiste hasta que el usuario lo cambie via `Cmd+F` o click en un pill.
```

- [ ] **Actualizar sección 11 — Footer**

En la tabla de la sección "Lado izquierdo", añadir mención al hint de ciclo:

```markdown
### Lado izquierdo: Settings y hint de ciclo

El lado izquierdo del footer muestra:
- Siempre: el atajo de Settings (`⌘,  settings` en macOS).
- Sólo cuando hay modos de búsqueda disponibles (`ShowModePill`): el hint `⌘F  cycle`.
```

- [ ] **Commit**

```bash
git add docs/ui-main-window.md
git commit -m "docs: actualizar ui-main-window con nueva jerarquía de escape y footer cycle hint"
```

---

## Verificación final

- [ ] Ejecutar `dotnet build Yottacast/Yottacast.csproj` — sin errores
- [ ] Ejecutar tests: `cd Yottacast.Core.Tests && dotnet test` — todos pasan
- [ ] Smoke test manual:
  - Con sólo Files en ModeOnly: aparecen pills All y Files; Clipboard no aparece
  - Con Files y Clipboard en ModeOnly: aparecen los tres pills
  - Cmd+F rota All → Files → Clipboard → All reflejado en pills
  - Click en cada pill lo activa
  - Escape con texto: borra texto (modo no cambia)
  - Escape sin texto: cierra ventana
  - Footer muestra "⌘F  cycle" sólo cuando hay pills
  - Sin modos en ModeOnly: no aparece ningún pill, no aparece hint en footer
