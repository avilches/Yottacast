# Calculadora y conversor de unidades

## Proposito

El launcher incluye una calculadora integrada y un conversor de unidades/divisas que responde en tiempo real mientras el
usuario escribe. Funciona como una fuente de busqueda instantanea: si la query del usuario es una expresion matematica o
una conversion valida, aparece un resultado sin necesidad de pulsar Enter ni seleccionar ninguna categoria.

---

## 1. Comportamiento general

### 1.1 Que puede hacer el usuario

| Tipo de entrada       | Ejemplo                      | Que ve el usuario                        |
|-----------------------|------------------------------|------------------------------------------|
| Expresion aritmetica  | `2 + 3 * 4`                  | Resultado: `14`                          |
| Aritmetica con unidad | `2 m + 3 m`                  | Resultado: `5 m` con nombre largo debajo (`5 metres`) |
| Funciones matematicas | `sqrt(144)`, `sin(45 deg)`   | Resultado numerico                       |
| Conversion explicita  | `10 kg to lbs`, `100 F to C` | Resultado con origen y destino           |
| Conversion implicita  | `10 km`, `60 km/h`           | Conversion automatica al par por defecto |
| Conversion de divisas | `100 USD`, `50 EUR to GBP`   | Conversion con tasas actualizadas        |
| Entrada de tiempo     | `38000 s`                    | Descomposicion: `10 h 33 min 20 s`       |
| Entrada de datos      | `1500 MB`                    | Mejor unidad: `1.5 GB`                   |
| Simplificación algebraica | `2*x+3*x`             | Celdas navegables: `simplify: 5*x`, `d/dx: 5`, `∫dx: 5*x^2/2` |
| Factorización             | `x^2-5*x+6`           | Celdas navegables: `factor: (x-2)*(x-3)`, `d/dx: 2*x-5`       |
| Derivada/integral         | `sin(x)`              | Celdas navegables: `d/dx: cos(x)`, `∫dx: -cos(x)`              |

### 1.2 Invariantes de la experiencia de usuario

- El resultado **nunca** aparece si la evaluacion devuelve exactamente la misma cadena que la query de entrada (por
  ejemplo, escribir solo `42` no produce resultado).
- La calculadora devuelve **como maximo un resultado** por query. El parametro `limit` del contrato de busqueda se
  ignora.
- El resultado numerico, de conversion y de ecuacion tiene prioridad alta (score `7`), por lo que aparece cerca de la
  cima de las sugerencias. El resultado de algebra simbolica es la excepcion: usa `AppDefaults.AlgebraResultScore`
  (4.01). Ver seccion 3b.
- Los errores de unidades incompatibles (`1 kg to m`) y los avisos de ambiguedad de unidades (`Maybe you meant...`) se
  muestran como hint informativo debajo del campo de busqueda via `LastHint`.
  Los errores de simbolos desconocidos se descartan silenciosamente para no generar ruido con texto plano (
  `safari to km`).
- La activacion (Enter) **copia al portapapeles y pega en la app anterior**: la accion por defecto del item lleva
  `PasteAfterClose = true` (campo de `ResultAction`). Copia el resultado aritmetico para calculos, o la celda
  seleccionada para conversiones. Cmd+C copia sin cerrar la ventana.
- El icono del resultado distingue el tipo: calculadora para aritmetica, conversor para unidades/divisas.
- La calculadora solo responde si `EnableCalculator` está activo. Controla aritmética, ecuaciones, conversiones de unidades y divisas. El toggle aparece en Settings → Calculator.

> **Verificar en:** `CalculatorSearch.Search()` en `Yottacast.Core/Search/Calculator/CalculatorSearch.cs`

### 1.3 Arranque y disponibilidad

`CalculatorSearch` no bloquea el arranque: `WhenReady()` devuelve inmediatamente. Mientras el engine aun no esta listo
(primeros ~2 s hasta que `ExchangeRateService` descarga tasas y `MathJsEngineProvider` termina de inicializar el engine),
`Search()` devuelve una lista vacia sin errores. El engine de la calculadora tarda en promedio 2 s en inicializarse
porque precarga todas las divisas activas en math.js.

