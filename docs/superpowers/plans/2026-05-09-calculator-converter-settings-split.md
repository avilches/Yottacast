# Calculator / Converter Settings Split — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Separar la sección Calculator de Settings en dos (Calculator + Converter), cada una con su propio toggle y ejemplos clickables que abren la ventana principal y rellenan el buscador.

**Architecture:** Se añade `EnableConverter` a `UserSettings`; `CalculatorSearch` usa ambos toggles para filtrar por tipo de resultado; `SettingsWindowViewModel` añade sección Converter y un delegate `OpenWithQuery` inyectado por `App.OpenSettings()`; el AXAML divide las secciones y añade filas de ejemplo clickables.

**Tech Stack:** Avalonia 11 + CommunityToolkit.Mvvm 8, .NET 9, xUnit, SettingsWindow hardcoded theme

---

## File Map

| Archivo | Acción |
|---------|--------|
| `Yottacast.Core/Services/UserSettings.cs` | Añadir `EnableConverter` (bool, default true) |
| `Yottacast.Core/Search/Calculator/CalculatorSearch.cs` | Dual guard + filtro por tipo de resultado |
| `Yottacast/ViewModels/SettingsWindowViewModel.cs` | Sección Converter, `TryExampleCommand`, `OpenWithQuery` |
| `Yottacast/Views/SettingsWindow.axaml` | Nav Converter, split secciones, estilo example-row, filas de ejemplo |
| `Yottacast/App.axaml.cs` | Wiring del delegate `OpenWithQuery` |
| `Yottacast.Core.Tests/Services/UserSettingsTests.cs` | Test default EnableConverter |
| `Yottacast.Core.Tests/Search/Calculator/CalculatorSearchTests.cs` | Tests routing EnableConverter/EnableCalculator |
| `docs/user-settings.md` | Añadir EnableConverter a la tabla de preferencias |
| `docs/search-calculator.md` | Actualizar sección de toggles |

---

## Task 1: Añadir `EnableConverter` a UserSettings

**Files:**
- Modify: `Yottacast.Core/Services/UserSettings.cs`
- Test: `Yottacast.Core.Tests/Services/UserSettingsTests.cs`

- [ ] **Escribir el test que falla**

En `UserSettingsTests.cs`, añadir un test al final de la clase:

```csharp
[Fact]
public void Defaults_EnableConverter_IsTrue() {
    var settings = Load();
    Assert.True(settings.EnableConverter);
}

[Fact]
public void EnableConverter_RoundTrips_ThroughJson() {
    var settings = Load();
    settings.EnableConverter = false;
    settings.Save();
    var loaded = Load();
    Assert.False(loaded.EnableConverter);
}
```

- [ ] **Correr para verificar que falla**

```bash
cd Yottacast.Core.Tests && dotnet test --filter "Defaults_EnableConverter_IsTrue" 2>&1 | tail -5
```

Esperado: error de compilación (`EnableConverter` no existe).

- [ ] **Añadir `EnableConverter` a UserSettings**

En `Yottacast.Core/Services/UserSettings.cs`:

**En la clase `UserSettings`** (junto a `EnableCalculator`, aprox. línea 35):
```csharp
public bool EnableConverter { get; set; } = true;
```

**En `UserSettingsData`** (junto a `EnableCalculator`, aprox. línea 152):
```csharp
[JsonPropertyName("enableConverter")] public bool EnableConverter { get; init; } = true;
```

**En el método `Load()` / bloque de asignación** (junto a `EnableCalculator`):
```csharp
EnableConverter = data.EnableConverter,
```

**En `ToData()` / bloque de serialización** (junto a `EnableCalculator`):
```csharp
EnableConverter = EnableConverter,
```

- [ ] **Correr tests para verificar que pasan**

```bash
cd Yottacast.Core.Tests && dotnet test --filter "EnableConverter" 2>&1 | tail -5
```

Esperado: `Passed! - Failed: 0, Passed: 2`

- [ ] **Commit**

```bash
git add Yottacast.Core/Services/UserSettings.cs Yottacast.Core.Tests/Services/UserSettingsTests.cs
git commit -m "feat: add EnableConverter setting (default true)"
```

