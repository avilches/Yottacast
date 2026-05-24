# Calculator Settings Examples Redesign — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rediseñar Settings → Calculator: unificar los toggles Calculator/Converter en uno solo, reorganizar en subsecciones (Math & Algebra, Unit Converter, Currency Exchange), y cambiar el layout de ejemplos a tabla 3 columnas (expresión | descripción | Try…) con descripciones reactivas.

**Architecture:** Se elimina `EnableConverter` de UserSettings y CalculatorSearch; todo lo controla `EnableCalculator`. SettingsWindowViewModel pierde la sección Converter y gana dos propiedades computed reactivas para descripciones dinámicas de divisas. SettingsWindow.axaml recibe la sección Calculator ampliada con tres subsecciones y el nuevo layout de ejemplos.

**Tech Stack:** Avalonia 11.3.12, .NET 9, CommunityToolkit.Mvvm 8.2.1, xUnit.

---

## Ficheros afectados

| Fichero | Acción |
|---------|--------|
| `Yottacast.Core/Services/UserSettings.cs` | Eliminar `EnableConverter` del modelo mutable y de `Save()`; mantener en DTO para leer JSON legacy; migrar en `Load()` |
| `Yottacast.Core/Search/Calculator/CalculatorSearch.cs` | Eliminar checks de `settings.EnableConverter` |
| `Yottacast/ViewModels/SettingsWindowViewModel.cs` | Eliminar sección Converter; añadir `CurrencyExampleDesc` y `CryptoCurrencyExampleDesc` |
| `Yottacast/Views/SettingsWindow.axaml` | Eliminar nav item Converter; rediseñar sección Calculator con 3 subsecciones |
| `Yottacast.Core.Tests/Search/Calculator/CalculatorSearchTests.cs` | Reescribir `CalculatorSearchEnableConverterTests` |
| `Yottacast.Core.Tests/Services/UserSettingsTests.cs` | Eliminar tests de `EnableConverter`; añadir test de migración |
| `docs/search-calculator.md` | Actualizar descripción de flags |
| `docs/user-settings.md` | Eliminar referencia a `EnableConverter` |

---

## Task 1: Crear worktree

- [ ] **Crear worktree en la rama `feat/calculator-settings-redesign`**

```bash
cd "/Users/avilches/Library/Mobile Documents/com~apple~CloudDocs/Shared/Proy/Yottacast"
git worktree add ".claude/worktrees/feat/calculator-settings-redesign" -b "feat/calculator-settings-redesign"
```

- [ ] **Verificar baseline (mismo fallo preexistente de terminal discovery)**

```bash
cd "/Users/avilches/Library/Mobile Documents/com~apple~CloudDocs/Shared/Proy/Yottacast/.claude/worktrees/feat/calculator-settings-redesign"
dotnet restore Yottacast.Core.Tests/ -v quiet && dotnet test Yottacast.Core.Tests/ -v quiet 2>&1 | tail -5
```

Expected: `Failed: 1, Passed: 122X` — solo falla `Discover_Terminal_NoneInstalled_ReturnsEmpty`.

---

## Task 2: Eliminar EnableConverter de UserSettings y CalculatorSearch

**Files:**
- Modify: `Yottacast.Core/Services/UserSettings.cs`
- Modify: `Yottacast.Core/Search/Calculator/CalculatorSearch.cs`
- Modify: `Yottacast.Core.Tests/Search/Calculator/CalculatorSearchTests.cs`
- Modify: `Yottacast.Core.Tests/Services/UserSettingsTests.cs`

### 2a. Tests primero (TDD)

- [ ] **Reemplazar la clase `CalculatorSearchEnableConverterTests` entera en `CalculatorSearchTests.cs` (empieza en línea 533)**

