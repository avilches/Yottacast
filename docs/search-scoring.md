# Scoring de resultados

Este documento describe como Yottacast decide que resultados mostrar al usuario, en que orden, y cual queda seleccionado por defecto. La fuente de verdad es el codigo; este documento describe el comportamiento esperado y sus invariantes.

---

## 1. Principio general: el resultado mas relevante siempre arriba

Cuando el usuario escribe en la barra de busqueda, Yottacast consulta multiples fuentes (aplicaciones, emojis, calculadora, conversiones, busqueda web, archivos). Cada fuente asigna un **score numerico** a sus resultados. El sistema mezcla todos los resultados y los ordena por score descendente. El usuario siempre ve primero lo mas relevante sin importar de que fuente proviene.

El score final de cada item es `score_base + bonus_de_uso`, donde el bonus lo aporta `LaunchHistory` en funcion de cuantas veces y con que recencia se ha activado ese item.

**Invariantes:**
- Los resultados siempre aparecen ordenados de mayor a menor score (base + bonus).
- Cada fuente devuelve como maximo 10 resultados (`SearchSourceLimit`).
- Tras mezclar, la lista visible contiene como maximo 10 elementos.
- Si hay un resultado de calculadora o conversor y el usuario no ha navegado con las teclas, ese resultado queda seleccionado automaticamente, independientemente de su posicion en la lista.

> **Verificar en:** `GlobalSearch.SearchInstant()`, `GlobalSearch.SearchSourcesAsync()`, `MainWindowViewModel.RefreshResults()`

---

## 2. Coincidencia de texto: como se decide si un nombre "matchea" una query

El motor de coincidencia de texto (`NameMatcher`) es compartido por la busqueda de aplicaciones y la de emojis. Dada una query del usuario y un nombre (de app o emoji), evalua cuatro estrategias en orden de prioridad y devuelve el score de la primera que tenga exito:

| Score raw | Estrategia | Comportamiento | Ejemplo |
|-----------|-----------|----------------|---------|
| 1.1 | Coincidencia exacta | El nombre completo coincide exactamente con la query (case-insensitive). Es la señal mas fuerte posible | "pycharm" -> "PyCharm", "safari" -> "Safari" |
| 1.0 | CamelHump desde el inicio | Cada fragmento de la query es prefijo de un token consecutivo del nombre, comenzando desde el primer token | "Saf" -> "Safari", "ActMon" -> "Activity Monitor" |
| 0.8 | CamelHump desde token interior | Igual que el anterior, pero comenzando en un token posterior al primero | "Mon" -> "Activity Monitor" (matchea desde "Monitor") |
| 0.6 | Iniciales | Las iniciales de los tokens del nombre comienzan por la query | "AM" -> "Activity Monitor", "MON" -> "Microsoft OneNote" |
| 0.4 | Abreviatura multi-palabra | Los caracteres de la query se distribuyen como prefijos de tokens consecutivos, consumiendo uno o mas caracteres por token. Solo se activa cuando el nombre tiene mas de un token. Puede comenzar desde cualquier token | "smifa" -> "smiling face" ("smi" + "fa"), "pyc" -> "PyCharm" |
| 0.2 | Substring interno | El nombre contiene la query como subcadena. Solo para queries de 3+ caracteres | "ari" -> "Safari" |
| 0.0 | Sin coincidencia | Ninguna estrategia encontro match | |

Estos valores son los scores **internos** de `NameMatcher`. Las fuentes que los usan (ApplicationSearch, SystemSettingsSearch) los multiplican por 4 antes de exponerlos al sistema global (ver seccion 3).

**Invariantes:**
- "af" nunca coincide con "Safari" (no es prefijo de ningun token).
- Si la query es toda minusculas y no alcanzo score 1.0, se reintenta automaticamente en mayusculas para que "am" coincida con "Activity Monitor" por iniciales.
- La tokenizacion divide por espacios, guiones y guiones bajos, y luego aplica CamelCase (transicion minuscula->mayuscula). Las palabras en mayusculas puras se tratan como tokens de un caracter: "AM" -> ["A", "M"].