---

## Task 2: Routing por tipo de resultado en CalculatorSearch

**Files:**
- Modify: `Yottacast.Core/Search/Calculator/CalculatorSearch.cs`
- Test: `Yottacast.Core.Tests/Search/Calculator/CalculatorSearchTests.cs`

- [ ] **Escribir los tests que fallan**

En `CalculatorSearchTests.cs`, añadir una clase de tests al final del archivo (misma colección):

```csharp
[Collection("MathJs")]
public class CalculatorSearchEnableConverterTests(MathJsEngineFixture fixture, NerdamerEngineFixture nerdamerFixture) : IClassFixture<NerdamerEngineFixture> {

    private CalculatorSearch Build(bool enableCalculator, bool enableConverter) {
        var clipboard = new ClipboardService(NullLogger<ClipboardService>.Instance);
        clipboard.Initialize(copy: _ => { }, read: () => Task.FromResult<string?>(null));
        var settings = UserSettings.Load(new FakePlatformProvider([]));
        settings.EnableCalculator = enableCalculator;
        settings.EnableConverter  = enableConverter;
        var provider = MathJsEngineProvider.ForTesting(fixture.Engine);
        var er = new ExchangeRateService(new HttpClient(), settings, NullLogger<ExchangeRateService>.Instance);
        return new CalculatorSearch(provider, er, clipboard, settings, NullLogger<CalculatorSearch>.Instance, nerdamerFixture.Engine);
    }

    [Fact]
    public void BothDisabled_ReturnsEmpty_ForArithmetic() {
        var search = Build(enableCalculator: false, enableConverter: false);
        Assert.Empty(search.Search("2+2", 5));
    }

    [Fact]
    public void BothDisabled_ReturnsEmpty_ForConversion() {
        var search = Build(enableCalculator: false, enableConverter: false);
        Assert.Empty(search.Search("10 km to miles", 5));
    }

    [Fact]
    public void CalculatorDisabled_ConversionStillWorks() {
        var search = Build(enableCalculator: false, enableConverter: true);
        var results = search.Search("10 km to miles", 5);
        Assert.Single(results);
        Assert.IsType<ConversionResultItemViewModel>(results[0]);
    }

    [Fact]
    public void CalculatorDisabled_ArithmeticReturnsEmpty() {
        var search = Build(enableCalculator: false, enableConverter: true);
        Assert.Empty(search.Search("2+2", 5));
    }

    [Fact]
    public void ConverterDisabled_ArithmeticStillWorks() {
        var search = Build(enableCalculator: true, enableConverter: false);
        var results = search.Search("2+2", 5);
        Assert.Single(results);
        Assert.IsType<CalculatorResultItemViewModel>(results[0]);
    }

    [Fact]
    public void ConverterDisabled_ConversionReturnsEmpty() {
        var search = Build(enableCalculator: true, enableConverter: false);
        Assert.Empty(search.Search("10 km to miles", 5));
    }

    [Fact]
    public void CalculatorDisabled_EquationReturnsEmpty() {
        var search = Build(enableCalculator: false, enableConverter: true);
        Assert.Empty(search.Search("2x-5=2", 5));
    }

    [Fact]
    public void CalculatorEnabled_EquationWorks() {
        var search = Build(enableCalculator: true, enableConverter: false);
        var results = search.Search("2x-5=2", 5);
        Assert.Single(results);
        Assert.IsType<CalculatorResultItemViewModel>(results[0]);
    }
}
```

- [ ] **Correr para verificar que fallan**

```bash
cd Yottacast.Core.Tests && dotnet test --filter "CalculatorSearchEnableConverterTests" 2>&1 | tail -10
```

Esperado: errores de compilación o todos los tests fallan (EnableConverter no existe en UserSettings hasta Task 1, o los guards no existen aún).

- [ ] **Modificar `CalculatorSearch.Search()` con los nuevos guards**

En `Yottacast.Core/Search/Calculator/CalculatorSearch.cs`, modificar el método `Search()`:

**Cambiar la guard de entrada** (reemplazar `if (!settings.EnableCalculator) return [];`):
```csharp
if (!settings.EnableCalculator && !settings.EnableConverter) return [];
```

**En el bloque de detección de ecuaciones**, añadir guard antes de llamar a nerdamer:
```csharp
if (q.Contains('=')) {
    if (!settings.EnableCalculator) return [];
    var solveResult = nerdamerEngine.TrySolve(q);
    if (solveResult != null) return BuildEquationResult(solveResult, q);
    return [];
}
```

**En el `switch` sobre `engine.Evaluate(q)`**, añadir guards al inicio de cada case:

`case ConversionResult r:` — primera línea del bloque:
```csharp
case ConversionResult r: {
    if (!settings.EnableConverter) return [];
    // ... resto sin cambios
```

`case CalcResult r when r.RawValue != q:` — primera línea del bloque:
```csharp
case CalcResult r when r.RawValue != q: {
    if (!settings.EnableCalculator) return [];
    // ... resto sin cambios
```

- [ ] **Correr los tests para verificar que pasan**

```bash
cd Yottacast.Core.Tests && dotnet test --filter "CalculatorSearchEnableConverterTests" 2>&1 | tail -5
```

Esperado: `Passed! - Failed: 0, Passed: 8`

- [ ] **Correr la suite completa para verificar que no hay regresiones**

```bash
cd Yottacast.Core.Tests && dotnet test 2>&1 | tail -5
```

Esperado: `Passed! - Failed: 0`

- [ ] **Commit**

```bash
git add Yottacast.Core/Search/Calculator/CalculatorSearch.cs Yottacast.Core.Tests/Search/Calculator/CalculatorSearchTests.cs
git commit -m "feat: add EnableConverter routing guards to CalculatorSearch"
```

---

## Task 3: Añadir sección Converter + TryExampleCommand + OpenWithQuery a SettingsWindowViewModel

**Files:**
- Modify: `Yottacast/ViewModels/SettingsWindowViewModel.cs`

- [ ] **Añadir `Converter` al enum `SettingsSection`** (aprox. línea 23)

```csharp
public enum SettingsSection {
    General, AppSearch, WebSearch, FileSearch, Calculator, Converter, Clipboard, Emoji, Dictionary, DateSearch, History
}
```

- [ ] **Añadir `[NotifyPropertyChangedFor(nameof(IsConverterSelected))]`** al campo `_selectedSection` (junto a los otros `[NotifyPropertyChangedFor]`, aprox. líneas 29-38):

```csharp
[NotifyPropertyChangedFor(nameof(IsConverterSelected))]
```

- [ ] **Añadir la propiedad computada y el comando de selección** (junto a `IsCalculatorSelected` y `SelectCalculatorCommand`, aprox. líneas 51 y 63):

```csharp
public bool IsConverterSelected  => SelectedSection == SettingsSection.Converter;
```

```csharp
[RelayCommand] private void SelectConverter() => SelectedSection = SettingsSection.Converter;
```

- [ ] **Añadir `EnableConverter` observable y su handler** (junto a `OnEnableCalculatorChanged`, aprox. línea 128)

Buscar el bloque de `[ObservableProperty] private bool _enableCalculator` (o `_enableClipboard`) y añadir junto a él:

```csharp
[ObservableProperty] private bool _enableConverter;
```

Y el handler:
```csharp
partial void OnEnableConverterChanged(bool value) { _settings.EnableConverter = value; _settings.Save(); _logger.LogInformation("Settings: EnableConverter = {Value}", value); _settings.NotifySearchSettingsChanged(); }
```

- [ ] **Inicializar `_enableConverter` en el constructor** (junto a `_enableCalculator`, aprox. línea 315):

```csharp
_enableConverter = settings.EnableConverter,
```

- [ ] **Añadir `OpenWithQuery` y `TryExampleCommand`** (al final de la sección de navegación o al inicio de la sección Calculator, antes del constructor):

```csharp
// ── Example try-it action ──────────────────────────────────────────────────
/// <summary>
/// Wired by App.OpenSettings() to show the main window and fill the search box.
/// </summary>
public Action<string>? OpenWithQuery { get; set; }

[RelayCommand]
private void TryExample(string query) => OpenWithQuery?.Invoke(query);
```