```csharp
[Collection("MathJs")]
public class CalculatorSearchEnableCalculatorTests(MathJsEngineFixture fixture, NerdamerEngineFixture nerdamerFixture) : IClassFixture<NerdamerEngineFixture> {

    private CalculatorSearch Build(bool enableCalculator) {
        var clipboard = new ClipboardService(NullLogger<ClipboardService>.Instance);
        clipboard.Initialize(copy: _ => { }, read: () => Task.FromResult<string?>(null));
        var settings = UserSettings.Load(new FakePlatformProvider([]));
        settings.EnableCalculator = enableCalculator;
        var provider = MathJsEngineProvider.ForTesting(fixture.Engine);
        var er = new ExchangeRateService(new HttpClient(), settings, NullLogger<ExchangeRateService>.Instance);
        return new CalculatorSearch(provider, er, clipboard, settings, NullLogger<CalculatorSearch>.Instance, nerdamerFixture.Engine);
    }

    [Fact] public void Disabled_ReturnsEmpty_ForArithmetic() {
        Assert.Empty(Build(false).Search("2+2", 5));
    }
    [Fact] public void Disabled_ReturnsEmpty_ForConversion() {
        Assert.Empty(Build(false).Search("10 km to miles", 5));
    }
    [Fact] public void Disabled_ReturnsEmpty_ForEquation() {
        Assert.Empty(Build(false).Search("2x-5=2", 5));
    }
    [Fact] public void Enabled_ArithmeticWorks() {
        var r = Build(true).Search("2+2", 5);
        Assert.Single(r); Assert.IsType<CalculatorResultItemViewModel>(r[0]);
    }
    [Fact] public void Enabled_ConversionWorks() {
        var r = Build(true).Search("10 km to miles", 5);
        Assert.Single(r); Assert.IsType<ConversionResultItemViewModel>(r[0]);
    }
    [Fact] public void Enabled_EquationWorks() {
        var r = Build(true).Search("2x-5=2", 5);
        Assert.Single(r); Assert.IsType<CalculatorResultItemViewModel>(r[0]);
    }
}
```

- [ ] **En `UserSettingsTests.cs` eliminar los tests `Defaults_EnableConverter_IsTrue` y `EnableConverter_RoundTrips_ThroughJson`**

Buscar y eliminar (las dos funciones con `EnableConverter` en su nombre, alrededor de línea 1193–1204).

- [ ] **En `UserSettingsTests.cs` añadir test de migración al final de la clase de tests de UserSettings:**

Primero comprueba la firma de `UserSettings.Load()`:
```bash
grep -n "public static.*Load" "/Users/avilches/Library/Mobile Documents/com~apple~CloudDocs/Shared/Proy/Yottacast/.claude/worktrees/feat/calculator-settings-redesign/Yottacast.Core/Services/UserSettings.cs" | head -5
```

Si `Load()` acepta un path opcional o hay una sobrecarga, añadir este test. Si no existe sobrecarga con path, usar el método interno de deserialización de JSON. El patrón correcto es copiar cómo los tests existentes prueban el round-trip (busca `RoundTrips_ThroughJson` en el mismo archivo).

```csharp
[Fact]
public void Load_MigratesEnableConverter_ToEnableCalculator() {
    // Simula un JSON legacy donde enableConverter=true pero enableCalculator=false
    // Usa el mismo patrón que otros tests de round-trip en este archivo
    var settings = UserSettings.Load(new FakePlatformProvider([]));
    settings.EnableCalculator = false;
    var json = settings.ToJsonForTest();          // ver patrón exacto en otros tests
    var injected = json.Replace("\"enableCalculator\":false", "\"enableCalculator\":false,\"enableConverter\":true");
    var loaded = UserSettings.LoadFromJson(injected, new FakePlatformProvider([])); // ver firma exacta
    Assert.True(loaded.EnableCalculator);
}
```

> **IMPORTANTE:** Antes de escribir este test, leer cómo los tests existentes hacen round-trip (método `ToJson`/`Save`/`Load`). Busca en el mismo archivo: `EnableCalculator_RoundTrips_ThroughJson` o similar para copiar el patrón exacto.

- [ ] **Ejecutar para verificar que los tests FALLAN (código aún no actualizado):**

```bash
dotnet build Yottacast.Core.Tests/ --no-restore -v quiet 2>&1 | tail -5
```

Expected: errores de compilación por `EnableConverter` eliminado del ViewModel pero no del Core aún.

### 2b. Actualizar UserSettings.cs

- [ ] **Eliminar la propiedad pública `EnableConverter` (línea 36):**

```csharp
// ELIMINAR:
public bool EnableConverter { get; set; } = true;
```

- [ ] **En `UserSettingsData` (DTO, línea ~154): cambiar default de `EnableConverter` a `false`:**

```csharp
// ANTES:
[JsonPropertyName("enableConverter")] public bool EnableConverter { get; init; } = true;
// DESPUÉS:
[JsonPropertyName("enableConverter")] public bool EnableConverter { get; init; } = false;
```

