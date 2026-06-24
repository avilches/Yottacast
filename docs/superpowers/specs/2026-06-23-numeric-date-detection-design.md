# Detección de fechas numéricas en DateSearch

**Fecha**: 2026-06-23

## Objetivo

Permitir que DateSearch reconozca fechas numéricas pegadas en formatos comunes (`2025-12-24`, `24-12-2025`, `12/24/2025`, `24.12.2025`), no solo ISO. El reto es la ambigüedad día/mes en formatos como `04-03-2015`, que se resuelve con una preferencia configurable y se hace visible al usuario mediante un subtítulo con la interpretación aplicada.

Esta feature extiende el comportamiento descrito en `2026-05-09-date-search-design.md`; el resto de contratos (resultado único, navegación de celdas, acciones) se mantiene igual.

---

## 1. Regla de aceptación

Una query **sin ninguna letra** se intenta parsear como fecha numérica antes de cualquier otra cosa. El reconocimiento de lenguaje natural (Microsoft.Recognizers) se reserva para queries que contienen letras.

Patrón aceptado: tres componentes numéricos separados por el **mismo** separador, que debe ser `-`, `/` o `.`:

```
^\s*(\d{1,4})([-/.])(\d{1,2})\2(\d{1,4})\s*$
```

- El separador central usa una backreference (`\2`): separadores mixtos (`04-03/2025`) se rechazan.
- Exactamente uno de los componentes primero/último debe tener **4 dígitos** (el año). El componente central nunca puede ser el año (`\d{1,2}`).

Esto excluye por construcción la entrada de calculadora: `1/2`, `16/9`, `3/4`, `12.5`, `134.2` tienen menos de tres componentes y nunca matchean. Las fechas sin año y con año de 2 dígitos quedan **fuera de alcance** (demasiada ambigüedad / colisión con números).

---

## 2. Resolución de año, mes y día

Sea `(a, sep, b, sep, c)` el resultado del patrón, con `aLen`/`cLen` el número de dígitos de `a`/`c`.

1. Si `aLen == 4` y `cLen == 4` → inválido (dos años).
2. Si ni `aLen == 4` ni `cLen == 4` → inválido (no hay año de 4 dígitos; el central nunca es año).
3. Si `aLen == 4` → **ISO** `a=año, b=mes, c=día`. No ambiguo.
4. Si `cLen == 4` → año al final; `a` y `b` son día/mes en algún orden:
   - `a > 12` y `b <= 12` → `día=a, mes=b` (formato D/M). No ambiguo.
   - `b > 12` y `a <= 12` → `mes=a, día=b` (formato M/D). No ambiguo.
   - `a <= 12` y `b <= 12` → **ambiguo** → se aplica la preferencia `DateNumericOrder`.
   - en otro caso (ambos `> 12`, o `> 31`) → inválido.
5. Validación final: construir `new DateTime(año, mes, día)`; si lanza (p. ej. `31-02-2025`) → inválido. Rango de año aceptado: 1–9999.

El parser devuelve, además de la fecha, el **formato interpretado** (`Iso`, `DayMonthYear`, `MonthDayYear`) y si la entrada era ambigua. Si el parser devuelve "no es fecha", DateSearch devuelve `[]` (la query sigue siendo número/cálculo).

---

## 3. Preferencia configurable

Nuevo setting `DateNumericOrder` (enum `{ DayFirst, MonthFirst }`):

- **Default inferido del SO**: se examina `CultureInfo.CurrentCulture.DateTimeFormat.ShortDatePattern`; si el primer especificador de día/mes es `M` → `MonthFirst`, en caso contrario `DayFirst`. (España → `DayFirst`; EE. UU. → `MonthFirst`.)
- Se usa **solo** en el caso ambiguo (punto 2.4, ambos componentes ≤ 12).
- Se persiste en `settings.json` como string (`"DayFirst"`/`"MonthFirst"`), con reparación a default si falta o es inválido.
- Editable en la ventana de Settings (sección Date Search).

---

## 4. Feedback visible

El resultado de una fecha parseada numéricamente muestra, como **subtítulo de la celda ISO**, el patrón con el que se interpretó la entrada:

| Formato interpretado | Subtítulo |
|---|---|
| `Iso` | `YYYY-MM-DD` |
| `DayMonthYear` | `DD/MM/YYYY` |
| `MonthDayYear` | `MM/DD/YYYY` |