- [ ] **Verificar compilación**

```bash
cd Yottacast && dotnet build 2>&1 | tail -5
```

Esperado: `Build succeeded` (puede haber warnings, 0 errores).

- [ ] **Commit**

```bash
git add Yottacast/ViewModels/SettingsWindowViewModel.cs
git commit -m "feat: add Converter section and TryExampleCommand to SettingsWindowViewModel"
```

---

## Task 4: Actualizar SettingsWindow.axaml

**Files:**
- Modify: `Yottacast/Views/SettingsWindow.axaml`

Esta tarea tiene múltiples pasos de edición. Hacerlos en orden.

### 4a — Añadir icono Converter en Window.Resources

- [ ] **Añadir `Icon.Converter`** (después de `Icon.Calculator`, aprox. línea 24):

```xml
<StreamGeometry x:Key="Icon.Converter">M1 11.5a.5.5 0 0 0 .5.5h11.793l-3.147 3.146a.5.5 0 0 0 .708.708l4-4a.5.5 0 0 0 0-.708l-4-4a.5.5 0 0 0-.708.708L13.293 11H1.5a.5.5 0 0 0-.5.5zm14-7a.5.5 0 0 1-.5.5H2.707l3.147 3.146a.5.5 0 1 1-.708.708l-4-4a.5.5 0 0 1 0-.708l4-4a.5.5 0 1 1 .708.708L2.707 4H14.5a.5.5 0 0 1 .5.5z</StreamGeometry>
```

### 4b — Añadir estilo `example-row` en las Styles del Window

- [ ] **Añadir estilo para los botones de ejemplo** (al final del bloque de Styles, antes del cierre de `<Window.Styles>`):

```xml
<!-- Example row button (try-it examples in Calculator/Converter sections) -->
<Style Selector="Button.example-row">
    <Setter Property="Background"             Value="Transparent"/>
    <Setter Property="BorderThickness"        Value="0"/>
    <Setter Property="Padding"               Value="8,5"/>
    <Setter Property="HorizontalAlignment"   Value="Stretch"/>
    <Setter Property="HorizontalContentAlignment" Value="Stretch"/>
    <Setter Property="CornerRadius"          Value="6"/>
    <Setter Property="Cursor"               Value="Hand"/>
</Style>
<Style Selector="Button.example-row:pointerover /template/ ContentPresenter">
    <Setter Property="Background" Value="{DynamicResource Theme.Results.Selection.Background}"/>
</Style>
```

### 4c — Reemplazar la sección Calculator actual por la versión dividida

La sección Calculator actual (aprox. líneas 1016–1095) incluye todo: toggle, decimal places Y todo lo de divisas. Hay que reemplazarla por dos secciones separadas.

- [ ] **Reemplazar el bloque Calculator+Currency actual por Calculator-only + nuevo bloque Converter**

Buscar y reemplazar el bloque completo entre `<!-- Calculator -->` y su cierre `</StackPanel>` (aprox. líneas 1016–1095) por el siguiente contenido:

```xml
                <!-- Calculator -->
                <StackPanel Spacing="16" IsVisible="{Binding IsCalculatorSelected}">
                    <TextBlock Classes="section-heading" Text="Calculator"/>
                    <ToggleSwitch IsChecked="{Binding EnableCalculator}"
                                  OnContent="Enabled"
                                  OffContent="Disabled"/>
                    <TextBlock Classes="description"
                               Text="Evaluate arithmetic expressions and solve equations directly in the search bar."/>

                    <StackPanel Spacing="16" IsVisible="{Binding EnableCalculator}">

                    <!-- Decimal places -->
                    <StackPanel Spacing="6">
                        <TextBlock Classes="label" Text="Result Decimal Places"/>
                        <Border Classes="numeric-field"
                                Width="130"
                                HorizontalAlignment="Left"
                                ClipToBounds="True">
                            <NumericUpDown x:Name="DecimalPlacesInput"
                                           Value="{Binding CalculatorDecimalPlaces}"
                                           Minimum="0"
                                           Maximum="6"
                                           Increment="1"
                                           FormatString="0"/>
                        </Border>
                    </StackPanel>

                    <!-- Examples -->
                    <StackPanel Spacing="4">
                        <TextBlock Classes="label" Text="Examples — click to try"/>
                        <Button Classes="example-row"
                                Command="{Binding TryExampleCommand}"
                                CommandParameter="2 + 3 * 4">
                            <Grid ColumnDefinitions="16,*,Auto">
                                <TextBlock Grid.Column="0" Text="▶"
                                           Foreground="{DynamicResource Theme.Results.Category.Color}"
                                           FontSize="10" VerticalAlignment="Center"/>
                                <TextBlock Grid.Column="1" Text="2 + 3 * 4"
                                           FontFamily="Courier New, Cascadia Code, monospace"
                                           Foreground="{DynamicResource Theme.Results.Title.Color}"
                                           VerticalAlignment="Center" Margin="8,0,0,0"/>
                                <TextBlock Grid.Column="2" Text="→ 14"
                                           Foreground="{DynamicResource Theme.Results.Subtitle.Color}"
                                           FontSize="12" VerticalAlignment="Center"/>
                            </Grid>
                        </Button>
                        <Button Classes="example-row"
                                Command="{Binding TryExampleCommand}"
                                CommandParameter="sqrt(144)">
                            <Grid ColumnDefinitions="16,*,Auto">
                                <TextBlock Grid.Column="0" Text="▶"
                                           Foreground="{DynamicResource Theme.Results.Category.Color}"
                                           FontSize="10" VerticalAlignment="Center"/>
                                <TextBlock Grid.Column="1" Text="sqrt(144)"
                                           FontFamily="Courier New, Cascadia Code, monospace"
                                           Foreground="{DynamicResource Theme.Results.Title.Color}"
                                           VerticalAlignment="Center" Margin="8,0,0,0"/>
                                <TextBlock Grid.Column="2" Text="→ 12"
                                           Foreground="{DynamicResource Theme.Results.Subtitle.Color}"
                                           FontSize="12" VerticalAlignment="Center"/>
                            </Grid>
                        </Button>
                        <Button Classes="example-row"
                                Command="{Binding TryExampleCommand}"
                                CommandParameter="2x - 5 = 2">
                            <Grid ColumnDefinitions="16,*,Auto">
                                <TextBlock Grid.Column="0" Text="▶"
                                           Foreground="{DynamicResource Theme.Results.Category.Color}"
                                           FontSize="10" VerticalAlignment="Center"/>
                                <TextBlock Grid.Column="1" Text="2x - 5 = 2"
                                           FontFamily="Courier New, Cascadia Code, monospace"
                                           Foreground="{DynamicResource Theme.Results.Title.Color}"
                                           VerticalAlignment="Center" Margin="8,0,0,0"/>
                                <TextBlock Grid.Column="2" Text="→ x = 3.5"
                                           Foreground="{DynamicResource Theme.Results.Subtitle.Color}"
                                           FontSize="12" VerticalAlignment="Center"/>
                            </Grid>
                        </Button>
                        <Button Classes="example-row"
                                Command="{Binding TryExampleCommand}"
                                CommandParameter="x^2 - 5x + 6 = 0">
                            <Grid ColumnDefinitions="16,*,Auto">
                                <TextBlock Grid.Column="0" Text="▶"
                                           Foreground="{DynamicResource Theme.Results.Category.Color}"
                                           FontSize="10" VerticalAlignment="Center"/>
                                <TextBlock Grid.Column="1" Text="x^2 - 5x + 6 = 0"
                                           FontFamily="Courier New, Cascadia Code, monospace"
                                           Foreground="{DynamicResource Theme.Results.Title.Color}"
                                           VerticalAlignment="Center" Margin="8,0,0,0"/>
                                <TextBlock Grid.Column="2" Text="→ x = 2, 3"
                                           Foreground="{DynamicResource Theme.Results.Subtitle.Color}"
                                           FontSize="12" VerticalAlignment="Center"/>
                            </Grid>
                        </Button>
                    </StackPanel>

                    </StackPanel> <!-- /EnableCalculator -->
                </StackPanel>

                <!-- Converter -->
                <StackPanel Spacing="16" IsVisible="{Binding IsConverterSelected}">
                    <TextBlock Classes="section-heading" Text="Converter"/>
                    <ToggleSwitch IsChecked="{Binding EnableConverter}"
                                  OnContent="Enabled"
                                  OffContent="Disabled"/>
                    <TextBlock Classes="description"
                               Text="Convert units and currencies directly in the search bar."/>

                    <StackPanel Spacing="16" IsVisible="{Binding EnableConverter}">

                    <!-- Currency pair -->
                    <StackPanel Spacing="6">
                        <TextBlock Classes="label" Text="Default Currency Pair"/>
                        <StackPanel Orientation="Horizontal" Spacing="8" VerticalAlignment="Center">
                            <TextBox Text="{Binding CalculatorCurrencyA}"
                                     Classes="flyout-input"
                                     Width="64"
                                     MaxLength="3"/>
                            <TextBlock Text="/"
                                       Foreground="{DynamicResource Theme.Results.Subtitle.Color}"
                                       VerticalAlignment="Center"
                                       FontSize="16"/>
                            <TextBox Text="{Binding CalculatorCurrencyB}"
                                     Classes="flyout-input"
                                     Width="64"
                                     MaxLength="3"/>
                        </StackPanel>
                        <TextBlock Classes="description"
                                   Text="When you type an amount in any currency, it converts to the left currency. The left currency converts to the right."/>
                    </StackPanel>

                    <!-- Exchange rates -->
                    <StackPanel Spacing="6">
                        <TextBlock Classes="label" Text="Exchange Rates"/>
                        <CheckBox Content="Include metals (gold, silver, platinum, palladium)"
                                  IsChecked="{Binding CalculatorIncludeMetals}"/>
                        <CheckBox Content="Include cryptocurrencies"
                                  IsChecked="{Binding CalculatorIncludeCrypto}"/>
                    </StackPanel>

                    <StackPanel Spacing="6">
                        <TextBlock Classes="label" Text="Refresh Interval"/>
                        <Border Classes="numeric-field"
                                Width="130"
                                HorizontalAlignment="Left"
                                ClipToBounds="True">
                            <NumericUpDown x:Name="ExchangeRateRefreshInput"
                                           Value="{Binding ExchangeRateRefreshIntervalHours}"
                                           Minimum="1"
                                           Maximum="168"
                                           Increment="1"
                                           FormatString="0"/>
                        </Border>
                        <TextBlock Classes="description" Text="How often to check for updated exchange rates (hours)."/>
                    </StackPanel>

                    <StackPanel Spacing="4">
                        <TextBlock Classes="label" Text="Last Rate Update"/>
                        <TextBlock Classes="description" Text="{Binding ExchangeRatesLastUpdatedText}"/>
                    </StackPanel>

                    <!-- Examples -->
                    <StackPanel Spacing="4">
                        <TextBlock Classes="label" Text="Examples — click to try"/>
                        <Button Classes="example-row"
                                Command="{Binding TryExampleCommand}"
                                CommandParameter="10 km to miles">
                            <Grid ColumnDefinitions="16,*,Auto">
                                <TextBlock Grid.Column="0" Text="▶"
                                           Foreground="{DynamicResource Theme.Results.Category.Color}"
                                           FontSize="10" VerticalAlignment="Center"/>
                                <TextBlock Grid.Column="1" Text="10 km to miles"
                                           FontFamily="Courier New, Cascadia Code, monospace"
                                           Foreground="{DynamicResource Theme.Results.Title.Color}"
                                           VerticalAlignment="Center" Margin="8,0,0,0"/>
                                <TextBlock Grid.Column="2" Text="→ 6.21 mi"
                                           Foreground="{DynamicResource Theme.Results.Subtitle.Color}"
                                           FontSize="12" VerticalAlignment="Center"/>
                            </Grid>
                        </Button>
                        <Button Classes="example-row"
                                Command="{Binding TryExampleCommand}"
                                CommandParameter="100 F to C">
                            <Grid ColumnDefinitions="16,*,Auto">
                                <TextBlock Grid.Column="0" Text="▶"
                                           Foreground="{DynamicResource Theme.Results.Category.Color}"
                                           FontSize="10" VerticalAlignment="Center"/>
                                <TextBlock Grid.Column="1" Text="100 F to C"
                                           FontFamily="Courier New, Cascadia Code, monospace"
                                           Foreground="{DynamicResource Theme.Results.Title.Color}"
                                           VerticalAlignment="Center" Margin="8,0,0,0"/>
                                <TextBlock Grid.Column="2" Text="→ 37.78 °C"
                                           Foreground="{DynamicResource Theme.Results.Subtitle.Color}"
                                           FontSize="12" VerticalAlignment="Center"/>
                            </Grid>
                        </Button>
                        <Button Classes="example-row"
                                Command="{Binding TryExampleCommand}"
                                CommandParameter="60 km/h to mph">
                            <Grid ColumnDefinitions="16,*,Auto">
                                <TextBlock Grid.Column="0" Text="▶"
                                           Foreground="{DynamicResource Theme.Results.Category.Color}"
                                           FontSize="10" VerticalAlignment="Center"/>
                                <TextBlock Grid.Column="1" Text="60 km/h to mph"
                                           FontFamily="Courier New, Cascadia Code, monospace"
                                           Foreground="{DynamicResource Theme.Results.Title.Color}"
                                           VerticalAlignment="Center" Margin="8,0,0,0"/>
                                <TextBlock Grid.Column="2" Text="→ 37.28 mph"
                                           Foreground="{DynamicResource Theme.Results.Subtitle.Color}"
                                           FontSize="12" VerticalAlignment="Center"/>
                            </Grid>
                        </Button>
                        <Button Classes="example-row"
                                Command="{Binding TryExampleCommand}"
                                CommandParameter="100 USD">
                            <Grid ColumnDefinitions="16,*,Auto">
                                <TextBlock Grid.Column="0" Text="▶"
                                           Foreground="{DynamicResource Theme.Results.Category.Color}"
                                           FontSize="10" VerticalAlignment="Center"/>
                                <TextBlock Grid.Column="1" Text="100 USD"
                                           FontFamily="Courier New, Cascadia Code, monospace"
                                           Foreground="{DynamicResource Theme.Results.Title.Color}"
                                           VerticalAlignment="Center" Margin="8,0,0,0"/>
                                <TextBlock Grid.Column="2" Text="→ EUR (live)"
                                           Foreground="{DynamicResource Theme.Results.Subtitle.Color}"
                                           FontSize="12" VerticalAlignment="Center"/>
                            </Grid>
                        </Button>
                        <Button Classes="example-row"
                                Command="{Binding TryExampleCommand}"
                                CommandParameter="500 EUR to GBP">
                            <Grid ColumnDefinitions="16,*,Auto">
                                <TextBlock Grid.Column="0" Text="▶"
                                           Foreground="{DynamicResource Theme.Results.Category.Color}"
                                           FontSize="10" VerticalAlignment="Center"/>
                                <TextBlock Grid.Column="1" Text="500 EUR to GBP"
                                           FontFamily="Courier New, Cascadia Code, monospace"
                                           Foreground="{DynamicResource Theme.Results.Title.Color}"
                                           VerticalAlignment="Center" Margin="8,0,0,0"/>
                                <TextBlock Grid.Column="2" Text="→ GBP (live)"
                                           Foreground="{DynamicResource Theme.Results.Subtitle.Color}"
                                           FontSize="12" VerticalAlignment="Center"/>
                            </Grid>
                        </Button>
                    </StackPanel>

                    </StackPanel> <!-- /EnableConverter -->
                </StackPanel>
```

