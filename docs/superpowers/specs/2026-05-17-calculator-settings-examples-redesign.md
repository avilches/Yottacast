# Spec: Rediseño de ejemplos y estructura de Settings → Calculator

**Fecha:** 2026-05-17  
**Estado:** Aprobado

---

## 1. Objetivo

Rediseñar la sección Settings → Calculator para que:

1. Los ejemplos tengan un layout de tabla (expresión | descripción | botón Try…) sin emoji ni resultado.
2. Los ejemplos cubran todas las capacidades de la calculadora.
3. Haya subsecciones claras: Math & Algebra, Unit Converter, Currency Exchange.
4. El toggle `EnableConverter` desaparezca; todo se controla con `EnableCalculator`.
5. Las descripciones que dependen de la divisa home sean reactivas al par configurado.

---

## 2. Layout de filas de ejemplo

Cada fila usa **tres columnas** en grid:

```
┌──────────────────┬─────────────────────────────┬────────┐
│  expresión mono  │  descripción pequeña         │ [Try…] │
└──────────────────┴─────────────────────────────┴────────┘
```

- Columna 1: expresión en `Courier New / monospace`, color title.
- Columna 2: descripción en `font-size=10`, color subtitle.
- Columna 3: botón `Try…` con borde, igual al botón `url-edit-btn` existente.
- Fila entera es el botón (clase `example-row`); el botón Try… dentro actúa solo como visual —
  o bien la fila entera es el botón y Try… es el content alineado a la derecha.
- Sin emoji ▶, sin texto de resultado.
- Separador `BorderThickness="0,0,0,1"` entre filas igual que los engine rows.

---

## 3. Estructura de la sección Calculator

Un único nav item **Calculator** en el sidebar (se elimina **Converter**).

```
Settings → Calculator
─────────────────────────────────────────────────────
  [toggle]  Enable Calculator

  Decimal places  [NumericUpDown 0–6]

  ── Math & Algebra ──────────────────────────────────
  Examples — click to try
  [tabla con 6 filas de ejemplos]

  ── Unit Converter ──────────────────────────────────
  Examples — click to try
  [tabla con 5 filas de ejemplos]

  ── Currency Exchange ───────────────────────────────
  Home currency pair  [TextBox A]  /  [TextBox B]
  [x] Include metals   [ ] Include crypto

  Examples — click to try
  [tabla con 2–3 filas de ejemplos]

  Exchange rate refresh  [NumericUpDown]  hours
─────────────────────────────────────────────────────
```

---

## 4. Ejemplos: Math & Algebra

| Expresión        | Descripción                          |
|------------------|--------------------------------------|
| `2 + 3 * 4`      | Operator precedence — result: 14     |
| `sqrt(pi)`       | Square root of π                     |
| `sin(45 deg)`    | Trigonometry in degrees              |
| `log(1000, 10)`  | Logarithm base 10                    |
| `2x - 5 = 2`     | Solve equation for x                 |
| `x^2 - 5x + 6`   | Symbolic: simplify, factor, derive   |

---

## 5. Ejemplos: Unit Converter

| Expresión        | Descripción                          |
|------------------|--------------------------------------|
| `26 c`           | Auto-convert to Fahrenheit           |
| `100 km/h`       | Convert to mph                       |
| `5 kg to lbs`    | Explicit unit conversion             |
| `38000 s`        | Decompose into hours, minutes, secs  |
| `1500 MB`        | Best-fit unit — shows 1.5 GB         |

---

## 6. Ejemplos: Currency Exchange

| Expresión   | Descripción (dinámica)                | Visibilidad        |
|-------------|---------------------------------------|--------------------|
| `100 USD`   | `"Convert to {CurrencyA}"`            | Siempre            |
| `50 EUR to GBP` | Explicit currency pair            | Siempre            |
| `1 BTC`     | `"Bitcoin in {CurrencyA}"`            | Solo si IncludeCrypto |

La descripción de la primera fila (`100 USD`) se genera desde la propiedad computed
`CurrencyExampleDesc` en el ViewModel:

```csharp
public string CurrencyExampleDesc => $"Convert to {CalculatorCurrencyA}";
```

Se añade `[NotifyPropertyChangedFor(nameof(CurrencyExampleDesc))]` al campo `_calculatorCurrencyA`.