`ExchangeRateService` arranca en background al inicio: primero lee la cache de disco (si existe y es reciente) y dispara
`RatesUpdated` de inmediato; si la cache es antigua o no existe, la descarga de la API ocurre tambien en background. En
cualquier caso el proceso de UI no se bloquea.

> **Verificar en:** `CalculatorSearch.WhenReady()` y `CalculatorSearch.Search()` en `CalculatorSearch.cs`;
`ExchangeRateService.StartAsync()` en `ExchangeRateService.cs`;
`MathJsEngineProvider.RecreateAsync()` en `MathJsEngineProvider.cs`;
constructor y `Initialize()` de `MathJsEngine` en `MathJsEngine.cs`

---

## 2. Reconocimiento de expresiones

Toda query pasa por un proceso de normalizacion que analiza el AST (arbol de sintaxis abstracta) de la expresion y la
clasifica en una de cuatro categorias:

| Categoria            | Significado                                              | Ejemplo             |
|----------------------|----------------------------------------------------------|---------------------|
| `calculation`        | Aritmetica pura, sin unidades                            | `2 + 3`, `sqrt(16)` |
| `unit_entry`         | Valor con unidad que tiene par de conversion por defecto | `10 km`, `60 km/h`  |
| `simple_conversion`  | Conversion explicita `valor unidad to unidad`            | `10 kg to lbs`      |
| `complex_conversion` | Expresion compleja con `to`                              | `2*5 kg to lbs`     |

La normalizacion tambien:

- Corrige el case de unidades y funciones (el usuario puede escribir `KG`, `SQRT`, `MILES` sin preocuparse por
  mayusculas/minusculas).
- Detecta divisas y las marca para registro dinamico de tasas.
- Detecta ambiguedades entre unidades (ej. `mg` podria ser miligramo o megagramo) y genera avisos.
- Limpia el AST: elimina bloques, asignaciones, y rechaza definiciones de funcion.
- Normaliza las keywords `TO`/`IN` a minusculas antes de parsear.

> **Verificar en:** `NormalizeExpressionCore()` en `MathJsEngine.cs`; `normalizeExpression()` en `mathjs-helpers.js`

---

## 3. Conversiones de unidades

### 3.1 Conversiones implicitas (par por defecto)

Cuando el usuario escribe solo un valor con una unidad (`10 km`), el sistema busca automaticamente un destino por
defecto. La busqueda sigue este orden de prioridad:

1. **`defaultTargets`**: mapa directo unidad-a-unidad (ej. `km` -> `mile`, `degC` -> `degF`).
2. **`defaultPairs`**: matching dimensional. Si la unidad es compatible dimensionalmente con alguno de los pares, se usa
   el otro miembro del par (ej. cualquier unidad de masa no listada cae al par `kg/lb`).
3. **Sin resultado**: si no hay par, la expresion se trata como calculo puro.

Para divisas, el par por defecto es EUR/USD.

> **Verificar en:** `findDefaultTarget()` en `mathjs-helpers.js`; seccion `defaultTargets` y `defaultPairs` en
`unit-config.json`

### 3.2 Unidades compuestas

Expresiones como `10 km/h` se reconocen como unidad compuesta (numerador / denominador). El sistema busca un destino por
defecto para la unidad compuesta completa (`km / h` -> `mi / h`). La tabla `defaultTargets` incluye pares para las
combinaciones mas comunes de velocidad y tasa de datos.

> **Verificar en:** `_isCompoundUnitEntry()` en `mathjs-helpers.js`; entradas de `defaultTargets` con ` / ` en
`unit-config.json`

### 3.3 Presentacion del resultado de conversion

Un resultado de conversion tiene dos modos de visualizacion:

| Situacion                       | Visualizacion                                   | Navegacion                         |
|---------------------------------|-------------------------------------------------|------------------------------------|
| Sin normalizacion del origen    | Item unico: `From → To`                         | Sin celdas. El item entero se resalta como un resultado normal. |
| Con normalizacion SI del origen | `[From original] → [From normalizado] → [To]`   | Dos celdas navegables: `NormFrom` y `To`. `From original` es contexto sin celda. |

Ejemplo de normalizacion SI: el usuario escribe `0.001 V to mA`. El from original es `0.001 V`, pero math.js lo
auto-simplifica a `1 mV`. Ambas formas se muestran.