> **Verificar en:** `Search/Application/NameMatcher.cs` (metodos `Score`, `ScoreWith`, `SplitTokens`), `Yottacast.Core.Tests/Search/NameMatcherTests.cs`

---

## 3. Busqueda de aplicaciones

Las aplicaciones instaladas se mantienen en cache en memoria. El score de cada resultado es el score raw de `NameMatcher` multiplicado por 4, lo que situa las apps en la banda [0, 4.0] del sistema global.

| Score global | Score NameMatcher | Estrategia |
|---|---|---|
| 4.4 | 1.1 | Coincidencia exacta del nombre (case-insensitive) |
| 4.0 | 1.0 | CamelHump token 0 (prefijo exacto desde el inicio) |
| 3.6* | 0.8 | CamelHump desde token interior *(floor aplicado si < 3.6)* |
| 3.6* | 0.6 | Iniciales *(floor aplicado si < 3.6)* |
| 3.6 | 0.4 | Abreviatura multi-palabra *(flooreado desde 1.6)* |
| 3.6 | 0.2 | Substring interno *(flooreado desde 0.8)* |

**Invariantes:**
- Solo se devuelven apps con score > 0.
- El score maximo de una app (sin bonus de uso) es 4.4 (coincidencia exacta del nombre).
- Cualquier app que matchea tiene un score minimo de `AppDefaults.AppMinScore` (3.6), lo que garantiza que las apps aparecen por encima de todos los resultados de ficheros excepto el match exacto de nombre completo con extension (3.85).

> **Verificar en:** `Search/Application/ApplicationSearch.cs` (metodo `Search`, multiplicador ×4 en la proyeccion)

---

## 4. Busqueda de emojis

Los emojis se activan cuando la query comienza con `:`. El sistema aplica un scoring en dos capas sobre `NameMatcher`:

| Score | Condicion |
|-------|-----------|
| 5.5 | Score de la grilla de emojis como item en la lista global (fijo) |
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

La calculadora evalua expresiones matematicas y conversiones de unidades. Los resultados de calculadora y conversor tienen un score fijo de **7**.

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
| ShowAlways | Se muestra con cualquier query (excepto si hay un PrefixOnly activo) | 0.4 |
| PrefixOnly | Se activa solo con el prefijo configurado (ej. "g texto") | 3.8 |

El modo ShowAlways tiene score bajo (0.4) para actuar como fallback: aparece debajo de cualquier app, archivo o resultado de sistema, pero siempre disponible cuando no hay nada mas relevante.

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
| PrefixOnly (default) | Solo con el prefijo configurado (ej. "define hello") | 3.7 |
| ShowAlways | Con cualquier query no vacia (sin modo emoji) | 0.3 |

En modo ShowAlways, el score (0.3) es inferior incluso al de web search ShowAlways (0.4) para que las definiciones no dominen.

> **Verificar en:** `Search/Dictionary/DictionarySource.cs` (metodo `SearchAsync`, scores en las ramas `DictionaryShowAlways`)

---

## 8. Busqueda de archivos

La busqueda de archivos es una fuente diferida (accede a disco). Se activa tras un debounce de 250 ms y solo para queries de 2+ caracteres. Produce snapshots progresivos cada 200 ms mientras los resultados llegan.

Los scores internos (calculados sobre el nombre del fichero) se multiplican por 3.5 antes de exponerse al sistema global:

| Score global | Score interno | Condicion |
|---|---|---|
| 3.85 | 1.1 | El nombre completo (con extension) coincide exactamente con la query (ej. query "PC.png" -> "PC.png") |
| 3.5 | 1.0 | El stem (sin extension) coincide exactamente con la query y el archivo tiene extension propia (ej. query "PC" -> "PC.png") |
| 3.15 | 0.9 | La extension del archivo coincide con la query (ej. query "png" -> archivos .png) |
| 2.975 | 0.85 | El nombre completo coincide exactamente con la query en un archivo sin extension (ej. carpetas) |
| 2.625 | 0.75 | El nombre comienza por la query / en multi-token, todos los tokens son prefijo de algun segmento del nombre |
| 1.75 | 0.5 | El nombre termina con la query / score por defecto para matches basicos |