### 4d — Añadir nav button Converter en el sidebar

- [ ] **Insertar nav button Converter** justo después del botón Calculator (aprox. línea 472):

```xml
                    <Button Classes="nav-item"
                            Classes.nav-selected="{Binding IsConverterSelected}"
                            Command="{Binding SelectConverterCommand}">
                        <StackPanel Orientation="Horizontal" Spacing="8">
                            <PathIcon Data="{StaticResource Icon.Converter}" Width="14" Height="14" VerticalAlignment="Center"/>
                            <TextBlock Text="Converter" VerticalAlignment="Center"/>
                        </StackPanel>
                    </Button>
```

- [ ] **Verificar compilación**

```bash
cd Yottacast && dotnet build 2>&1 | tail -5
```

Esperado: `Build succeeded`.

- [ ] **Commit**

```bash
git add Yottacast/Views/SettingsWindow.axaml
git commit -m "feat: split Calculator/Converter settings sections with clickable examples"
```

---

## Task 5: Wiring del delegate OpenWithQuery en App.axaml.cs

**Files:**
- Modify: `Yottacast/App.axaml.cs`

- [ ] **Añadir el wiring del delegate justo después de crear `_settingsVm`**

En `App.OpenSettings()`, encontrar la línea:
```csharp
_settingsVm = _services.GetRequiredService<SettingsWindowViewModel>();
```