Sin normalizacion, al activar se copia siempre el valor `To`. Con normalizacion, las celdas `NormFrom` y `To` se navegan
con flechas izquierda/derecha; la celda por defecto es `To` (derecha). Al activar, se copia la celda seleccionada.
El fondo de seleccion del ListBoxItem se suprime para evitar doble resaltado (clase `conv-navigable`).

> **Verificar en:** `ConversionResultItemViewModel` en `Yottacast.Core/ViewModels/ConversionResultItemViewModel.cs`;
`EvaluateSimple()` y `EvaluateComplex()` en `MathJsEngine.cs`

### 3.4 Normalizacion del origen (From)

Existen tres mecanismos que pueden cambiar la unidad mostrada en el lado "From":

1. **Auto-simplificacion SI de math.js**: para unidades SI con coeficiente < 1, math.js reescribe al prefijo mas
   conveniente (`0.001 V` -> `1 mV`). Solo ocurre hacia abajo; `1000 m` permanece como `1000 m`. Las imperiales nunca se
   simplifican. Cuando ocurre, `FromWasNormalized = true` y se muestran tres celdas.
2. **Forzado de unidad para compuestos**: para unidades compuestas (con `/`), se fuerza la unidad del usuario para
   evitar que math.js auto-simplifique a una unidad custom registrada (ej. `kmh` o `mph`).
3. **Intercept de normalizacion natural (tiempo/datos)**: se fuerza la unidad original con `to origUnit` para prevenir
   la auto-simplificacion SI. `FromWasNormalized` siempre es `false` para estas unidades.

> **Verificar en:** `EvaluateSimple()` y `EvaluateComplex()` en `MathJsEngine.cs`; `TryNormalize()` en `MathJsEngine.cs`

---

## 3b. Álgebra simbólica

Cuando la query no contiene `=` y math.js no puede evaluarla (contiene variables como `x`, `y`, `t`), la query se redirige al motor nerdamer para álgebra simbólica.

El resultado es un ítem navegable con hasta N celdas, una por operación útil. Las celdas se navegan con ←/→; Enter copia el resultado de la celda seleccionada y lo pega en la app anterior.

| Operación  | Cuándo aparece                          | Ejemplo entrada  | Ejemplo celda           |
|------------|-----------------------------------------|------------------|-------------------------|
| simplify   | Resultado ≠ input normalizado           | `2*x+3*x`        | `5*x`                   |
| expand     | Resultado ≠ input normalizado           | `(x+1)^2`        | `x^2+2*x+1`             |
| factor     | Resultado ≠ input normalizado           | `x^2-5*x+6`      | `(x-2)*(x-3)`           |
| d/dx       | Siempre (una celda por variable, a–z)   | `x^2`            | `2*x`                   |
| ∫dx        | Solo si la expresión tiene 1 variable   | `x^2`            | `x^3/3`                 |

Las celdas donde el resultado es igual al input se descartan (no-ops). Las celdas con resultado duplicado se deduplicarán, conservando la primera (prioridad: simplify > expand > factor > d/dx > ∫dx).

Si tras el filtrado no queda ninguna celda útil, no se muestra ningún resultado.

El texto plano sin estructura matemática (`safari to km`) no produce resultado: nerdamer no detecta variables y devuelve `null`.

**Longitud mínima**: el modo álgebra solo se activa si la query tiene al menos `AppDefaults.AlgebraMinQueryLength` caracteres (3). Esto descarta falsos positivos como `1p`, `2x` o `ax`, donde nerdamer aceptaría la variable de una sola letra pero el usuario casi seguro no está pidiendo álgebra. Las ecuaciones con `=` (ruta `TrySolve`) no aplican este filtro.

**Score**: el resultado de álgebra simbólica usa `AppDefaults.AlgebraResultScore` (4.01), no el 7 fijo de los demás modos de calculadora. Queda justo por encima de una app con prefijo exacto (4.0) y por debajo del match exacto de nombre de app (4.4); con un solo lanzamiento de la app el bonus de `LaunchHistory` (~+0.35) la empuja por encima del álgebra. Esto permite que apps usadas con frecuencia ganen sobre álgebra ambigua sin sacrificar la respuesta cuando la query es claramente algebraica. Los resultados numéricos, conversiones y ecuaciones siguen con score 7.