Un documento con match exacto (3.5) supera a una app con match por token interior (3.2) pero queda por debajo de una app con prefijo exacto desde el inicio (4.0).

**Invariantes:**
- Nunca se buscan archivos con queries de 1 caracter.
- La busqueda tiene un timeout de 20 segundos.
- Los resultados se refinan progresivamente: el usuario ve mejores resultados a medida que llegan.
- Para queries multi-token, todos los tokens deben estar presentes en el nombre del archivo.

> **Verificar en:** `Search/UserDocuments/UserDocumentSearch.cs` (metodo `SearchAsync`, multiplicador ×3.5 al construir el `ResultItemViewModel`), `AppDefaults.cs` (constantes `FileSearchMinQueryLength`, `FileSearchTimeoutMs`, `SearchDebouncedMs`, `FileSearchSnapshotIntervalMs`)

---

## 9. Rutas y URLs explicitas

Cuando el usuario escribe una ruta del sistema de ficheros o una URL reconocida, Yottacast muestra directamente ese fichero o URL sin necesidad de buscar. Estas fuentes tienen el score mas alto (10.0) porque representan una intencion completamente explicita del usuario.

| Fuente | Activacion | Score |
|---|---|---|
| LocalPathSearch | Query empieza por `/`, `~/`, `./`, `../` o sigue patron Windows `C:\` | 10.0 |
| UrlSearch | Query reconocida como URL (con protocolo, `www.`, o dominio con TLD conocido) | 10.0 |

> **Verificar en:** `Search/LocalPath/LocalPathSearch.cs`, `Search/Url/UrlSearch.cs`

---

## 10. Boost por uso: LaunchHistory

Cada vez que el usuario activa un item (app o archivo), `LaunchHistory` registra el evento. En `MainWindowViewModel.RefreshResults()`, antes de ordenar, cada item recibe un **bonus** basado en su historial:

```
ageDays = (ahora - ultimoUso).TotalDays
decay   = e^(-ageDays / 30)          // half-life ~21 días
bonus   = min(ln(count + 1) × decay, 1.0)
```

Solo reciben bonus los items con `ItemPath` no nulo: aplicaciones (`ApplicationSearch`) y archivos (`UserDocumentSearch`). Calculator, Emoji, WebSearch y Dictionary no tienen `ItemPath` y no participan.

El bonus maxima (1.0) esta calibrado para que incluso la app mas usada no salga de su banda: una app con prefijo exacto muy usada puede llegar a 5.0, por debajo de Emoji (5.5) y muy por debajo de Calculator (7.0).

**Ejemplos:**
- 1 lanzamiento reciente: +0.35
- 10 lanzamientos recientes: +0.80
- ≥30 lanzamientos recientes: cap en +1.0
- 30 dias sin uso: bonus reducido a ~37% del valor fresco

Los datos se persisten en `AppPaths.LaunchHistoryFile` (JSON atomico, mismo patron que `EmojiUsageStore`).

> **Verificar en:** `Services/LaunchHistory.cs` (`BonusFor`, `Record`, `LoadAsync`), `MainWindowViewModel.RefreshResults()` (aplicacion del bonus), `MainWindow.axaml.cs` (`RecordLaunch` en Key.Return y OnResultsTapped), `AppDefaults.LaunchHistoryHalfLifeDays`, `AppDefaults.LaunchHistoryMaxBonus`

---

## 11. Jerarquia de scores entre fuentes

La siguiente tabla resume los scores base por fuente, de mayor a menor prioridad. El score final puede incrementarse hasta +1.0 por `LaunchHistory` (solo apps y archivos).

| Score base | Fuente | Descripcion |
|---|---|---|
| 10.0 | LocalPath / URL | Intencion explicita del usuario |
| 7.0 | Calculadora / Conversor | Score fijo. Siempre domina salvo ruta/URL |
| 5.5 | Emoji (grilla) | Score fijo de la grilla como item global |
| 3.6–4.4 (+bonus) | Aplicaciones | NameMatcher × 4 con floor 3.6; exact name = 4.4; max 5.4 con LaunchHistory |
| 3.85 | Archivos (exact full name+ext) | El unico match de fichero que supera al floor de apps |
| 3.8 | Busqueda web (PrefixOnly) | Cuando el usuario uso un prefijo explicito |
| 3.7 | Diccionario (PrefixOnly) | Cuando el usuario uso el prefijo (ej. "define") |
| 0–3.5 (+bonus) | Archivos (resto de matches) | FileScore × 3.5; max 4.5 con LaunchHistory |
| 0–4.4 (+bonus) | System Settings (macOS) | NameMatcher × 4 (igual que apps, sin floor) |
| 0.4 | Busqueda web (ShowAlways) | Fallback siempre presente |
| 0.3 | Diccionario (ShowAlways) | Fallback de menor prioridad |

**Invariantes:**
- Una URL o ruta explicita siempre aparece primero.
- Un resultado de calculadora siempre aparece por encima de cualquier app o archivo, incluso muy usados.
- Cualquier app que matchea (score > 0) aparece por encima de cualquier archivo, excepto cuando el fichero es match exacto nombre+extension (3.85 > floor de apps 3.6).
- Typing el nombre exacto de una app ("pycharm") da el score mas alto de esa app (4.4), por encima de sus iniciales ("PC" → 4.0).

> **Verificar en:** `CalculatorSearch.cs` (score 7), `EmojiSearch.cs` (score 5.5), `LocalPathSearch.cs` y `UrlSearch.cs` (score 10.0), `WebSearchSource.cs` (scores 0.4 y 3.8), `DictionarySource.cs` (scores 0.3 y 3.7), `ApplicationSearch.cs` (multiplicador ×4), `SystemSettingsSearch.cs` (multiplicador ×4), `UserDocumentSearch.cs` (multiplicador ×3.5), `LaunchHistory.cs` (bonus)

---

## 12. Debug: Modo Alt (información de scoring)

Cuando el usuario presiona Alt, la lista de resultados cambia de apariencia para mostrar informacion de scoring. Esta caracteristica es una herramienta de diagnostico para entender por qué ciertos resultados aparecen en cierto orden.

### Badge de score

Cuando Alt esta presionado, la columna de "Category" en cada resultado se reemplaza por el score total formateado como un numero (ej. `"2.64"`), siendo `total = base + bonus`. El desglose detallado (score base, razon y bonus) se muestra en el tooltip al pasar el raton.

### Tooltip de scoring

Posicionarse sobre el badge del score muestra un tooltip multi-linea con dos bloques:

1. **Razon de scoring** (la primera linea): explicacion legible del score base, establecida por la fuente de busqueda (ej. `"CamelHump inicio (×4)"` para una app que coincide por prefijo CamelCase)
2. **Breakdown de bonus** (si existe): cuantas veces se ha activado el item y hace cuanto tiempo (ej. `"3 lanzamientos, hace 5 dias"`), seguido por el valor numerico del bonus

**Contrato:** Cada fuente de busqueda debe establecer `ScoreReason` al crear el item, describiendo en lenguaje natural por qué le asigno ese score. Si una fuente no requiere explicacion (ej. calculadora con score fijo), puede establecerlo a `null`.

### Invariantes

- El badge solo aparece cuando Alt esta presionado. Sin Alt, se muestra la categoria normal.
- Los tooltips solo se muestran si el mouse esta sobre el resultado. No requieren interaccion adicional.
- El score se calcula en `MainWindowViewModel.RefreshResults()` combinando el score base de la fuente con el bonus de `LaunchHistory`.

> **Verificar en:** `MainWindowViewModel.RefreshResults()` (construccion de `ScoreDisplayText` y `ScoreTooltipText`), `Yottacast/Views/MainWindow.axaml.cs` (captura de Alt), `BaseResultItemViewModel.cs` (propiedades `ScoreDisplayText`, `ScoreTooltipText`, `ScoreReason`), cada fuente de busqueda (propiedad `ScoreReason` al crear items)

---

## 14. Flujo de busqueda en dos fases

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