Añadir inmediatamente después:
```csharp
_settingsVm.OpenWithQuery = query =>
    Dispatcher.UIThread.InvokeAsync(() => {
        _services.GetRequiredService<MainWindowViewModel>().SearchText = query;
        var mw = (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (mw != null) AppHandler.Instance.ShowWindow(mw);
    });
```

- [ ] **Verificar compilación completa**

```bash
cd Yottacast && dotnet build 2>&1 | tail -5
```

Esperado: `Build succeeded`.

- [ ] **Correr suite completa de tests**

```bash
cd Yottacast.Core.Tests && dotnet test 2>&1 | tail -5
```

Esperado: `Passed! - Failed: 0`

- [ ] **Commit**

```bash
git add Yottacast/App.axaml.cs
git commit -m "feat: wire OpenWithQuery delegate in App.OpenSettings"
```

---

## Task 6: Actualizar documentación

**Files:**
- Modify: `docs/user-settings.md`
- Modify: `docs/search-calculator.md`

- [ ] **En `docs/user-settings.md`**, añadir `EnableConverter` a la tabla de preferencias (sección 2, junto a `EnableCalculator`):

```markdown
| EnableConverter | `true` | Toggle del conversor de unidades y divisas |
```

- [ ] **En `docs/search-calculator.md`**, actualizar la sección de toggles (sección 1.2 o donde se mencione `EnableCalculator`):