- [ ] **En `Load()` (línea ~228): reemplazar las dos líneas `EnableCalculator/EnableConverter` con la migración:**

```csharp
// ANTES:
EnableCalculator = data.EnableCalculator,
EnableConverter = data.EnableConverter,

// DESPUÉS:
EnableCalculator = data.EnableCalculator || data.EnableConverter,
```

- [ ] **En `Save()` (línea ~343): eliminar `EnableConverter = EnableConverter,`**

### 2c. Actualizar CalculatorSearch.cs

- [ ] **Línea 28: reemplazar el check combinado:**

```csharp
// ANTES:
if (!settings.EnableCalculator && !settings.EnableConverter) return [];
// DESPUÉS:
if (!settings.EnableCalculator) return [];
```

- [ ] **Dentro del `case ConversionResult r:` (línea 53): eliminar el guard:**

```csharp
// ELIMINAR:
if (!settings.EnableConverter) return [];
```

- [ ] **Verificar que no quedan más usos:**

```bash
grep -rn "EnableConverter" ".claude/worktrees/feat/calculator-settings-redesign/" --include="*.cs" | grep -v "obj/" | grep -v "UserSettings.cs"
```

Expected: 0 resultados.

### 2d. Compilar y tests

- [ ] **Compilar y ejecutar:**

```bash
dotnet test Yottacast.Core.Tests/ -v quiet 2>&1 | tail -5
```

Expected: mismo baseline que Task 1.

- [ ] **Commit:**

```bash
git add Yottacast.Core/Services/UserSettings.cs Yottacast.Core/Search/Calculator/CalculatorSearch.cs
git add Yottacast.Core.Tests/Search/Calculator/CalculatorSearchTests.cs Yottacast.Core.Tests/Services/UserSettingsTests.cs
git commit -m "refactor: unify EnableConverter into EnableCalculator"
```

---

## Task 3: Actualizar SettingsWindowViewModel

**Files:**
- Modify: `Yottacast/ViewModels/SettingsWindowViewModel.cs`

- [ ] **Eliminar `Converter` del enum `SettingsSection` (línea 23):**

```csharp
// ANTES:
public enum SettingsSection {
    General, AppSearch, WebSearch, FileSearch, Calculator, Converter, Clipboard, Emoji, Dictionary, DateSearch, History
}
// DESPUÉS:
public enum SettingsSection {
    General, AppSearch, WebSearch, FileSearch, Calculator, Clipboard, Emoji, Dictionary, DateSearch, History
}
```

- [ ] **Eliminar `[NotifyPropertyChangedFor(nameof(IsConverterSelected))]` del campo `_selectedSection` (~línea 34)**

- [ ] **Eliminar `public bool IsConverterSelected => ...` (~línea 53)**

- [ ] **Eliminar `[RelayCommand] private void SelectConverter()...` (~línea 66)**

- [ ] **Eliminar `[ObservableProperty] private bool _enableConverter;` (~línea 106)**

- [ ] **Eliminar `partial void OnEnableConverterChanged(bool value) { ... }` (~línea 142)**

- [ ] **Eliminar `_enableConverter = settings.EnableConverter;` en el constructor (~línea 331)**

- [ ] **Añadir `[NotifyPropertyChangedFor]` al campo `_calculatorCurrencyA` y las dos propiedades computed (~línea 224):**

```csharp
// ANTES:
[ObservableProperty] private string _calculatorCurrencyA = "EUR";

// DESPUÉS:
[ObservableProperty]
[NotifyPropertyChangedFor(nameof(CurrencyExampleDesc))]
[NotifyPropertyChangedFor(nameof(CryptoCurrencyExampleDesc))]
private string _calculatorCurrencyA = "EUR";

public string CurrencyExampleDesc       => $"Convert to {CalculatorCurrencyA}";
public string CryptoCurrencyExampleDesc => $"Bitcoin in {CalculatorCurrencyA}";
```

- [ ] **Compilar:**

```bash
dotnet build Yottacast/Yottacast.csproj --no-restore -v quiet 2>&1 | tail -5
```

Expected: `0 Error(s)`.

- [ ] **Commit:**

