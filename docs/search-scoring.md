# Scoring de resultados

Este documento describe como Yottacast decide que resultados mostrar al usuario, en que orden, y cual queda seleccionado por defecto. La fuente de verdad es el codigo; este documento describe el comportamiento esperado y sus invariantes.

---

## 1. Principio general: el resultado mas relevante siempre arriba

Cuando el usuario escribe en la barra de busqueda, Yottacast consulta multiples fuentes (aplicaciones, emojis, calculadora, conversiones, busqueda web, archivos). Cada fuente asigna un **score numerico** a sus resultados. El sistema mezcla todos los resultados y los ordena por score descendente. El usuario siempre ve primero lo mas relevante sin importar de que fuente proviene.

**Invariantes:**
- Los resultados siempre aparecen ordenados de mayor a menor score.
- Cada fuente devuelve como maximo 10 resultados (`SearchSourceLimit`).
- Tras mezclar, la lista visible contiene como maximo 10 elementos.
- Si hay un resultado de calculadora o conversor y el usuario no ha navegado con las teclas, ese resultado queda seleccionado automaticamente, independientemente de su posicion en la lista.

> **Verificar en:** `GlobalSearch.SearchInstant()`, `GlobalSearch.SearchSourcesAsync()`, `MainWindowViewModel.RefreshResults()`

---

## 2. Coincidencia de texto: como se decide si un nombre "matchea" una query

El motor de coincidencia de texto (`NameMatcher`) es compartido por la busqueda de aplicaciones y la de emojis. Dada una query del usuario y un nombre (de app o emoji), evalua cuatro estrategias en orden de prioridad y devuelve el score de la primera que tenga exito:

| Score | Estrategia | Comportamiento | Ejemplo |
|-------|-----------|----------------|---------|
| 1.0 | CamelHump desde el inicio | Cada fragmento de la query es prefijo de un token consecutivo del nombre, comenzando desde el primer token | "Saf" -> "Safari", "ActMon" -> "Activity Monitor" |
| 0.8 | CamelHump desde token interior | Igual que el anterior, pero comenzando en un token posterior al primero | "Mon" -> "Activity Monitor" (matchea desde "Monitor") |
| 0.6 | Iniciales | Las iniciales de los tokens del nombre comienzan por la query | "AM" -> "Activity Monitor", "MON" -> "Microsoft OneNote" |
| 0.4 | Abreviatura multi-palabra | Los caracteres de la query se distribuyen como prefijos de tokens consecutivos, consumiendo uno o mas caracteres por token. Solo se activa cuando el nombre tiene mas de un token. Puede comenzar desde cualquier token | "smifa" -> "smiling face" ("smi" + "fa") |
| 0.2 | Substring interno | El nombre contiene la query como subcadena. Solo para queries de 3+ caracteres | "ari" -> "Safari" |
| 0.0 | Sin coincidencia | Ninguna estrategia encontro match | |

**Invariantes:**
- "af" nunca coincide con "Safari" (no es prefijo de ningun token).
- Si la query es toda minusculas y no alcanzo score 1.0, se reintenta automaticamente en mayusculas para que "am" coincida con "Activity Monitor" por iniciales.
- La tokenizacion divide por espacios, guiones y guiones bajos, y luego aplica CamelCase (transicion minuscula->mayuscula). Las palabras en mayusculas puras se tratan como tokens de un caracter: "AM" -> ["A", "M"].

> **Verificar en:** `Search/Application/NameMatcher.cs` (metodos `Score`, `ScoreWith`, `SplitTokens`), `Yottacast.Core.Tests/Search/NameMatcherTests.cs`

---

## 3. Busqueda de aplicaciones

Las aplicaciones instaladas se mantienen en cache en memoria. Cada resultado recibe directamente el score de `NameMatcher` (rango 0.0 a 1.0). La tokenizacion del nombre de la app se recalcula en cada busqueda (los nombres son cortos y el coste es despreciable).

**Invariantes:**
- Solo se devuelven apps con score > 0.
- El score maximo de una app es 1.0 (match CamelHump desde el inicio).

> **Verificar en:** `Search/Application/ApplicationSearch.cs` (metodo `Search`)

