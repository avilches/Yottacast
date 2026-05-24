# Calculator Settings Improvements — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix decimal-places refresh, round algebra decimals, add ComboBox for currency pair, move examples to bottom, and add more examples.

**Architecture:** Fixes are isolated: (1) App.axaml.cs gets an immediate `UpdateConfig` call before the async engine recreation; (2) nerdamer-helpers.js gets a `roundLongDecimals` function keyed to a per-call injected global; (3) ExchangeRateService exposes a forex list used by a ComboBox in AXAML; (4) AXAML-only rearrangement + new example buttons + ViewModel computed properties.

**Tech Stack:** Avalonia 11 / C# / JS (Jint) / CommunityToolkit.Mvvm / xUnit

---

## Files to modify

| File | What changes |
|------|-------------|
| `Yottacast/App.axaml.cs` | Add `UpdateConfig` call in `SearchSettingsChanged` handler |
| `Yottacast.Core/Search/Calculator/nerdamer-helpers.js` | Add `_ALGEBRA_DECIMALS` global + `roundLongDecimals` in `tryOp` |
| `Yottacast.Core/Search/Calculator/NerdamerEngine.cs` | Accept `decimalPlaces` param in `TryAlgebra` and `TrySolve` |
| `Yottacast.Core/Search/Calculator/CalculatorSearch.cs` | Pass `settings.CalculatorDecimalPlaces` to nerdamer calls |
| `Yottacast.Core/Search/Calculator/ExchangeRateService.cs` | Add `GetForexCurrencyCodes()` |
| `Yottacast/ViewModels/SettingsWindowViewModel.cs` | Add `AvailableForexCurrencies`, `CurrencyAExampleQuery`, `CurrencyBExampleQuery`; `[NotifyPropertyChangedFor]` for example props |
| `Yottacast/Views/SettingsWindow.axaml` | Reorganise Calculator section, replace TextBox with ComboBox, add examples |
| `Yottacast.Core.Tests/Search/Calculator/AlgebraSearchTests.cs` | Test for decimal rounding in algebra results |

---

## Task 1 — Fix decimal-places refresh

**Problem:** When the user changes *Result Decimal Places* in Settings, `SearchSettingsChanged` fires the search immediately—but the engine update (`RecreateAsync`) is async (~2 s). The search runs against the *old* engine.

**Fix:** Call `engine.UpdateConfig(...)` synchronously *before* scheduling the async recreation. `UpdateConfig` is already thread-safe and hot-patches `_FMT_LARGE_DECIMALS` in the running JS engine in milliseconds.

**Files:**
- Modify: `Yottacast/App.axaml.cs` (line ≈77)

- [ ] **Step 1 — Add `UpdateConfig` call in `SearchSettingsChanged` handler**

  In `App.axaml.cs`, find:
  ```csharp
  _services.GetRequiredService<UserSettings>().SearchSettingsChanged += () => {
      exchangeService.NotifySettingsChanged();
  };
  ```
  Replace with:
  ```csharp
  _services.GetRequiredService<UserSettings>().SearchSettingsChanged += () => {
      engineProvider.Current?.UpdateConfig(BuildFormatConfig(settings));
      exchangeService.NotifySettingsChanged();
  };
  ```

  `engineProvider` and `settings` are already captured in the same lambda scope. The `BuildFormatConfig` helper is defined just above this block.

- [ ] **Step 2 — Build and verify compilation**
  ```bash
  cd Yottacast && dotnet build 2>&1 | tail -5
  ```
  Expected: 0 errors.

- [ ] **Step 3 — Manual test**
  - Open app, type `123.456789 km to miles`
  - Open Settings → Calculator, change Decimal Places to 0 → result in main window updates to `76 mi` immediately
  - Change to 4 → updates to `76.7148 mi`

- [ ] **Step 4 — Commit**
  ```bash
  git add Yottacast/App.axaml.cs
  git commit -m "fix: actualizar config del engine inmediatamente al cambiar decimales"
  ```

---

## Task 2 — Round algebra decimals to configured decimal places

**Problem:** nerdamer returns raw decimal approximations like `0.3333333333333333*x^3` even when the user has configured 2 decimal places.

