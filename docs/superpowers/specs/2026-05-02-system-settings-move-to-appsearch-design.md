# Move System Settings Toggle into App Search

## Objetivo

Eliminar la sección "System Settings" del menú lateral de Settings y mover el toggle a la sección "App Search", renombrado como "Include system settings panels". La búsqueda en paneles del sistema requiere que ambos flags estén activos: `EnableAppSearch` y `EnableSystemSettings`.

---

## Cambios en UI (`SettingsWindow.axaml`)

1. **Eliminar** el botón de navegación "System Settings" del sidebar (actualmente condicionado por `IsSystemSettingsSectionVisible`).
2. **Eliminar** el panel de contenido `<!-- System Settings -->` con su `ToggleSwitch` y texto descriptivo.
3. **Añadir** dentro del bloque App Search, dentro del `StackPanel` condicionado a `IsVisible="{Binding EnableAppSearch}"`, un nuevo `ToggleSwitch`:
   - `IsChecked="{Binding EnableSystemSettings}"`
   - Label: "Include system settings panels"
   - `IsVisible="{Binding IsSystemSettingsSectionVisible}"` — la propiedad ya existe y limita la opción a macOS (donde `SupportsSystemSettingsSearch` es `true`)

---

## Cambios en ViewModel (`SettingsWindowViewModel.cs`)

Eliminar todo lo relacionado con la navegación a la sección System Settings:

- `SystemSettings` del enum `SettingsSection`
- Propiedad `IsSystemSettingsSelected`
- Atributo `[NotifyPropertyChangedFor(nameof(IsSystemSettingsSelected))]` sobre `_selectedSection`
- Método `SelectSystemSettings()` y su `[RelayCommand]`

Se conservan sin cambios:
- `_enableSystemSettings` (campo observable)
- `OnEnableSystemSettingsChanged` (guarda, loguea, notifica búsqueda)
- `IsSystemSettingsSectionVisible` (controla visibilidad en macOS)
- Inicialización en constructor

---

## Cambios en búsqueda (`SystemSettingsSearch.cs`)

La búsqueda en paneles del sistema requiere ambos flags activos. Actualizar las dos comprobaciones existentes:

**`Start()`** — actualmente:
```csharp
if (!settings.EnableSystemSettings) {
```
→ cambiar a:
```csharp
if (!settings.EnableSystemSettings || !settings.EnableAppSearch) {
```

**`Search()`** — actualmente:
```csharp
if (!settings.EnableSystemSettings) return [];
```
→ cambiar a:
```csharp
if (!settings.EnableSystemSettings || !settings.EnableAppSearch) return [];
```

---

## Invariantes

- El toggle "Include system settings panels" solo es visible en macOS (mismo guard que antes: `IsSystemSettingsSectionVisible`).
- El toggle solo es visible cuando App Search está habilitado (está dentro del bloque `IsVisible="{Binding EnableAppSearch}"`).
- Si `EnableAppSearch = false`, los paneles del sistema no aparecen en búsqueda aunque `EnableSystemSettings = true`.
- El comportamiento de persistencia, logging y `NotifySearchSettingsChanged` no cambia.

---

## Ficheros afectados

- `Yottacast/Views/SettingsWindow.axaml`
- `Yottacast/ViewModels/SettingsWindowViewModel.cs`
- `Yottacast.Core/Search/SystemSettings/SystemSettingsSearch.cs`