---

## 4. Busqueda de emojis

Los emojis se activan cuando la query comienza con `:`. El sistema aplica un scoring en dos capas sobre `NameMatcher`:

| Score | Condicion |
|-------|-----------|
| 3.5 | Score de la grilla de emojis como item en la lista global (fijo) |
| 3.0 | Nombre del emoji coincide exactamente con el termino (case-insensitive) |
| 1.2 a 2.0 | El nombre tiene match en NameMatcher (score > 0): se devuelve `nameScore + 1` |
| 0.0 a 1.0 | El nombre no matchea pero algun keyword si: se devuelve el maximo score de NameMatcher sobre los keywords |

**Invariantes:**
- "fire" siempre aparece antes que "fireworks" cuando el termino es "fire", porque el match exacto (3.0) supera al match por prefijo (2.0).
- Cuando la query es solo `:` (sin texto), se muestran todos los emojis ordenados por `SortOrder` ascendente (orden Unicode), sin aplicar scoring. El viewport del grid limita las filas visibles simultaneamente.
- Los tokens del nombre del emoji se pre-computan al cargar los datos (tokenizacion simple por espacios, sin CamelCase), evitando re-tokenizar en cada keystroke.
- Activar un emoji copia el caracter al portapapeles.

> **Verificar en:** `Search/Emoji/EmojiSearch.cs` (metodos `MatchScore`, `FilterEmojis`, `GetDefaultEmojis`), `Search/Emoji/EmojiDataLoader.cs` (propiedad `NameTokens` de `EmojiEntry`)

---

## 5. Calculadora y conversor de unidades

La calculadora evalua expresiones matematicas y conversiones de unidades. Los resultados de calculadora y conversor tienen un score fijo de **4**, que es el mas alto de todas las fuentes.

**Invariantes:**
- Si la expresion produce un resultado valido y distinto de la query original, siempre aparece un resultado.
- El resultado de calculadora/conversor siempre queda seleccionado automaticamente si el usuario no ha navegado manualmente.
- No se muestra resultado si la evaluacion devuelve el mismo texto que la query (evita "5" -> "5").
- Activar el resultado copia el valor al portapapeles.
- Si hay un error de unidades incompatibles, se muestra un hint informativo en lugar de un resultado.

> **Verificar en:** `Search/Calculator/CalculatorSearch.cs` (metodo `Search`), `MainWindowViewModel.RefreshResults()` (seleccion automatica)

---

## 6. Busqueda web

La busqueda web genera items para abrir la query en un motor de busqueda del navegador. Soporta dos modos por motor:

| Modo | Activacion | Score |
|------|-----------|-------|
| ShowAlways | Se muestra con cualquier query (excepto si hay un PrefixOnly activo) | 3.0 |
| PrefixOnly | Se activa solo con el prefijo configurado (ej. "g texto") | 3.5 |

**Invariantes:**
- Cuando un motor PrefixOnly coincide, los motores ShowAlways se ocultan para no saturar la lista.
- La busqueda web no se activa con queries vacias ni con queries que empiecen por `:` (modo emoji).
- Activar el resultado abre la URL en el navegador configurado por el usuario.

> **Verificar en:** `Search/WebSearch/WebSearchSource.cs` (metodo `Search`)

---

## 7. Diccionario

La busqueda de diccionario es una fuente diferida. Se activa en dos modos:

| Modo | Activacion | Score |
|------|-----------|-------|
| PrefixOnly (default) | Solo con el prefijo configurado (ej. "define hello") | 3.5 |
| ShowAlways | Con cualquier query no vacia (sin modo emoji) | 2.5 |

En modo ShowAlways, el score (2.5) es inferior al de web search (3.0) para que las definiciones no dominen sobre los resultados de busqueda web. Los resultados de diccionario siempre tienen `BypassLimit = true` y no estan sujetos al limite global.

> **Verificar en:** `Search/Dictionary/DictionarySource.cs` (metodo `SearchAsync`, scores en lineas 65/72)

---

## 8. Busqueda de archivos