```bash
git add Yottacast/ViewModels/SettingsWindowViewModel.cs
git commit -m "refactor: eliminar sección Converter del ViewModel, añadir CurrencyExampleDesc"
```

---

## Task 4: Rediseñar SettingsWindow.axaml

**Files:**
- Modify: `Yottacast/Views/SettingsWindow.axaml`

### 4a. Añadir estilos nuevos (después del estilo `Button.example-row:pointerover` ~línea 418)

- [ ] **Insertar los 4 estilos nuevos:**

```xml
        <!-- Example row: expression column (monospace) -->
        <Style Selector="TextBlock.expr-mono">
            <Setter Property="FontFamily" Value="Courier New, Cascadia Code, Consolas, monospace"/>
            <Setter Property="Foreground"  Value="{DynamicResource Theme.Results.Title.Color}"/>
            <Setter Property="FontSize"    Value="{DynamicResource Theme.Results.Title.Size}"/>
            <Setter Property="VerticalAlignment" Value="Center"/>
        </Style>
        <!-- Example row: description column -->
        <Style Selector="TextBlock.expr-desc">
            <Setter Property="Foreground"    Value="{DynamicResource Theme.Results.Subtitle.Color}"/>
            <Setter Property="FontSize"      Value="10"/>
            <Setter Property="VerticalAlignment" Value="Center"/>
            <Setter Property="TextWrapping"  Value="Wrap"/>
        </Style>
        <!-- Example row: Try… label -->
        <Style Selector="TextBlock.try-label">
            <Setter Property="Foreground"    Value="{DynamicResource Theme.Results.Subtitle.Color}"/>
            <Setter Property="FontSize"      Value="10"/>
            <Setter Property="VerticalAlignment" Value="Center"/>
            <Setter Property="Opacity"       Value="0.6"/>
        </Style>
        <!-- Subsection heading within a settings section -->
        <Style Selector="TextBlock.subsection-heading">
            <Setter Property="Foreground"  Value="{DynamicResource Theme.Results.Title.Color}"/>
            <Setter Property="FontSize"    Value="{DynamicResource Theme.Results.Title.Size}"/>
            <Setter Property="FontWeight"  Value="SemiBold"/>
            <Setter Property="Margin"      Value="0,8,0,0"/>
        </Style>
```

### 4b. Eliminar nav item Converter del sidebar (~líneas 489–496)

- [ ] **Eliminar el bloque completo:**

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

### 4c. Reemplazar sección Calculator completa (~líneas 1040–1137)

- [ ] **Reemplazar desde `<!-- Calculator -->` hasta el `</StackPanel>` antes de `<!-- Converter -->`:**