Así el usuario ve de un vistazo cómo se leyó su entrada y, si en un caso ambiguo no es lo que quería, cambia la preferencia. El subtítulo se muestra siempre para fechas numéricas (también las no ambiguas, por consistencia y bajo coste). Las fechas de lenguaje natural siguen sin subtítulo, como hasta ahora.

---

## 5. Flujo en DateSearch.Search

```
if (!DateSearchEnabled) return [];
if (query en blanco) return [];

if (query no tiene letras) {
    parsed = NumericDateParser.TryParse(query, settings.DateNumericOrder);
    result = parsed != null ? construir resultado de fecha numérica : [];
    cachear (query, result), cancelar cualquier recognizer en vuelo;
    return result;   // síncrono, sin background, sin coste de arranque del recognizer
}

if (DateSearchLanguages vacío) return [];
... lanzar recognizer en background (sin cambios)
```

El parser numérico es **síncrono y locale-independiente**: no depende de `DateSearchLanguages` ni del arranque en frío del recognizer.

---

## 6. Componentes

- **`NumericDateParser`** (`Yottacast.Core/Search/Date/NumericDateParser.cs`): clase con método `static` puro `TryParse(string, DateNumericOrder)` que devuelve `Result?` (`record struct` con `Date`, `Format`, `Ambiguous`). Define también el enum `DateNumericOrder` y el enum `NumericDateFormat`. Sin estado, sin dependencias → `static` puro es aceptable (helper de parsing).
- **`DateSearch`**: integra el fast-path numérico y un constructor de resultado que reutiliza la lógica de fecha simple existente, añadiendo el subtítulo de interpretación a la celda ISO.
- **`UserSettings`**: nueva propiedad `DateNumericOrder` (+ data/load/save), default vía `AppDefaults.DefaultDateNumericOrder()`.
- **`AppDefaults`**: `static DateNumericOrder DefaultDateNumericOrder()` que infiere de la cultura.
- **`SettingsWindowViewModel`** + **`SettingsDateSearchView.axaml`**: selector de la preferencia (patrón `ComboBox` con tipo opción, espejo de `CurrencyOption`).

---

## 7. Invariantes

- Una query sin letras que no case el patrón numérico válido → `[]` (sigue siendo número/cálculo). En particular `134.2`, `12.5`, `1/2`, `16/9` nunca son fecha.
- Separadores mixtos → no es fecha.
- Sin año de 4 dígitos → no es fecha.
- Fecha de calendario inválida (`31-02-2025`) → no es fecha.
- La preferencia solo altera el resultado en el caso ambiguo (ambos componentes ≤ 12, año al final); nunca cambia un formato inequívoco como `24-12-2025` o `2025-12-24`.
- El parseo numérico es síncrono: para una fecha numérica válida, `Search` devuelve el resultado en la misma llamada (no vía `ResultChanged`).
- El subtítulo de interpretación se muestra para toda fecha numérica y refleja el orden realmente aplicado.

---

## 8. Tests

- `NumericDateParserTests`: ISO `2025-12-24`; D/M obvio `24-12-2025`; M/D obvio `12-24-2025`; ambiguo `04-03-2015` con cada preferencia (verifica fecha y `Ambiguous=true`); separadores `/` y `.`; inválidos (`31-02-2025`, dos años `2025-2024-01`, sin año `12-5-25`... espera: `12-5-25` no tiene año de 4 dígitos → inválido), `16/9`, `1/2`, `12.5`, `134.2`; default por cultura.
- `DateSearchTests`: `2025-12-24` y `24-12-2025` devuelven resultado **síncrono** (sin esperar `ResultChanged`); subtítulo de interpretación correcto; `134.2`/`12.5` siguen vacíos; `04-03-2015` respeta la preferencia configurada.
- `UserSettingsTests`: persistencia y reparación de `DateNumericOrder`.

> **Verificar en:** `Yottacast.Core/Search/Date/NumericDateParser.cs`, `Yottacast.Core/Search/Date/DateSearch.cs` (Search, construcción de resultado de fecha), `Yottacast.Core/Services/UserSettings.cs` (DateNumericOrder), `Yottacast.Core/AppDefaults.cs` (DefaultDateNumericOrder), `Yottacast/ViewModels/SettingsWindowViewModel.cs`, `Yottacast/Views/Settings/SettingsDateSearchView.axaml`, `Yottacast.Core.Tests/Search/Date/NumericDateParserTests.cs`, `Yottacast.Core.Tests/Search/Date/DateSearchTests.cs`.
