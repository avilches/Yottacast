# Move System Settings into App Search Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Mover el toggle de System Settings a la sección App Search (renombrado "Include system settings panels") y hacer que la búsqueda requiera ambos flags activos.

**Architecture:** Tres cambios coordinados: eliminar la sección nav de System Settings del AXAML y su panel, añadir un toggle dentro de App Search, limpiar el ViewModel de la navegación obsoleta, y actualizar `SystemSettingsSearch` para requerir `EnableAppSearch && EnableSystemSettings`.

**Tech Stack:** Avalonia 11 AXAML, .NET 9, CommunityToolkit.Mvvm

---

### Task 1: Mover toggle, limpiar ViewModel, actualizar búsqueda

**Files:**
- Modify: `Yottacast/Views/SettingsWindow.axaml`
- Modify: `Yottacast/ViewModels/SettingsWindowViewModel.cs`
- Modify: `Yottacast.Core/Search/SystemSettings/SystemSettingsSearch.cs`

- [ ] **Step 1: Eliminar botón nav "System Settings" del sidebar**

En `Yottacast/Views/SettingsWindow.axaml`, eliminar el bloque completo (líneas ~510-518):

```xml
<Button Classes="nav-item"
        Classes.nav-selected="{Binding IsSystemSettingsSelected}"
        Command="{Binding SelectSystemSettingsCommand}"
        IsVisible="{Binding IsSystemSettingsSectionVisible}">
    <StackPanel Orientation="Horizontal" Spacing="8">
        <PathIcon Data="{StaticResource Icon.SystemSettings}" Width="14" Height="14" VerticalAlignment="Center"/>
        <TextBlock Text="System Settings" VerticalAlignment="Center"/>
    </StackPanel>
</Button>
```

- [ ] **Step 2: Eliminar el panel de contenido "System Settings"**

En `Yottacast/Views/SettingsWindow.axaml`, eliminar el bloque completo (líneas ~1201-1209):

```xml
<!-- System Settings -->
<StackPanel Spacing="16" IsVisible="{Binding IsSystemSettingsSelected}">
    <TextBlock Classes="section-heading" Text="System Settings"/>
    <ToggleSwitch IsChecked="{Binding EnableSystemSettings}"
                  OnContent="Enabled"
                  OffContent="Disabled"/>
    <TextBlock Classes="description"
               Text="Search macOS System Settings panels from the launcher. Type a panel name like 'Wi-Fi', 'Bluetooth', or 'Displays'."/>
</StackPanel>
```

- [ ] **Step 3: Añadir toggle "Include system settings panels" dentro de App Search**

En `Yottacast/Views/SettingsWindow.axaml`, dentro del bloque App Search, al final del `StackPanel` condicionado con `IsVisible="{Binding EnableAppSearch}"` (justo antes del `</StackPanel>` que cierra ese bloque en la línea ~698), añadir:

```xml
                        <StackPanel Spacing="4" IsVisible="{Binding IsSystemSettingsSectionVisible}">
                            <ToggleSwitch IsChecked="{Binding EnableSystemSettings}"
                                          OnContent="Include system settings panels"
                                          OffContent="Include system settings panels"/>
                        </StackPanel>
```

El `StackPanel` wrapper con `IsVisible="{Binding IsSystemSettingsSectionVisible}"` garantiza que el toggle solo aparezca en macOS.

- [ ] **Step 4: Limpiar el ViewModel — eliminar navegación de System Settings**

En `Yottacast/ViewModels/SettingsWindowViewModel.cs`:

**4a.** En el enum `SettingsSection` (línea ~22), eliminar `SystemSettings`:
```csharp
// Antes:
public enum SettingsSection {
    General, AppSearch, WebSearch, FileSearch, Calculator, Clipboard, Emoji, Dictionary, History, SystemSettings
}
// Después:
public enum SettingsSection {
    General, AppSearch, WebSearch, FileSearch, Calculator, Clipboard, Emoji, Dictionary, History
}
```

**4b.** Eliminar el atributo sobre `_selectedSection` (línea ~38):
```csharp
// Eliminar esta línea:
[NotifyPropertyChangedFor(nameof(IsSystemSettingsSelected))]
```

**4c.** Eliminar la propiedad `IsSystemSettingsSelected` (línea ~56):
```csharp
// Eliminar esta línea:
public bool IsSystemSettingsSelected      => SelectedSection == SettingsSection.SystemSettings;
```

**4d.** Eliminar el comando `SelectSystemSettings` (línea ~68):
```csharp
// Eliminar esta línea:
[RelayCommand] private void SelectSystemSettings() => SelectedSection = SettingsSection.SystemSettings;
```

- [ ] **Step 5: Actualizar `SystemSettingsSearch` para requerir ambos flags**

En `Yottacast.Core/Search/SystemSettings/SystemSettingsSearch.cs`:

**5a.** En `Start()` (línea ~33), reemplazar:
```csharp
if (!settings.EnableSystemSettings) {
```
Por:
```csharp
if (!settings.EnableSystemSettings || !settings.EnableAppSearch) {
```

**5b.** En `Search()` (línea ~48), reemplazar:
```csharp
if (!settings.EnableSystemSettings) return [];
```
Por:
```csharp
if (!settings.EnableSystemSettings || !settings.EnableAppSearch) return [];
```

- [ ] **Step 6: Compilar**

```bash
cd Yottacast && dotnet build
```

Esperado: sin errores. Si hay un error de compilación por `SettingsSection.SystemSettings` usado en algún otro sitio, localizar y eliminar esa referencia también.

- [ ] **Step 7: Ejecutar tests**

```bash
cd Yottacast.Core.Tests && dotnet test
```

Esperado: todos los tests pasan.

- [ ] **Step 8: Commit**

```bash
git add Yottacast/Views/SettingsWindow.axaml \
        Yottacast/ViewModels/SettingsWindowViewModel.cs \
        Yottacast.Core/Search/SystemSettings/SystemSettingsSearch.cs
git commit -m "feat: move system settings toggle into app search section"
```