Añadir una nueva fila/párrafo que indique:
- `EnableCalculator` controla aritmética y ecuaciones
- `EnableConverter` controla conversiones de unidades y divisas
- El engine solo se omite si ambos están desactivados

- [ ] **Commit**

```bash
git add docs/user-settings.md docs/search-calculator.md
git commit -m "docs: update user-settings and search-calculator for EnableConverter"
```

---

## Notas de implementación

### Sobre el `x:Name="DecimalPlacesInput"` y `x:Name="ExchangeRateRefreshInput"`

Estos nombres son referenciados en `SettingsWindow.axaml.cs` (code-behind) para bloquear la entrada de texto no numérico:
```csharp
DecimalPlacesInput.AddHandler(...)
ExchangeRateRefreshInput.AddHandler(...)
```
Al mover `ExchangeRateRefreshInput` a la sección Converter, el code-behind sigue funcionando porque el nombre `x:Name` sigue siendo el mismo — solo cambia la sección en la que vive el control. No hay cambios necesarios en el code-behind.

### Sobre los bindings `CalculatorCurrencyA`, `CalculatorCurrencyB`, `CalculatorIncludeMetals`, etc.

Estos bindings ya existen en `SettingsWindowViewModel` y siguen funcionando en la nueva sección Converter. Solo se mueven de panel — no se renombran ni se cambia su lógica.

### Sobre el tipo de fuente monospace en el AXAML

`FontFamily="Courier New, Cascadia Code, monospace"` usa las fuentes disponibles en el sistema en orden de prioridad. Avalonia acepta esta sintaxis de fallback. Si la plataforma no tiene ninguna, cae al fallback genérico `monospace`.