```xml
                <!-- Calculator -->
                <StackPanel Spacing="16" IsVisible="{Binding IsCalculatorSelected}">
                    <TextBlock Classes="section-heading" Text="Calculator"/>
                    <ToggleSwitch IsChecked="{Binding EnableCalculator}"
                                  OnContent="Enabled"
                                  OffContent="Disabled"/>
                    <TextBlock Classes="description"
                               Text="Evaluate math, convert units and currencies directly in the search bar."/>

                    <StackPanel Spacing="16" IsVisible="{Binding EnableCalculator}">

                    <!-- Decimal places -->
                    <StackPanel Spacing="6">
                        <TextBlock Classes="label" Text="Result Decimal Places"/>
                        <Border Classes="numeric-field" Width="130" HorizontalAlignment="Left" ClipToBounds="True">
                            <NumericUpDown x:Name="DecimalPlacesInput"
                                           Value="{Binding CalculatorDecimalPlaces}"
                                           Minimum="0" Maximum="6" Increment="1" FormatString="0"/>
                        </Border>
                    </StackPanel>

                    <!-- ── Math & Algebra ── -->
                    <TextBlock Classes="subsection-heading" Text="Math &amp; Algebra"/>
                    <StackPanel Spacing="0">
                        <TextBlock Classes="label" Text="Examples — click to try"/>
                        <Button Classes="example-row" Command="{Binding TryExampleCommand}" CommandParameter="2 + 3 * 4">
                            <Grid ColumnDefinitions="130,*,Auto">
                                <TextBlock Grid.Column="0" Classes="expr-mono" Text="2 + 3 * 4"/>
                                <TextBlock Grid.Column="1" Classes="expr-desc" Text="Operator precedence" Margin="8,0"/>
                                <TextBlock Grid.Column="2" Classes="try-label" Text="Try…"/>
                            </Grid>
                        </Button>
                        <Button Classes="example-row" Command="{Binding TryExampleCommand}" CommandParameter="sqrt(pi)">
                            <Grid ColumnDefinitions="130,*,Auto">
                                <TextBlock Grid.Column="0" Classes="expr-mono" Text="sqrt(pi)"/>
                                <TextBlock Grid.Column="1" Classes="expr-desc" Text="Square root of π" Margin="8,0"/>
                                <TextBlock Grid.Column="2" Classes="try-label" Text="Try…"/>
                            </Grid>
                        </Button>
                        <Button Classes="example-row" Command="{Binding TryExampleCommand}" CommandParameter="sin(45 deg)">
                            <Grid ColumnDefinitions="130,*,Auto">
                                <TextBlock Grid.Column="0" Classes="expr-mono" Text="sin(45 deg)"/>
                                <TextBlock Grid.Column="1" Classes="expr-desc" Text="Trigonometry in degrees" Margin="8,0"/>
                                <TextBlock Grid.Column="2" Classes="try-label" Text="Try…"/>
                            </Grid>
                        </Button>
                        <Button Classes="example-row" Command="{Binding TryExampleCommand}" CommandParameter="log(1000, 10)">
                            <Grid ColumnDefinitions="130,*,Auto">
                                <TextBlock Grid.Column="0" Classes="expr-mono" Text="log(1000, 10)"/>
                                <TextBlock Grid.Column="1" Classes="expr-desc" Text="Logarithm base 10" Margin="8,0"/>
                                <TextBlock Grid.Column="2" Classes="try-label" Text="Try…"/>
                            </Grid>
                        </Button>
                        <Button Classes="example-row" Command="{Binding TryExampleCommand}" CommandParameter="2x - 5 = 2">
                            <Grid ColumnDefinitions="130,*,Auto">
                                <TextBlock Grid.Column="0" Classes="expr-mono" Text="2x - 5 = 2"/>
                                <TextBlock Grid.Column="1" Classes="expr-desc" Text="Solve equation for x" Margin="8,0"/>
                                <TextBlock Grid.Column="2" Classes="try-label" Text="Try…"/>
                            </Grid>
                        </Button>
                        <Button Classes="example-row" Command="{Binding TryExampleCommand}" CommandParameter="x^2 - 5x + 6">
                            <Grid ColumnDefinitions="130,*,Auto">
                                <TextBlock Grid.Column="0" Classes="expr-mono" Text="x^2 - 5x + 6"/>
                                <TextBlock Grid.Column="1" Classes="expr-desc" Text="Factor / derive symbolic expression" Margin="8,0"/>
                                <TextBlock Grid.Column="2" Classes="try-label" Text="Try…"/>
                            </Grid>
                        </Button>
                    </StackPanel>

                    <!-- ── Unit Converter ── -->
                    <TextBlock Classes="subsection-heading" Text="Unit Converter"/>
                    <StackPanel Spacing="0">
                        <TextBlock Classes="label" Text="Examples — click to try"/>
                        <Button Classes="example-row" Command="{Binding TryExampleCommand}" CommandParameter="26 c">
                            <Grid ColumnDefinitions="130,*,Auto">
                                <TextBlock Grid.Column="0" Classes="expr-mono" Text="26 c"/>
                                <TextBlock Grid.Column="1" Classes="expr-desc" Text="Auto-convert to Fahrenheit" Margin="8,0"/>
                                <TextBlock Grid.Column="2" Classes="try-label" Text="Try…"/>
                            </Grid>
                        </Button>
                        <Button Classes="example-row" Command="{Binding TryExampleCommand}" CommandParameter="100 km/h">
                            <Grid ColumnDefinitions="130,*,Auto">
                                <TextBlock Grid.Column="0" Classes="expr-mono" Text="100 km/h"/>
                                <TextBlock Grid.Column="1" Classes="expr-desc" Text="Auto-convert to mph" Margin="8,0"/>
                                <TextBlock Grid.Column="2" Classes="try-label" Text="Try…"/>
                            </Grid>
                        </Button>
                        <Button Classes="example-row" Command="{Binding TryExampleCommand}" CommandParameter="5 kg to lbs">
                            <Grid ColumnDefinitions="130,*,Auto">
                                <TextBlock Grid.Column="0" Classes="expr-mono" Text="5 kg to lbs"/>
                                <TextBlock Grid.Column="1" Classes="expr-desc" Text="Explicit unit conversion" Margin="8,0"/>
                                <TextBlock Grid.Column="2" Classes="try-label" Text="Try…"/>
                            </Grid>
                        </Button>
                        <Button Classes="example-row" Command="{Binding TryExampleCommand}" CommandParameter="38000 s">
                            <Grid ColumnDefinitions="130,*,Auto">
                                <TextBlock Grid.Column="0" Classes="expr-mono" Text="38000 s"/>
                                <TextBlock Grid.Column="1" Classes="expr-desc" Text="Decomposes into hours, minutes, secs" Margin="8,0"/>
                                <TextBlock Grid.Column="2" Classes="try-label" Text="Try…"/>
                            </Grid>
                        </Button>
                        <Button Classes="example-row" Command="{Binding TryExampleCommand}" CommandParameter="1500 MB">
                            <Grid ColumnDefinitions="130,*,Auto">
                                <TextBlock Grid.Column="0" Classes="expr-mono" Text="1500 MB"/>
                                <TextBlock Grid.Column="1" Classes="expr-desc" Text="Best-fit unit — shows 1.5 GB" Margin="8,0"/>
                                <TextBlock Grid.Column="2" Classes="try-label" Text="Try…"/>
                            </Grid>
                        </Button>
                    </StackPanel>

                    <!-- ── Currency Exchange ── -->
                    <TextBlock Classes="subsection-heading" Text="Currency Exchange"/>

                    <StackPanel Spacing="6">
                        <TextBlock Classes="label" Text="Default Currency Pair"/>
                        <StackPanel Orientation="Horizontal" Spacing="8" VerticalAlignment="Center">
                            <TextBox Text="{Binding CalculatorCurrencyA}" Classes="flyout-input" Width="64" MaxLength="3"/>
                            <TextBlock Text="/" Foreground="{DynamicResource Theme.Results.Subtitle.Color}"
                                       VerticalAlignment="Center" FontSize="16"/>
                            <TextBox Text="{Binding CalculatorCurrencyB}" Classes="flyout-input" Width="64" MaxLength="3"/>
                        </StackPanel>
                        <TextBlock Classes="description"
                                   Text="When you type an amount in any currency, it converts to the left currency. The left currency converts to the right."/>
                    </StackPanel>

                    <StackPanel Spacing="6">
                        <TextBlock Classes="label" Text="Exchange Rates"/>
                        <CheckBox Content="Include metals (gold, silver, platinum, palladium)"
                                  IsChecked="{Binding CalculatorIncludeMetals}"/>
                        <CheckBox Content="Include cryptocurrencies"
                                  IsChecked="{Binding CalculatorIncludeCrypto}"/>
                    </StackPanel>

                    <StackPanel Spacing="0">
                        <TextBlock Classes="label" Text="Examples — click to try"/>
                        <Button Classes="example-row" Command="{Binding TryExampleCommand}" CommandParameter="100 USD">
                            <Grid ColumnDefinitions="130,*,Auto">
                                <TextBlock Grid.Column="0" Classes="expr-mono" Text="100 USD"/>
                                <TextBlock Grid.Column="1" Classes="expr-desc" Text="{Binding CurrencyExampleDesc}" Margin="8,0"/>
                                <TextBlock Grid.Column="2" Classes="try-label" Text="Try…"/>
                            </Grid>
                        </Button>
                        <Button Classes="example-row" Command="{Binding TryExampleCommand}" CommandParameter="50 EUR to GBP">
                            <Grid ColumnDefinitions="130,*,Auto">
                                <TextBlock Grid.Column="0" Classes="expr-mono" Text="50 EUR to GBP"/>
                                <TextBlock Grid.Column="1" Classes="expr-desc" Text="Explicit currency pair" Margin="8,0"/>
                                <TextBlock Grid.Column="2" Classes="try-label" Text="Try…"/>
                            </Grid>
                        </Button>
                        <Button Classes="example-row" Command="{Binding TryExampleCommand}" CommandParameter="1 BTC"
                                IsVisible="{Binding CalculatorIncludeCrypto}">
                            <Grid ColumnDefinitions="130,*,Auto">
                                <TextBlock Grid.Column="0" Classes="expr-mono" Text="1 BTC"/>
                                <TextBlock Grid.Column="1" Classes="expr-desc" Text="{Binding CryptoCurrencyExampleDesc}" Margin="8,0"/>
                                <TextBlock Grid.Column="2" Classes="try-label" Text="Try…"/>
                            </Grid>
                        </Button>
                    </StackPanel>

                    <StackPanel Spacing="6">
                        <TextBlock Classes="label" Text="Refresh Interval"/>
                        <Border Classes="numeric-field" Width="130" HorizontalAlignment="Left" ClipToBounds="True">
                            <NumericUpDown x:Name="ExchangeRateRefreshInput"
                                           Value="{Binding ExchangeRateRefreshIntervalHours}"
                                           Minimum="1" Maximum="168" Increment="1" FormatString="0"/>
                        </Border>
                        <TextBlock Classes="description" Text="How often to check for updated exchange rates (hours)."/>
                    </StackPanel>

                    <StackPanel Spacing="4">
                        <TextBlock Classes="label" Text="Last Rate Update"/>
                        <TextBlock Classes="description" Text="{Binding ExchangeRatesLastUpdatedText}"/>
                    </StackPanel>

                    </StackPanel> <!-- /EnableCalculator -->
                </StackPanel>
```