> **Verificar en:** `getAlgebraResults()` en `nerdamer-helpers.js`; `NerdamerEngine.TryAlgebra()` en `NerdamerEngine.cs`; routing `UnknownSymbol` y `BuildAlgebraResult()` en `CalculatorSearch.cs`; constantes `AlgebraMinQueryLength` y `AlgebraResultScore` en `AppDefaults.cs`; decimal rounding via `_ALGEBRA_DECIMALS` in `nerdamer-helpers.js`

---

## 4. Normalizacion natural (tiempo y datos)

Cuando el usuario escribe un valor con una unidad de tiempo o de datos sin destino explicito, el sistema intenta
presentar el resultado en una forma mas legible antes de recurrir a la conversion por defecto.

### 4.1 Modos

| Modo                         | Aplica a | Comportamiento                                     | Ejemplo                         |
|------------------------------|----------|----------------------------------------------------|---------------------------------|
| Descomposicion (por defecto) | Tiempo   | Descompone en hasta 3 componentes de mayor a menor | `38000 s` -> `10 h 33 min 20 s` |
| Mejor unidad (`best_unit`)   | Datos    | Encuentra la unidad mas alta donde el valor >= 1   | `1500 MB` -> `1.5 GB`           |

### 4.2 Cuando la normalizacion es "interesante"

El resultado de la normalizacion solo se usa si es diferente de la entrada. Si el resultado es trivial (misma unidad,
mismo valor), se descarta y se aplica la conversion por defecto normal (`defaultTargets`).

Las unidades normalizables incluyen tanto las listadas explicitamente en la cadena (ej. `s`, `h`, `MB`) como cualquier
unidad dimensionalmente compatible (ej. `Ms`, `ks`, `PB`, `EB`), salvo aquellas que ya tienen un `defaultTarget`
explicito (ej. `decade` -> `year`, `week` -> `day`).

> **Verificar en:** `TryNormalize()`, `ComputeNormalization()`, `IsNormalizableUnit()` en `MathJsEngine.cs`;
`computeNormalization()`, `isNormalizableUnit()` en `mathjs-helpers.js`; `normalizeUnits` en `unit-config.json`

---

## 5. Resolucion de unidades y case-insensitivity

math.js es case-sensitive: `kg` y `KG` son tokens distintos. Para que el usuario pueda escribir en cualquier case, el
sistema mantiene un pipeline de resolucion que se aplica sobre cada token del AST.

### 5.1 Orden de resolucion de un token

| Paso                                    | Fuente                                     | Ejemplo                                          |
|-----------------------------------------|--------------------------------------------|--------------------------------------------------|
| 1. Token bloqueado                      | `blocked` en `unit-config.json`            | `li`, `BTU` -- descartados                       |
| 2. Override exacto (case-sensitive)     | `tokenAliases` en `unit-config.json`       | `c` -> `degC`, `C` -> `degC`, `F` -> `degF`      |
| 3. Override lowercase (solo multi-char) | `tokenAliases` en `unit-config.json`       | `HOUR` -> `h`, `CELSIUS` -> `degC`               |
| 4. Sinonimos (mismo longName)           | Datos precomputados                        | `l` y `L` -> ambos "litre" -> `L` sin ambiguedad |
| 5. Ya canonico                          | Registry de math.js                        | Token exacto reconocido -> sin cambio            |
| 6. Override de ambiguedad               | `ambiguityOverrides` en `unit-config.json` | `pa` -> `Pa` (pascal, no petaamperio)            |
| 7. Override forzado con aviso           | `forceAmbiguous` en `unit-config.json`     | `mS` -> `ms` con aviso de ambiguedad             |
| 8. Verdaderamente ambiguo               | Multiples candidatos sin override          | Primer candidato + hint al usuario               |
| 9. Token no ambiguo                     | `mathjs-precomputed.json`                  | `KG` -> `kg` via mapa lowercase                  |

Los tokens de un solo caracter se excluyen del paso 3 para preservar la distincion case-sensitive.