**Fix:** Pass the decimal-places setting into `NerdamerEngine` per call, inject it as a JS global, and apply rounding in `nerdamer-helpers.js` before returning each cell result.

**Files:**
- Modify: `Yottacast.Core/Search/Calculator/nerdamer-helpers.js`
- Modify: `Yottacast.Core/Search/Calculator/NerdamerEngine.cs`
- Modify: `Yottacast.Core/Search/Calculator/CalculatorSearch.cs`
- Modify: `Yottacast.Core.Tests/Search/Calculator/AlgebraSearchTests.cs`

### Step-by-step

- [ ] **Step 1 — Write the failing test**

  In `AlgebraSearchTests.cs`, add inside the class:

  ```csharp
  [Fact]
  public void TryAlgebra_IntegralWithRepeatingDecimal_RoundsToConfiguredPlaces() {
      // ∫(x^2 - 5x + 6)dx = x^3/3 - 5x^2/2 + 6x
      // nerdamer returns "0.3333333333333333*x^3" for the x^3/3 term
      var result = fixture.Engine.TryAlgebra("x^2 - 5*x + 6", decimalPlaces: 2);
      Assert.NotNull(result);
      var integralCell = result.Cells.FirstOrDefault(c => c.Label.StartsWith("∫"));
      Assert.NotNull(integralCell);
      // Must not contain long repeating decimals
      Assert.DoesNotContain("0.3333333333", integralCell!.Result);
      // Must be rounded to 2 dp: 0.33
      Assert.Contains("0.33", integralCell.Result);
  }

  [Fact]
  public void TryAlgebra_WithZeroDecimalPlaces_KeepsExactIntegers() {
      var result = fixture.Engine.TryAlgebra("x^2 + 2*x", decimalPlaces: 0);
      Assert.NotNull(result);
      // derivative of x^2+2x is 2x+2, no decimals involved
      var dCell = result.Cells.FirstOrDefault(c => c.Label == "d/dx");
      Assert.NotNull(dCell);
      Assert.Equal("2*x+2", dCell!.Result);
  }
  ```