### 4d. Eliminar sección Converter completa (~líneas 1139–1287 en el original)

- [ ] **Eliminar el bloque que empieza en `<!-- Converter -->` y termina en el `</StackPanel>` de cierre de la sección:**

```xml
<!-- ELIMINAR desde aquí: -->
                <!-- Converter -->
                <StackPanel Spacing="16" IsVisible="{Binding IsConverterSelected}">
                    ...
                </StackPanel>
<!-- hasta aquí (inclusive) -->
```

### 4e. Compilar

- [ ] **Compilar:**

```bash
dotnet build Yottacast/Yottacast.csproj --no-restore -v quiet 2>&1 | tail -5
```

Expected: `0 Error(s)`. Si hay error de compiled binding sobre `CurrencyExampleDesc` o `CryptoCurrencyExampleDesc`, verificar que las propiedades están en el ViewModel y que el `x:DataType="vm:SettingsWindowViewModel"` sigue en la raíz del AXAML.

- [ ] **Commit:**

```bash
git add Yottacast/Views/SettingsWindow.axaml
git commit -m "feat: rediseñar ejemplos Calculator con tabla 3 col y subsecciones"
```

---

## Task 5: Docs + tests finales

- [ ] **Actualizar `docs/search-calculator.md` sección 1.2 (Invariantes):**