> **Verificar en:** `resolveUnitToken()` en `mathjs-helpers.js`

### 5.2 Aliases de entrada con caracteres especiales

Antes de que la expresion llegue al parser de math.js, se aplican reemplazos de texto para caracteres especiales que el
AST no maneja:

| Alias      | Canonico |
|------------|----------|
| `°c`, `oc` | `degC`   |
| `°f`, `of` | `degF`   |

> **Verificar en:** `inputAliases` en `unit-config.json`; `NormalizeExpressionCore()` en `MathJsEngine.cs`

### 5.3 Aliases eval-safe

Algunos simbolos de unidad colisionan con funciones de math.js. Para evitar conflictos, se sustituyen en el AST antes de
evaluar y se restauran en el display:

| El usuario escribe | Se evalua como | Se muestra como |
|--------------------|----------------|-----------------|
| `min`              | `minute`       | `min`           |

> **Verificar en:** `evalSafeAliases` y `displayNames` en `unit-config.json`

---

## 6. Nombres de display y nombres largos

### 6.1 Nombre de display (forma corta)

Los nombres de display transforman simbolos internos en formas legibles: `degC` -> `°C`, `degF` -> `°F`, `minute` ->
`min`. Para unidades compuestas, se aplica el lookup a cada componente: `mi / minute` -> `mi/min`.

> **Verificar en:** `DisplayUnit()` en `MathJsEngine.cs`; `displayNames` en `unit-config.json`

### 6.2 Nombre largo

Los nombres largos se resuelven en este orden:

1. **Override explicito** en `longNames` de `unit-config.json` (ej. `h` -> `hour`, `degC` -> `celsius`).
2. **Derivacion para compuestos**: si la unidad contiene ` / `, se construye automaticamente a partir de los
   componentes (`km / h` -> `kilometer per hour`).
3. **Derivacion automatica** desde el registry de math.js via prefijos LONG (ej. `km` -> `kilometer`).

Las entradas en `longNames` donde clave y valor son iguales (ej. `"day": "day"`) sirven para habilitar la
pluralizacion (`10 days`). Los mappings inversos se generan automaticamente (ej. `longNames["h"] = "hour"` genera
`_unitOverrides["hour"] = "h"`).

### 6.3 Pluralizacion

La pluralizacion se aplica sobre los nombres largos segun el valor numerico:

| Regla                                             | Ejemplo                  |
|---------------------------------------------------|--------------------------|
| Valor absoluto == 1 -> singular                   | `1 meter`                |
| Irregulares: `foot` -> `feet`, `inch` -> `inches` | `10 feet`                |
| Invariantes: terminados en `hertz`                | `10 kilohertz`           |
| Compuestos `X per Y`: solo se pluraliza X         | `10 kilometers per hour` |
| Terminados en `s` o `heit`: invariantes           | `10 fahrenheit`          |
| Resto: se anade `s`                               | `10 meters`              |

> **Verificar en:** `GetUnitLongName()` y `GetComponentLongName()` en `MathJsEngine.cs`; `getUnitLongName()` y
`getExplicitLongName()` en `mathjs-helpers.js`; `UnitPluralizer` en `Yottacast.Core/Search/Calculator/UnitPluralizer.cs`

---

## 7. Unidades custom

Al arrancar, el motor registra unidades que math.js no incluye por defecto:

| Categoria      | Unidades                              | Definicion                      |
|----------------|---------------------------------------|---------------------------------|
| Velocidad      | `kmh`, `mph`                          | En la dimension `m/s`           |
| Rotacion       | `rpm`                                 | En la dimension `1/s`           |
| Tasas de datos | `bps`, `kbps`, `Mbps`, `Gbps`, `Tbps` | Cadena de `1000x` sobre `bit/s` |

> **Verificar en:** primeras lineas de `mathjs-helpers.js`

---

## 8. Divisas

Las tasas de cambio se descargan de la API publica fawazahmed0 (200+ monedas, actualizacion diaria) y se cachean en
disco en `AppPaths.ExchangeRatesCache`. El motor se construye con todas las tasas activas precargadas; no se inyectan
divisas durante la vida del engine. Cuando cambia el conjunto de tasas activas, `MathJsEngineProvider` crea un nuevo
engine en background y hace un swap atomico con `Interlocked.Exchange`, de modo que las busquedas en curso siguen
sirviendo el engine anterior hasta que el nuevo esta listo (~2 s).

