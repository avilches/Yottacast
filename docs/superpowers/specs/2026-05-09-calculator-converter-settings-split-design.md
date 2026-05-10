# Spec: Split Calculator / Converter Settings + Clickable Examples

**Fecha:** 2026-05-09
**Estado:** aprobado

---

## Propósito

Separar la sección "Calculator" de los Settings en dos secciones independientes (Calculator y Converter), cada una con su propio toggle de activación y ejemplos clickables. Al hacer clic en un ejemplo se abre la ventana principal y se rellena el buscador con la query del ejemplo.

---

## Comportamiento esperado

### Secciones del sidebar

| Antes | Después |
|-------|---------|
| Calculator (única sección) | Calculator + Converter (dos entradas separadas) |

### Sección Calculator

Contiene:
- Toggle `EnableCalculator` (siempre visible)
- Si activo: Decimal places config + bloque de ejemplos

Ejemplos (al final, bajo la configuración):

| Query | Resultado mostrado |
|-------|-------------------|
| `2 + 3 * 4` | → 14 |
| `sqrt(144)` | → 12 |
| `2x - 5 = 2` | → x = 3.5 |
| `x^2 - 5x + 6 = 0` | → x = 2, 3 |

### Sección Converter

Contiene:
- Toggle `EnableConverter` (siempre visible)
- Si activo: Currency pair + Include metals + Include crypto + Refresh interval + Last rate update + bloque de ejemplos

Ejemplos (al final, bajo la configuración):

| Query | Resultado mostrado |
|-------|-------------------|
| `10 km to miles` | → 6.21 mi |
| `100 F to C` | → 37.78 °C |
| `60 km/h to mph` | → 37.28 mph |
| `100 USD` | → EUR (live) |
| `500 EUR to GBP` | → GBP (live) |

### Invariantes de UI

- El toggle se muestra siempre, independientemente de su estado.
- Todo lo que hay debajo del toggle (opciones + ejemplos) se colapsa cuando el toggle está desactivado (`IsVisible="{Binding EnableX}"`).
- Los ejemplos de divisas muestran "(live)" para indicar que el resultado es dinámico.

### Acción al hacer clic en un ejemplo

1. La ventana principal se muestra (`AppHandler.Instance.ShowWindow(mainWindow)`).
2. El campo de búsqueda se rellena con la query del ejemplo (`MainWindowViewModel.SearchText = query`).
3. La búsqueda se dispara automáticamente por `OnSearchTextChanged`.
4. Si la ventana ya estaba visible (modo sticky), el texto se rellena y la ventana permanece visible.

---

## Lógica de routing en CalculatorSearch

### Nuevo campo

`EnableConverter` (`bool`, default `true`) en `UserSettings` y `UserSettingsData`.

### Guard al inicio de Search()

```csharp
if (!settings.EnableCalculator && !settings.EnableConverter) return [];
```

### Filtro por tipo de resultado

| Tipo de resultado | Toggle que se comprueba |
|-------------------|------------------------|
| `CalcResult` (aritmética) | `EnableCalculator` |
| Ecuación (nerdamer) | `EnableCalculator` |
| `ConversionResult` (unidades / divisas) | `EnableConverter` |

Si el toggle correspondiente está desactivado, `Search()` devuelve `[]` para ese resultado concreto (aunque el engine ya lo haya evaluado).

### Engine lifecycle

`MathJsEngine` y `NerdamerEngine` se inicializan siempre en background al arrancar, independientemente de los toggles. Los toggles solo filtran la salida de `CalculatorSearch.Search()`.

### Refresco automático

`EnableConverter` dispara `SearchSettingsChanged` igual que `EnableCalculator`, para refrescar los resultados en caliente cuando se cambia el toggle.

---

## Arquitectura — Acción de clic (delegate pattern)

### SettingsWindowViewModel

```csharp
// Delegate inyectado por App.OpenSettings() tras crear el ViewModel.
public Action<string>? OpenWithQuery { get; set; }

// Comando vinculado a cada fila de ejemplo en el AXAML.
[RelayCommand]
private void TryExample(string query) => OpenWithQuery?.Invoke(query);
```

### App.axaml.cs — OpenSettings()

```csharp
_settingsVm.OpenWithQuery = query =>
    Dispatcher.UIThread.InvokeAsync(() => {
        _services.GetRequiredService<MainWindowViewModel>().SearchText = query;
        var mw = (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (mw != null) AppHandler.Instance.ShowWindow(mw);
    });
```

### AXAML — cada fila de ejemplo

```xml
<Button Command="{Binding TryExampleCommand}"
        CommandParameter="2x - 5 = 2"
        Classes="example-row">
    <Grid ColumnDefinitions="Auto,*,Auto">
        <TextBlock Grid.Column="0" Text="▶" Classes="example-play"/>
        <TextBlock Grid.Column="1" Text="2x - 5 = 2" Classes="example-query"/>
        <TextBlock Grid.Column="2" Text="→ x = 3.5"  Classes="example-result"/>
    </Grid>
</Button>
```

---

## Archivos afectados

| Archivo | Cambio |
|---------|--------|
| `Yottacast.Core/Services/UserSettings.cs` | Añadir `EnableConverter` (bool, default true) |
| `Yottacast.Core/Search/Calculator/CalculatorSearch.cs` | Guard dual + filtro por tipo de resultado |
| `Yottacast/ViewModels/SettingsWindowViewModel.cs` | `SettingsSection.Converter`, `IsConverterSelected`, `TryExampleCommand`, `OpenWithQuery` |
| `Yottacast/Views/SettingsWindow.axaml` | Nav Converter, split de secciones, bloques de ejemplos |
| `Yottacast/App.axaml.cs` | Wiring del delegate `OpenWithQuery` |
| `docs/user-settings.md` | Añadir `EnableConverter` a la tabla de preferencias |
| `docs/search-calculator.md` | Actualizar sección de toggles |

> **Verificar en:** `CalculatorSearch.Search()`, `UserSettings.EnableConverter`, `SettingsWindowViewModel.TryExampleCommand`, `App.OpenSettings()`, `SettingsWindow.axaml` (secciones Calculator y Converter).