```markdown
# ANTES:
- La calculadora solo responde si al menos uno de `EnableCalculator` o `EnableConverter` está activo. `EnableCalculator` controla aritmética y ecuaciones; `EnableConverter` controla conversiones de unidades y divisas. Ambos toggles aparecen en Settings → Calculator y Settings → Converter respectivamente.

# DESPUÉS:
- La calculadora solo responde si `EnableCalculator` está activo. Controla aritmética, ecuaciones, conversiones de unidades y divisas. El toggle aparece en Settings → Calculator.
```

- [ ] **Verificar `docs/user-settings.md` y eliminar cualquier referencia a `EnableConverter` o `Settings → Converter`:**

```bash
grep -n "EnableConverter\|Converter" docs/user-settings.md
```

- [ ] **Suite completa:**

```bash
dotnet test Yottacast.Core.Tests/ -v quiet 2>&1 | tail -5
```

Expected: mismo baseline que Task 1.

- [ ] **Commit:**

```bash
git add docs/search-calculator.md docs/user-settings.md
git commit -m "docs: actualizar flags y settings para EnableCalculator unificado"
```

---

## Task 6: Build final y resumen

- [ ] **Build release:**

```bash
dotnet build Yottacast/Yottacast.csproj -c Release --no-restore -v quiet 2>&1 | tail -5
```

Expected: `0 Error(s)`.

- [ ] **Verificar git log:**

```bash
git log --oneline -5
```

Expected:
```
docs: actualizar flags y settings para EnableCalculator unificado
feat: rediseñar ejemplos Calculator con tabla 3 col y subsecciones
refactor: eliminar sección Converter del ViewModel, añadir CurrencyExampleDesc
refactor: unify EnableConverter into EnableCalculator
```