Las divisas se clasifican en tres categorias por `CurrencyClassifier`:

| Categoria | Ejemplos            | Incluida por defecto |
|-----------|---------------------|----------------------|
| Forex     | USD, EUR, GBP, JPY  | Siempre              |
| Metales   | XAU, XAG, XPT, XPD | Si (configurable)    |
| Cripto    | Todo lo demas       | No (configurable)    |

Los toggles "Include metals" e "Include cryptocurrencies" de Settings → Calculator filtran las tasas antes de recrear el
engine. Al cambiar un toggle, `ExchangeRateService.NotifySettingsChanged()` recalcula `ActiveRates` y dispara el evento
`RatesUpdated`, que a su vez lanza la recreacion del engine.

Al escribir solo una divisa (ej. `10 USD`), se convierte al otro miembro del par de divisas configurado. El par por
defecto es EUR/USD y se puede cambiar en Settings → Calculator. La divisa "home" (izquierda del par) es el destino de
cualquier divisa desconocida; la divisa "home" convierte a la de la derecha. El par se aplica en caliente sin reiniciar
la app.

Las tasas son relativas a USD: `EUR = 0.92` significa `1 USD = 0.92 EUR`.

Cuando las tasas no se han podido descargar o estan desactualizadas, el resultado de conversion muestra un aviso
"Exchange rates may be outdated" debajo del resultado (`RatesAreStale = true` en `ConversionResultItemViewModel`).

> **Verificar en:** `ExchangeRateService` en `Yottacast.Core/Search/Calculator/ExchangeRateService.cs`;
`MathJsEngineProvider` en `Yottacast.Core/Search/Calculator/MathJsEngineProvider.cs`;
`CurrencyClassifier` en `Yottacast.Core/Search/Calculator/CurrencyClassifier.cs`;
`PreloadAllCurrencies()` e `Initialize()` en `MathJsEngine.cs`; `registerCurrency()` en `mathjs-helpers.js`

---

## 9. Formateo de resultados

Los resultados numericos se formatean con precision adaptativa:

| Tipo de numero      | Regla por defecto       | Ejemplo                               |
|---------------------|-------------------------|---------------------------------------|
| Entero              | Sin cambio              | `600 min`                             |
| Valor absoluto >= 1 | 2 decimales (configurable) | `6.213711922 mi` -> `6.21 mi`         |
| Valor absoluto < 1  | 3 cifras significativas    | `0.001450377377 psi` -> `0.00145 psi` |

El numero de decimales para valores >= 1 se puede cambiar en Settings → Calculator (rango 0-6) y se aplica en caliente.
La precision base (parametro `BasePrecision` de `FormatConfig`, 10 cifras significativas por defecto) y las cifras
significativas para valores < 1 (`SmallNumberSigFigs`, 3 por defecto) son parametros de `FormatConfig` que la UI no
expone: `App.axaml.cs > BuildFormatConfig` solo rellena `LargeNumberDecimals` (de `CalculatorDecimalPlaces`) y el par de
divisas, dejando el resto en sus defaults.

> **Estado: incompleto** - el hot-reload de formato (`MathJsEngine.UpdateConfig`) solo reaplica `_FMT_LARGE_DECIMALS` y
> `_defaultCurrencyPair`. Ignora `SmallNumberSigFigs` y `BasePrecision`: estos solo se aplican al construir el motor
> (`MathJsEngine` constructor, lineas que hacen `SetValue("_FMT_SMALL_SIG_FIGS"/"_FMT_BASE_PRECISION")`). Cambiarlos en
> caliente requeriria recrear el motor (`MathJsEngineProvider.RecreateAsync`), no `UpdateConfig`.

Los resultados algebraicos (nerdamer) también respetan los decimales configurados mediante `roundLongDecimals()` en `nerdamer-helpers.js`.

> **Verificar en:** `smartFormat()` en `mathjs-helpers.js`; `FormatConfig` y `UpdateConfig()` en `MathJsEngine.cs`;
> `BuildFormatConfig` en `App.axaml.cs`

