# Plan: detección de fechas numéricas en DateSearch

Spec: `docs/superpowers/specs/2026-06-23-numeric-date-detection-design.md`.

Ejecución con 3 agentes en paralelo, ficheros disjuntos. Los agentes **solo editan y escriben tests**; no ejecutan `dotnet build` ni `dotnet test`. Verificación central única al final (build de la solución + tests de Core).

## Partición (ficheros disjuntos)

### Agente 1 — Core de parsing + integración + tests de Core de fecha
- `Yottacast.Core/Search/Date/NumericDateParser.cs` (NUEVO): enums `DateNumericOrder`, `NumericDateFormat`; `static class NumericDateParser` con `TryParse` y `FormatLabel`.
- `Yottacast.Core/AppDefaults.cs`: `static DateNumericOrder DefaultDateNumericOrder()` (infiere de `CultureInfo.CurrentCulture`).
- `Yottacast.Core/Search/Date/DateSearch.cs`: fast-path numérico síncrono en `Search`; eliminar `IsoDateRegex` (lo cubre el parser); refactor de construcción de resultado de fecha simple para añadir subtítulo de interpretación.
- `Yottacast.Core.Tests/Search/Date/NumericDateParserTests.cs` (NUEVO).
- `Yottacast.Core.Tests/Search/Date/DateSearchTests.cs`: tests de integración numérica.

### Agente 2 — Persistencia del setting
- `Yottacast.Core/Services/UserSettings.cs`: propiedad `DateNumericOrder` + data (`dateNumericOrder` string) + Load (reparación a default) + Save.
- `Yottacast.Core.Tests/Services/UserSettingsTests.cs`: persistencia y reparación.

### Agente 3 — UI de settings
- `Yottacast/ViewModels/SettingsWindowViewModel.cs`: tipo opción + `DateNumericOrderOptions` + `SelectedDateNumericOrder` (`ObservableProperty`) + handler `OnSelectedDateNumericOrderChanged` + init en constructor.
- `Yottacast/Views/Settings/SettingsDateSearchView.axaml`: selector (espejo del patrón `CurrencyOption`/ComboBox existente).

## Contrato de tipos (compartido)

```csharp
namespace Yottacast.Core.Search.Date;
public enum DateNumericOrder { DayFirst, MonthFirst }
public enum NumericDateFormat { Iso, DayMonthYear, MonthDayYear }
public readonly record struct /* NumericDateParser.Result */ Result(DateTime Date, NumericDateFormat Format, bool Ambiguous);
```

## Verificación central
- `dotnet build Yottacast.sln`
- `cd Yottacast.Core.Tests && dotnet test`
- `cd Yottacast.Ipc.Tests && dotnet test`