- [ ] **Step 2 — Run test to verify it fails**
  ```bash
  cd Yottacast.Core.Tests && dotnet test --filter "TryAlgebra_IntegralWithRepeatingDecimal_RoundsToConfiguredPlaces|TryAlgebra_WithZeroDecimalPlaces" -v 2>&1 | tail -15
  ```
  Expected: compile error (method signature doesn't accept `decimalPlaces` yet).

- [ ] **Step 3 — Update `nerdamer-helpers.js`**

  At the top of the file, before `function solveEquation`, add:
  ```javascript
  var _ALGEBRA_DECIMALS = 2; // injected by C# before each call

  function roundLongDecimals(text) {
      return text.replace(/-?\d+\.\d+/g, function(match) {
          var dot = match.indexOf('.');
          var decimPart = match.substring(dot + 1);
          if (decimPart.length > _ALGEBRA_DECIMALS) {
              var n = parseFloat(match);
              var rounded = parseFloat(n.toFixed(_ALGEBRA_DECIMALS)).toString();
              return rounded;
          }
          return match;
      });
  }
  ```

  In `getAlgebraResults`, inside `tryOp`, change:
  ```javascript
  function tryOp(label, fn) {
      try {
          var r = fn();
          if (!r) return;
          var text = r.text ? r.text() : String(r);
          if (text === expr) return;          // no-op: result equals raw input
  ```
  To:
  ```javascript
  function tryOp(label, fn) {
      try {
          var r = fn();
          if (!r) return;
          var text = roundLongDecimals(r.text ? r.text() : String(r));
          if (text === expr) return;          // no-op: result equals raw input
  ```

  Also apply rounding to equation solver results. In `solveEquation`, change:
  ```javascript
  var solText = sol.text ? sol.text() : String(sol);
  ```
  To:
  ```javascript
  var solText = roundLongDecimals(sol.text ? sol.text() : String(sol));
  ```

- [ ] **Step 4 — Update `NerdamerEngine.cs`**

  Change signatures of both public methods to accept `decimalPlaces`:

  ```csharp
  public SolveResult? TrySolve(string query, int decimalPlaces = 2) {
      if (_engine == null) return null;
      lock (_lock) {
          if (_engine == null) return null;
          try {
              _engine.Execute($"_ALGEBRA_DECIMALS = {decimalPlaces};");
              var json = _engine.Evaluate($"solveEquation({JsonSerializer.Serialize(query)})");
  ```

  ```csharp
  public AlgebraResult? TryAlgebra(string expr, int decimalPlaces = 2) {
      if (_engine == null) return null;
      lock (_lock) {
          if (_engine == null) return null;
          try {
              _engine.Execute($"_ALGEBRA_DECIMALS = {decimalPlaces};");
              var json = _engine.Evaluate($"getAlgebraResults({JsonSerializer.Serialize(expr)})");
  ```

  The `_engine.Execute(...)` call is inside the same `lock` so it's thread-safe.

- [ ] **Step 5 — Update `CalculatorSearch.cs`**

  Find the two nerdamer calls and pass decimal places:
  ```csharp
  // Equation with '='
  var solveResult = nerdamerEngine.TrySolve(q, settings.CalculatorDecimalPlaces);
  ```
  ```csharp
  // Algebra without '='
  var algebraResult = nerdamerEngine.TryAlgebra(q, settings.CalculatorDecimalPlaces);
  ```

  Locate the exact calls in `CalculatorSearch.Search()` (lines ≈34 and ≈190) and add the parameter.

- [ ] **Step 6 — Run tests**
  ```bash
  cd Yottacast.Core.Tests && dotnet test --filter "AlgebraSearch" -v 2>&1 | tail -20
  ```
  Expected: all algebra tests PASS.

- [ ] **Step 7 — Run full test suite**
  ```bash
  cd Yottacast.Core.Tests && dotnet test 2>&1 | tail -10
  ```
  Expected: 0 failures.

- [ ] **Step 8 — Commit**
  ```bash
  git add Yottacast.Core/Search/Calculator/nerdamer-helpers.js \
          Yottacast.Core/Search/Calculator/NerdamerEngine.cs \
          Yottacast.Core/Search/Calculator/CalculatorSearch.cs \
          Yottacast.Core.Tests/Search/Calculator/AlgebraSearchTests.cs
  git commit -m "fix: aplicar decimales configurados a resultados algebraicos de nerdamer"
  ```

---

## Task 3 — Currency pair as ComboBox (always-valid value)

**Problem:** CurrencyA/CurrencyB are TextBoxes that accept any text; the user can type an invalid code.

**Fix:** Expose forex currency codes from `ExchangeRateService` and bind a `ComboBox` in AXAML.

**Files:**
- Modify: `Yottacast.Core/Search/Calculator/ExchangeRateService.cs`
- Modify: `Yottacast/ViewModels/SettingsWindowViewModel.cs`
- Modify: `Yottacast/Views/SettingsWindow.axaml`

- [ ] **Step 1 — Add `GetForexCurrencyCodes()` to ExchangeRateService**

  Add a private fallback list and a public method (after the `BuildActiveRates` method, around line 110):

  ```csharp
  private static readonly IReadOnlyList<string> _forexFallback = [
      "AUD","BRL","CAD","CHF","CNY","DKK","EUR","GBP","HKD",
      "HUF","IDR","ILS","INR","JPY","KRW","MXN","MYR","NOK",
      "NZD","PHP","PLN","RON","SEK","SGD","THB","TRY","USD","ZAR"
  ];

  /// <summary>
  /// Returns all forex (non-metal, non-crypto) currency codes from downloaded rates,
  /// sorted alphabetically. Falls back to a hardcoded list if rates aren't loaded yet.
  /// </summary>
  public IReadOnlyList<string> GetForexCurrencyCodes() {
      IReadOnlyDictionary<string, double> snapshot;
      lock (_lock) { snapshot = _allRates; }
      var forex = snapshot.Keys
          .Where(k => CurrencyClassifier.Classify(k) == CurrencyType.Forex)
          .OrderBy(k => k)
          .ToList();
      return forex.Count > 0 ? forex : _forexFallback;
  }
  ```

- [ ] **Step 2 — Add `AvailableForexCurrencies` to SettingsWindowViewModel**

  After the existing `ExchangeRateService` field declaration, add a property:
  ```csharp
  public IReadOnlyList<string> AvailableForexCurrencies { get; private set; } = [];
  ```

  In the constructor, after `_exchangeRateService = exchangeRateService;`:
  ```csharp
  var forex = exchangeRateService.GetForexCurrencyCodes();
  // Ensure the currently saved values are always in the list (handles custom/old codes)
  var extra = new[] { settings.CalculatorCurrencyA, settings.CalculatorCurrencyB }
      .Where(c => !string.IsNullOrEmpty(c) && !forex.Contains(c, StringComparer.OrdinalIgnoreCase))
      .Select(c => c.ToUpperInvariant());
  AvailableForexCurrencies = forex.Concat(extra).OrderBy(c => c).ToList();
  ```

- [ ] **Step 3 — Update AXAML: replace TextBox with ComboBox**

  Find the Default Currency Pair section in `SettingsWindow.axaml` (around line 1154):
  ```axaml
  <TextBox Text="{Binding CalculatorCurrencyA}" Classes="flyout-input" Width="64" MaxLength="3"/>
  <TextBlock Text="/" .../>
  <TextBox Text="{Binding CalculatorCurrencyB}" Classes="flyout-input" Width="64" MaxLength="3"/>
  ```
  Replace with:
  ```axaml
  <ComboBox ItemsSource="{Binding AvailableForexCurrencies}"
            SelectedItem="{Binding CalculatorCurrencyA}"
            Width="80"/>
  <TextBlock Text="/" Foreground="{DynamicResource Theme.Results.Subtitle.Color}"
             VerticalAlignment="Center" FontSize="16"/>
  <ComboBox ItemsSource="{Binding AvailableForexCurrencies}"
            SelectedItem="{Binding CalculatorCurrencyB}"
            Width="80"/>
  ```

- [ ] **Step 4 — Build and verify**
  ```bash
  cd Yottacast && dotnet build 2>&1 | tail -5
  ```
  Expected: 0 errors.

- [ ] **Step 5 — Manual test**
  - Open Settings → Calculator → Default Currency Pair
  - Both dropdowns should show a sorted list of forex currencies
  - The current values (EUR / USD) should be pre-selected
  - Selecting a different currency should update the search immediately

- [ ] **Step 6 — Commit**
  ```bash
  git add Yottacast.Core/Search/Calculator/ExchangeRateService.cs \
          Yottacast/ViewModels/SettingsWindowViewModel.cs \
          Yottacast/Views/SettingsWindow.axaml
  git commit -m "feat: combo de monedas para el par de divisas en settings"
  ```

---

## Task 4 — Move examples to bottom + add more examples

**Problem:** Examples are interleaved with configuration. The user wants them all at the bottom. Also, the current examples don't showcase many unit types or math functions.

**Files:**
- Modify: `Yottacast/Views/SettingsWindow.axaml`
- Modify: `Yottacast/ViewModels/SettingsWindowViewModel.cs`

### Computed example queries that follow the configured currency pair

In `SettingsWindowViewModel.cs`, the `CurrencyExampleDesc` already uses `CalculatorCurrencyA`. Add two new properties for dynamic example queries:

- [ ] **Step 1 — Add example query properties to SettingsWindowViewModel**

  After `CurrencyExampleDesc` and `CryptoCurrencyExampleDesc` (around line 224):
  ```csharp
  // Example queries — use the configured pair so the example is always valid
  [NotifyPropertyChangedFor(nameof(CurrencyAExampleQuery))]
  [NotifyPropertyChangedFor(nameof(CurrencyBExampleQuery))]
  [NotifyPropertyChangedFor(nameof(CurrencyExampleDesc))]
  [NotifyPropertyChangedFor(nameof(CryptoCurrencyExampleDesc))]
  private string _calculatorCurrencyA = "EUR";

  public string CurrencyExampleDesc       => $"Convert to {CalculatorCurrencyA}";
  public string CryptoCurrencyExampleDesc => $"Bitcoin in {CalculatorCurrencyA}";
  public string CurrencyAExampleQuery     => $"100 {CalculatorCurrencyB}";
  public string CurrencyBExampleQuery     => $"50 {CalculatorCurrencyA} to {CalculatorCurrencyB}";
  ```

  > Note: `[NotifyPropertyChangedFor]` attributes must be on the *field*, not the property. The existing `[NotifyPropertyChangedFor(nameof(CurrencyExampleDesc))]` annotations are already there — add `CurrencyAExampleQuery` and `CurrencyBExampleQuery` to the same attribute list.

  After `CurrencyBExampleQuery` also add:
  ```csharp
  [ObservableProperty]
  [NotifyPropertyChangedFor(nameof(CurrencyBExampleQuery))]
  private string _calculatorCurrencyB = "USD";
  ```

  Check which `NotifyPropertyChangedFor` the existing `_calculatorCurrencyB` field uses, and extend the list.

- [ ] **Step 2 — Restructure the Calculator section in SettingsWindow.axaml**

  The new order inside the `<!-- Calculator -->` `<StackPanel>`:

  1. Enable toggle
  2. Decimal Places
  3. Currency Exchange (heading)
     - Currency Pair (now ComboBox from Task 3)
     - Exchange Rates checkboxes
     - Refresh Interval
  4. Examples — Try it (heading, at the bottom)
     - Math & Algebra subsection with expanded examples
     - Unit Converter subsection with expanded examples
     - Currency Exchange subsection with dynamic examples

  Remove the old `<!-- ── Math and Algebra ──>` and `<!-- ── Unit Converter ──>` subsection headings from their current positions (lines ≈1073 and ≈1115). Move all example StackPanels to after the Refresh Interval block.

- [ ] **Step 3 — Write the new examples block**

  Replace the old three example StackPanels at the bottom of the Calculator section with the following (after the Refresh Interval block):

  ```axaml
  <!-- ── Examples ── -->
  <TextBlock Classes="subsection-heading" Text="Examples — click to try"/>

  <TextBlock Classes="label" Text="Math &amp; Algebra"/>
  <StackPanel Spacing="0">
      <Button Classes="example-row" Command="{Binding TryExampleCommand}" CommandParameter="2 + 3 * 4">
          <Grid ColumnDefinitions="130,*">
              <TextBlock Grid.Column="0" Classes="expr-mono" Text="2 + 3 * 4"/>
              <TextBlock Grid.Column="1" Classes="expr-desc" Text="Operator precedence → 14" Margin="8,0"/>
          </Grid>
      </Button>
      <Button Classes="example-row" Command="{Binding TryExampleCommand}" CommandParameter="sqrt(pi)">
          <Grid ColumnDefinitions="130,*">
              <TextBlock Grid.Column="0" Classes="expr-mono" Text="sqrt(pi)"/>
              <TextBlock Grid.Column="1" Classes="expr-desc" Text="Square root of π" Margin="8,0"/>
          </Grid>
      </Button>
      <Button Classes="example-row" Command="{Binding TryExampleCommand}" CommandParameter="sin(45 deg)">
          <Grid ColumnDefinitions="130,*">
              <TextBlock Grid.Column="0" Classes="expr-mono" Text="sin(45 deg)"/>
              <TextBlock Grid.Column="1" Classes="expr-desc" Text="Trigonometry in degrees → 0.71" Margin="8,0"/>
          </Grid>
      </Button>
      <Button Classes="example-row" Command="{Binding TryExampleCommand}" CommandParameter="log(1000, 10)">
          <Grid ColumnDefinitions="130,*">
              <TextBlock Grid.Column="0" Classes="expr-mono" Text="log(1000, 10)"/>
              <TextBlock Grid.Column="1" Classes="expr-desc" Text="Logarithm base 10 → 3" Margin="8,0"/>
          </Grid>
      </Button>
      <Button Classes="example-row" Command="{Binding TryExampleCommand}" CommandParameter="hypot(3, 4)">
          <Grid ColumnDefinitions="130,*">
              <TextBlock Grid.Column="0" Classes="expr-mono" Text="hypot(3, 4)"/>
              <TextBlock Grid.Column="1" Classes="expr-desc" Text="Hypotenuse of 3-4-5 triangle → 5" Margin="8,0"/>
          </Grid>
      </Button>
      <Button Classes="example-row" Command="{Binding TryExampleCommand}" CommandParameter="factorial(10)">
          <Grid ColumnDefinitions="130,*">
              <TextBlock Grid.Column="0" Classes="expr-mono" Text="factorial(10)"/>
              <TextBlock Grid.Column="1" Classes="expr-desc" Text="10! = 3,628,800" Margin="8,0"/>
          </Grid>
      </Button>
      <Button Classes="example-row" Command="{Binding TryExampleCommand}" CommandParameter="gcd(48, 18)">
          <Grid ColumnDefinitions="130,*">
              <TextBlock Grid.Column="0" Classes="expr-mono" Text="gcd(48, 18)"/>
              <TextBlock Grid.Column="1" Classes="expr-desc" Text="Greatest common divisor → 6" Margin="8,0"/>
          </Grid>
      </Button>
      <Button Classes="example-row" Command="{Binding TryExampleCommand}" CommandParameter="erf(1)">
          <Grid ColumnDefinitions="130,*">
              <TextBlock Grid.Column="0" Classes="expr-mono" Text="erf(1)"/>
              <TextBlock Grid.Column="1" Classes="expr-desc" Text="Error function (statistics) → 0.84" Margin="8,0"/>
          </Grid>
      </Button>
      <Button Classes="example-row" Command="{Binding TryExampleCommand}" CommandParameter="2x - 5 = 2">
          <Grid ColumnDefinitions="130,*">
              <TextBlock Grid.Column="0" Classes="expr-mono" Text="2x - 5 = 2"/>
              <TextBlock Grid.Column="1" Classes="expr-desc" Text="Solve equation for x → 3.5" Margin="8,0"/>
          </Grid>
      </Button>
      <Button Classes="example-row" Command="{Binding TryExampleCommand}" CommandParameter="x^2 - 5x + 6">
          <Grid ColumnDefinitions="130,*">
              <TextBlock Grid.Column="0" Classes="expr-mono" Text="x^2 - 5x + 6"/>
              <TextBlock Grid.Column="1" Classes="expr-desc" Text="Factor / derive / integrate" Margin="8,0"/>
          </Grid>
      </Button>
      <Button Classes="example-row" Command="{Binding TryExampleCommand}" CommandParameter="sin(x)">
          <Grid ColumnDefinitions="130,*">
              <TextBlock Grid.Column="0" Classes="expr-mono" Text="sin(x)"/>
              <TextBlock Grid.Column="1" Classes="expr-desc" Text="d/dx: cos(x), ∫dx: −cos(x)" Margin="8,0"/>
          </Grid>
      </Button>
  </StackPanel>

  <TextBlock Classes="label" Text="Unit Converter"/>
  <StackPanel Spacing="0">
      <Button Classes="example-row" Command="{Binding TryExampleCommand}" CommandParameter="26 c">
          <Grid ColumnDefinitions="130,*">
              <TextBlock Grid.Column="0" Classes="expr-mono" Text="26 c"/>
              <TextBlock Grid.Column="1" Classes="expr-desc" Text="Celsius → Fahrenheit (auto)" Margin="8,0"/>
          </Grid>
      </Button>
      <Button Classes="example-row" Command="{Binding TryExampleCommand}" CommandParameter="70 F">
          <Grid ColumnDefinitions="130,*">
              <TextBlock Grid.Column="0" Classes="expr-mono" Text="70 F"/>
              <TextBlock Grid.Column="1" Classes="expr-desc" Text="Fahrenheit → Celsius → 21.11 °C" Margin="8,0"/>
          </Grid>
      </Button>
      <Button Classes="example-row" Command="{Binding TryExampleCommand}" CommandParameter="100 km/h">
          <Grid ColumnDefinitions="130,*">
              <TextBlock Grid.Column="0" Classes="expr-mono" Text="100 km/h"/>
              <TextBlock Grid.Column="1" Classes="expr-desc" Text="Speed → mi/h (auto)" Margin="8,0"/>
          </Grid>
      </Button>
      <Button Classes="example-row" Command="{Binding TryExampleCommand}" CommandParameter="100 mph to km/h">
          <Grid ColumnDefinitions="130,*">
              <TextBlock Grid.Column="0" Classes="expr-mono" Text="100 mph to km/h"/>
              <TextBlock Grid.Column="1" Classes="expr-desc" Text="US speed to metric → 160.93 km/h" Margin="8,0"/>
          </Grid>
      </Button>
      <Button Classes="example-row" Command="{Binding TryExampleCommand}" CommandParameter="5 kg to lbs">
          <Grid ColumnDefinitions="130,*">
              <TextBlock Grid.Column="0" Classes="expr-mono" Text="5 kg to lbs"/>
              <TextBlock Grid.Column="1" Classes="expr-desc" Text="Mass conversion → 11.02 lbs" Margin="8,0"/>
          </Grid>
      </Button>
      <Button Classes="example-row" Command="{Binding TryExampleCommand}" CommandParameter="6 ft to m">
          <Grid ColumnDefinitions="130,*">
              <TextBlock Grid.Column="0" Classes="expr-mono" Text="6 ft to m"/>
              <TextBlock Grid.Column="1" Classes="expr-desc" Text="Height conversion → 1.83 m" Margin="8,0"/>
          </Grid>
      </Button>
      <Button Classes="example-row" Command="{Binding TryExampleCommand}" CommandParameter="1 atm to Pa">
          <Grid ColumnDefinitions="130,*">
              <TextBlock Grid.Column="0" Classes="expr-mono" Text="1 atm to Pa"/>
              <TextBlock Grid.Column="1" Classes="expr-desc" Text="Pressure → 101325 Pa" Margin="8,0"/>
          </Grid>
      </Button>
      <Button Classes="example-row" Command="{Binding TryExampleCommand}" CommandParameter="5 acre to m2">
          <Grid ColumnDefinitions="130,*">
              <TextBlock Grid.Column="0" Classes="expr-mono" Text="5 acre to m2"/>
              <TextBlock Grid.Column="1" Classes="expr-desc" Text="Area → 20234 m²" Margin="8,0"/>
          </Grid>
      </Button>
      <Button Classes="example-row" Command="{Binding TryExampleCommand}" CommandParameter="38000 s">
          <Grid ColumnDefinitions="130,*">
              <TextBlock Grid.Column="0" Classes="expr-mono" Text="38000 s"/>
              <TextBlock Grid.Column="1" Classes="expr-desc" Text="Decomposes into hours, minutes, secs" Margin="8,0"/>
          </Grid>
      </Button>
      <Button Classes="example-row" Command="{Binding TryExampleCommand}" CommandParameter="1500 MB">
          <Grid ColumnDefinitions="130,*">
              <TextBlock Grid.Column="0" Classes="expr-mono" Text="1500 MB"/>
              <TextBlock Grid.Column="1" Classes="expr-desc" Text="Best-fit unit → 1.5 GB" Margin="8,0"/>
          </Grid>
      </Button>
      <Button Classes="example-row" Command="{Binding TryExampleCommand}" CommandParameter="1 lightyear to km">
          <Grid ColumnDefinitions="130,*">
              <TextBlock Grid.Column="0" Classes="expr-mono" Text="1 lightyear to km"/>
              <TextBlock Grid.Column="1" Classes="expr-desc" Text="Astronomical → 9.46 × 10¹² km" Margin="8,0"/>
          </Grid>
      </Button>
  </StackPanel>

  <TextBlock Classes="label" Text="Currency Exchange"/>
  <StackPanel Spacing="0">
      <Button Classes="example-row" Command="{Binding TryExampleCommand}"
              CommandParameter="{Binding CurrencyAExampleQuery}">
          <Grid ColumnDefinitions="130,*">
              <TextBlock Grid.Column="0" Classes="expr-mono" Text="{Binding CurrencyAExampleQuery}"/>
              <TextBlock Grid.Column="1" Classes="expr-desc" Text="{Binding CurrencyExampleDesc}" Margin="8,0"/>
          </Grid>
      </Button>
      <Button Classes="example-row" Command="{Binding TryExampleCommand}"
              CommandParameter="{Binding CurrencyBExampleQuery}">
          <Grid ColumnDefinitions="130,*">
              <TextBlock Grid.Column="0" Classes="expr-mono" Text="{Binding CurrencyBExampleQuery}"/>
              <TextBlock Grid.Column="1" Classes="expr-desc" Text="Explicit currency pair" Margin="8,0"/>
          </Grid>
      </Button>
      <Button Classes="example-row" Command="{Binding TryExampleCommand}" CommandParameter="1 BTC"
              IsVisible="{Binding CalculatorIncludeCrypto}">
          <Grid ColumnDefinitions="130,*">
              <TextBlock Grid.Column="0" Classes="expr-mono" Text="1 BTC"/>
              <TextBlock Grid.Column="1" Classes="expr-desc" Text="{Binding CryptoCurrencyExampleDesc}" Margin="8,0"/>
          </Grid>
      </Button>
  </StackPanel>
  ```

  Note: `CurrencyAExampleQuery` and `CurrencyBExampleQuery` are now bound for both `CommandParameter` and `Text`, so examples always show the configured pair (e.g. "100 USD" if B is USD).

- [ ] **Step 4 — Build and verify**
  ```bash
  cd Yottacast && dotnet build 2>&1 | tail -5
  ```
  Expected: 0 errors.

- [ ] **Step 5 — Manual test**
  - Open Settings → Calculator
  - Verify: Enable toggle and Decimal Places appear first, then Currency Exchange config, then Examples at bottom
  - Click each new example and verify result appears in main window
  - Check specifically: `hypot(3, 4)` → 5, `factorial(10)` → 3628800, `gcd(48, 18)` → 6, `erf(1)` → ≈0.84, `sin(x)` shows algebra cells
  - Check that currency examples use the configured pair

- [ ] **Step 6 — Run tests**
  ```bash
  cd Yottacast.Core.Tests && dotnet test 2>&1 | tail -5
  ```

- [ ] **Step 7 — Update docs/search-calculator.md**

  In section 3b (Álgebra simbólica), update the `getAlgebraResults` line in the verification block to mention that decimal rounding is now applied via `_ALGEBRA_DECIMALS`.

  In section 9 (Formateo de resultados), add a note that algebraic expressions also respect the decimal places setting via `roundLongDecimals` in `nerdamer-helpers.js`.

- [ ] **Step 8 — Commit**
  ```bash
  git add Yottacast/ViewModels/SettingsWindowViewModel.cs \
          Yottacast/Views/SettingsWindow.axaml \
          docs/search-calculator.md
  git commit -m "feat: mover ejemplos al final de settings y añadir más unidades y funciones"
  ```

---

## Self-review

**Spec coverage:**
- ✅ Decimal refresh: Task 1 — `UpdateConfig` before `NotifySettingsChanged`
- ✅ Algebra decimals: Task 2 — `roundLongDecimals` in nerdamer-helpers + decimals param in NerdamerEngine
- ✅ Currency ComboBox: Task 3 — `GetForexCurrencyCodes` + AXAML ComboBox
- ✅ Examples to bottom: Task 4 — restructured AXAML
- ✅ More examples: Task 4 — `hypot`, `factorial`, `gcd`, `erf`, `sin(x)`, `70 F`, `100 mph to km/h`, `6 ft to m`, `1 atm to Pa`, `5 acre to m2`, `1 lightyear to km`
- ✅ Dynamic currency examples: Task 4 — `CurrencyAExampleQuery` / `CurrencyBExampleQuery` bound to CommandParameter

**Potential issues:**
- In Task 3, `ComboBox.SelectedItem` binding for strings with compiled bindings: if `CalculatorCurrencyA` is a `string` property (it is, via `[ObservableProperty]`), the binding `SelectedItem="{Binding CalculatorCurrencyA}"` works out of the box.
- `CommandParameter="{Binding CurrencyAExampleQuery}"` with compiled bindings: the AXAML declaration needs `x:DataType` on the enclosing scope. Verify the Calculator section has `DataContext` properly typed (it inherits from the Window's DataContext).
- Task 2: `_ALGEBRA_DECIMALS = {decimalPlaces};` is executed under the same `lock (_lock)` as the subsequent `getAlgebraResults` call — so it's race-condition-safe. ✓
- `1 lightyear to km` — verify math.js supports `lightyear` as a unit name before committing. If not, use `9.461e12 km` as the example or a supported unit like `1 parsec to ly`.