---

## 10. Manejo de errores

Los errores de evaluacion se clasifican en:

| Tipo                | Comportamiento en la UI                                  |
|---------------------|----------------------------------------------------------|
| `IncompatibleUnitsConvert` | Hint "Can't convert X to Y" con nombres largos de unidad |
| `IncompatibleUnitsOp`     | Hint "Units do not match" (operacion aritmetica)         |
| `UnknownSymbol`     | Se descarta silenciosamente (evita ruido en texto plano) |
| `Syntax`            | Sin resultado                                            |
| `Other`             | Sin resultado                                            |

El motivo de descartar `UnknownSymbol` es que cualquier texto no matematico (`safari to km`) genera este error, y
mostrarlo seria contraproducente. `BuildErrorHint` soporta ambos tipos por si en el futuro la UI quisiera exponer el
aviso condicionalmente.

> **Verificar en:** `classifyError()` en `mathjs-helpers.js`; `CalculatorSearch.Search()` en `CalculatorSearch.cs`

---

## 11. Configuracion: unit-config.json

El fichero `unit-config.json` es un embedded resource que centraliza la configuracion manual del sistema de unidades. C#
solo deserializa `inputAliases` y `displayNames`; el resto se reenvia integro al motor JS.

| Campo                | Proposito                                                                |
|----------------------|--------------------------------------------------------------------------|
| `inputAliases`       | Reemplazos de texto antes del parseo (caracteres especiales)             |
| `tokenAliases`       | Overrides de tokens en el AST (case, plurales, aliases)                  |
| `evalSafeAliases`    | Sustituciones para evitar colisiones con funciones de math.js            |
| `displayNames`       | Nombres de display para la forma corta del resultado                     |
| `longNames`          | Nombres largos explicitos + generacion automatica de reversos            |
| `defaultTargets`     | Destinos por defecto para conversion implicita (prioridad maxima)        |
| `defaultPairs`       | Pares dimensionales de fallback                                          |
| `forceAmbiguous`     | Canonicos que deben resolverse a otro simbolo con aviso (case-sensitive) |
| `ambiguityOverrides` | Preferencia de resolucion para tokens ambiguos (lowercase)               |
| `normalizeUnits`     | Unidades que activan la normalizacion natural (tiempo/datos)             |
| `blocked`            | Tokens excluidos de la resolucion de ambiguedades                        |

> **Verificar en:** `Yottacast.Core/Search/Calculator/unit-config.json`; `loadAliasData()` en `mathjs-helpers.js`;
> record `UnitConfig` en `MathJsEngine.cs`

---

## 12. Datos precomputados

`mathjs-precomputed.json` es un embedded resource generado automaticamente que contiene:

- `symbols`: lista de todos los tokens canonicos del registry de math.js.
- `ambiguous`: mapa `lowercase -> [{symbol, longName}]` para tokens con multiples formas canonicas.
- `functionNames`: mapa `lowercase -> canonical` de funciones de math.js.
- `longToShort`: mapa de nombres largos a simbolos cortos.

Sin este recurso, el sistema de normalizacion de case no funciona. Se construye a partir de `mathjs-precompute.js`.

> **Verificar en:** `Yottacast.Core/Search/Calculator/mathjs-precomputed.json`; `loadPrecomputedData()` en
`mathjs-helpers.js`; `Yottacast.Core/Search/Calculator/mathjs-precompute.js`

---

## 13. Snapshot de regresion y actualizacion de math.js

### 13.1 Proteccion contra regresiones

Existe un test (`GeneratedFiles_MatchCommittedBaseline`) que regenera los archivos precomputados en memoria y los
compara con los comprometidos en el repositorio. Si difieren, falla con un diff detallado indicando unidades nuevas,
eliminadas, y tokens ambiguos nuevos o resueltos.

La fixture del test usa un provider de divisas vacio para no contaminar el snapshot con divisas registradas por otros
tests.

### 13.2 Workflow al actualizar math.js