La busqueda de archivos es una fuente diferida (accede a disco). Se activa tras un debounce de 250 ms y solo para queries de 2+ caracteres. Produce snapshots progresivos cada 200 ms mientras los resultados llegan.

| Score | Condicion |
|-------|-----------|
| 1.0 | El nombre del archivo (sin extension) coincide exactamente con la query y tiene extension propia |
| 0.9 | La extension del archivo coincide con la query (ej. query "png" -> archivos .png) |
| 0.85 | El nombre completo coincide exactamente con la query (ej. carpetas sin extension) |
| 0.75 | El nombre comienza por la query / en multi-token, todos los tokens son prefijo de algun segmento del nombre |
| 0.5 | El nombre termina con la query / score por defecto para matches basicos |

**Invariantes:**
- Nunca se buscan archivos con queries de 1 caracter.
- La busqueda tiene un timeout de 20 segundos.
- Los resultados se refinan progresivamente: el usuario ve mejores resultados a medida que llegan.
- Para queries multi-token, todos los tokens deben estar presentes en el nombre del archivo.

> **Verificar en:** `Search/UserDocuments/UserDocumentSearch.cs` (metodo `SearchAsync`), `AppDefaults.cs` (constantes `FileSearchMinQueryLength`, `FileSearchTimeoutMs`, `SearchDebouncedMs`, `FileSearchSnapshotIntervalMs`)

---

## 9. Jerarquia de scores entre fuentes

La siguiente tabla resume los rangos de score por fuente, de mayor a menor prioridad:

| Score | Fuente | Descripcion |
|-------|--------|-------------|
| 4.0 | Calculadora / Conversor | Score fijo. Siempre domina la lista |
| 3.5 | Emoji (grilla) | Score fijo de la grilla como item global |
| 3.5 | Busqueda web (PrefixOnly) | Cuando el usuario uso un prefijo explicito |
| 3.5 | Diccionario (PrefixOnly) | Cuando el usuario uso el prefijo (ej. "define") |
| 3.0 | Busqueda web (ShowAlways) | Cuando no hay PrefixOnly activo |
| 2.5 | Diccionario (ShowAlways) | Cuando no hay prefijo, inferior a web search |
| 0.0 - 1.0 | Aplicaciones | Segun NameMatcher |
| 0.0 - 1.0 | System Settings (macOS) | Segun NameMatcher |
| 0.0 - 1.0 | Archivos | Segun relevancia del nombre |

**Invariantes:**
- Un resultado de calculadora siempre aparece por encima de cualquier app o archivo.
- La busqueda web siempre aparece por encima de apps y archivos, pero por debajo de la calculadora.
- Los emojis (como grilla) aparecen al mismo nivel que busqueda web PrefixOnly (3.5).

> **Verificar en:** `CalculatorSearch.cs` (score 4), `EmojiSearch.cs` (score 3.5), `WebSearchSource.cs` (scores 3.0 y 3.5), `DictionarySource.cs` (scores 2.5 y 3.5), `ApplicationSearch.cs` (score de NameMatcher), `SystemSettingsSearch.cs` (score de NameMatcher), `UserDocumentSearch.cs` (scores 0.5-1.0)

---

## 10. Flujo de busqueda en dos fases

La busqueda se ejecuta en dos fases para dar respuesta inmediata al usuario:

1. **Fase instantanea** (sin delay): consulta fuentes en memoria (aplicaciones, emojis, calculadora, busqueda web). Los resultados aparecen al instante.
2. **Fase diferida** (tras 250 ms de debounce): consulta fuentes de disco (archivos). Los resultados se van intercalando con los instantaneos a medida que llegan.

En modo emoji (query empieza por `:`), la fase diferida se omite por completo.

**Invariantes:**
- Cada cambio de texto cancela la busqueda anterior.
- Los resultados instantaneos siempre aparecen sin delay.
- Los resultados diferidos nunca desplazan a los instantaneos de mayor score.
- Mientras la fase diferida esta en curso, se muestra un indicador de busqueda activa.

> **Verificar en:** `MainWindowViewModel.SearchAsync()`, `GlobalSearch.cs` (metodos `SearchInstant` y `SearchDeferredAsync`)