Análogamente `CryptoCurrencyExampleDesc`:
```csharp
public string CryptoCurrencyExampleDesc => $"Bitcoin in {CalculatorCurrencyA}";
```

---

## 7. Unificación del flag: eliminar EnableConverter

### 7.1 UserSettings

Se elimina `EnableConverter`. `EnableCalculator` controla todo: aritmética, conversiones de
unidades y divisas.

No hace falta backward compatibility. Se elimina el settings, se añade una migración en `RunMigrations()`:
- Si `enableConverter == true && enableCalculator == false` → setear `EnableCalculator = true`.
- `enableConverter` se ignora al leer el JSON (deserialización resiliente).

### 7.2 CalculatorSearch.cs

Se eliminan todas las comprobaciones de `settings.EnableConverter`. El comportamiento unificado:
- Si `!settings.EnableCalculator` → return vacío en todos los paths.

### 7.3 SettingsWindowViewModel

- Se elimina `_enableConverter`, `EnableConverter`, `IsConverterSelected`, `SelectConverterCommand`.
- Se elimina `SettingsSection.Converter` del enum.
- Se añaden `CurrencyExampleDesc` y `CryptoCurrencyExampleDesc` como propiedades computed.

### 7.4 SettingsWindow.axaml

- Se elimina el nav item "Converter" del sidebar.
- Se elimina la sección `IsVisible="{Binding IsConverterSelected}"` completa.
- En la sección Calculator (`IsVisible="{Binding IsCalculatorSelected}"`):
  - Se añaden las subsecciones Unit Converter y Currency Exchange.
  - Se migran los settings de divisa (par, metals, crypto, refresh) a Currency Exchange.
  - Los ejemplos se rediseñan en layout B para las tres subsecciones.

---

## 8. Estilo AXAML del botón Try…

Se reutiliza la clase `url-edit-btn` existente (borde, padding, font-size=10, color subtitle).
Las filas de ejemplo mantienen la clase `example-row` como botón contenedor.

Layout interno del Button.Content (grid de 3 columnas):
```xml
<Grid ColumnDefinitions="120,*,Auto">
  <TextBlock Grid.Column="0" Classes="expr-mono" Text="26 c"/>
  <TextBlock Grid.Column="1" Classes="expr-desc" Text="Auto-convert to Fahrenheit"
             VerticalAlignment="Center" Margin="8,0"/>
  <TextBlock Grid.Column="2" Text="Try…" Classes="try-label"
             VerticalAlignment="Center"/>
</Grid>
```

Se añaden dos estilos nuevos:
- `TextBlock.expr-mono`: `FontFamily="Courier New, Cascadia Code, monospace"`, color title, size title.
- `TextBlock.expr-desc`: color subtitle, `FontSize="10"`.
- `TextBlock.try-label`: color subtitle, `FontSize="10"`, `Opacity="0.6"`.

La clase `example-row` existente ya provee hover. Los estilos nuevos son solo tipografía.

---

## 9. Archivos afectados

| Archivo | Cambio |
|---------|--------|
| `Yottacast.Core/Services/UserSettings.cs` | Eliminar `EnableConverter` |
| `Yottacast.Core/Search/Calculator/CalculatorSearch.cs` | Eliminar checks `EnableConverter` |
| `Yottacast/ViewModels/SettingsWindowViewModel.cs` | Eliminar sección Converter, añadir computed props |
| `Yottacast/Views/SettingsWindow.axaml` | Rediseñar sección Calculator, eliminar Converter |
| `Yottacast/App.axaml.cs` | Añadir migración `EnableConverter → EnableCalculator` |
| `docs/search-calculator.md` | Actualizar descripción de flags y settings |
| `docs/user-settings.md` | Eliminar `EnableConverter` |
| `Yottacast.Core.Tests/` | Actualizar tests que usen `EnableConverter` |

---

## 10. Tests

- Actualizar cualquier test en `CalculatorSearch*Tests` que setee `EnableConverter`.
- `EnableCalculator = false` debe silenciar toda la calculadora (math + conversiones + divisas).
- Añadir test: migración de `enableConverter=true, enableCalculator=false` → `enableCalculator=true`.