1. Cambiar la URL de descarga en el `.csproj` a la nueva version.
2. Borrar `math.min.js` (se redescarga en el siguiente build).
3. `dotnet build`
4. Borrar `mathjs-unit-snapshot.json` y `mathjs-precomputed.json`.
5. `dotnet test --filter GeneratedFiles_MatchCommittedBaseline` (los regenera).
6. `dotnet build` (re-embebe el nuevo `mathjs-precomputed.json`).
7. `dotnet test` (verificacion completa).

### 13.3 Incompatibilidad conocida con Jint

Versiones recientes de math.js lanzan "Assignment to constant variable" dentro de Jint 3.x. La version embebida esta
fijada (11.12.0). Si se actualiza Jint a una version con soporte ES2022+, se puede probar una version mas reciente de
math.js.

### 13.4 Riesgo del EmbeddedResource condicional

El recurso `math.min.js` tiene `Condition="Exists(...)"` en el `.csproj`. Si `curl` falla en el build, la compilacion
termina correctamente pero el recurso no queda embebido. En ese caso la app lanza `InvalidOperationException` en
runtime.

> **Verificar en:** `MathJsGeneratedFilesTests` en `Yottacast.Core.Tests/Search/MathJsUnitSnapshotTests.cs`; target
`DownloadMathJs` en `Yottacast.Core/Yottacast.Core.csproj`

---

## 14. Thread safety y Dispose

- El acceso al motor esta protegido por un `lock` en cada llamada a `Evaluate()` y `NormalizeExpression()`. Es seguro
  llamarlo desde multiples hilos.
- `Evaluate()` comprueba `_engine == null` antes y despues de adquirir el lock (double-checked null guard): la
  comprobacion exterior evita contension cuando el engine aun no esta listo; la interior garantiza corrección ante una
  carrera con `Dispose()`.
- `Dispose()` espera la finalizacion de la tarea de inicializacion, adquiere el lock, dispone el engine y pone
  `_engine = null`. Esto garantiza que no haya evaluaciones en curso cuando se libera el engine.

> **Verificar en:** `Evaluate()`, `NormalizeExpression()`, `Dispose()` en `MathJsEngine.cs`

---

## 15. Portapapeles

El modulo Core no depende de Avalonia. `ClipboardService` es un bridge que se inicializa en `App.axaml.cs` con un
delegate que despacha la operacion al hilo UI. Antes de la inicializacion, las copias se descartan silenciosamente (en
la practica nunca ocurre porque la UI no es interactiva hasta que las fuentes estan listas).

En tests, se inicializa con un delegate de captura, sin necesidad de Avalonia.

> **Verificar en:** `ClipboardService` en `Yottacast.Core/Services/ClipboardService.cs`

---

## 16. Tests

Los tests usan `MathJsEngineFixture` (coleccion `"MathJs"`) para compartir una sola instancia del engine, y
`MathJsSnapshotFixture` (coleccion `"MathJsSnapshot"`) para tests de snapshot con provider de divisas vacio.

| Clase de test                         | Cobertura                                                    |
|---------------------------------------|--------------------------------------------------------------|
| `CalculatorSearchTests.cs`            | Aritmetica y funciones                                       |
| `UnitConverterSearchTests.cs`         | Conversiones de unidades                                     |
| `DefaultConversionTests.cs`           | Conversiones por defecto y nombres largos                    |
| `DefaultConversionTestsFormatting.cs` | Formateo de resultados de conversion                         |
| `ClassifyErrorTests.cs`               | Clasificacion de errores                                     |
| `NormalizeExpressionTests.cs`         | Normalizacion de expresiones y deteccion de kinds            |
| `CurrencyRateUpdateTests.cs`          | Engine con tasas distintas produce resultados distintos       |
| `CurrencyClassifierTests.cs`          | Clasificacion de divisas en Forex / Metal / Crypto           |
| `MathJsEngineProviderTests.cs`        | Ciclo de vida del provider: null inicial, swap, Dispose      |
| `MathJsUnitSnapshotTests.cs`          | Snapshot de regresion y casing de unidades                   |
| `EquationSolverTests.cs`              | Resolución de ecuaciones (NerdamerEngine.TrySolve + CalculatorSearch integración) |

> **Verificar en:** `Yottacast.Core.Tests/Search/Calculator/` y `Yottacast.Core.Tests/Search/MathJsUnitSnapshotTests.cs`
