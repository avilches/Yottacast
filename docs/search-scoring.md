# Scoring de resultados

Cada fuente de búsqueda asigna un score numérico a sus resultados. `GlobalSearch` y `MainWindowViewModel` mezclan los resultados de todas las fuentes ordenándolos por score descendente.

## NameMatcher — scoring de aplicaciones y emojis

`NameMatcher` (en `Search/Application/NameMatcher.cs`) es el motor de scoring de texto para `ApplicationSearch` y `EmojiSearch`.

Expone dos overloads públicos:
- `Score(string name, string query)` — tokeniza el nombre internamente con `SplitTokens` y delega en el otro overload.
- `Score(IReadOnlyList<string> tokens, string name, string query)` — acepta tokens pre-computados; usado por `EmojiSearch`, que almacena `NameTokens` en `EmojiEntry` para no re-tokenizar en cada keystroke.

Ambos overloads aplican el mismo algoritmo: evalúan el query tal como viene y, si la query es todo minúsculas y el score no es el máximo, reintenta con `query.ToUpperInvariant()` (para que "am" coincida con "Activity Monitor"). `SplitTokens` es público para que los consumidores puedan pre-computar tokens cuando la cadena de entrada es estable.

`ScoreWith` implementa cuatro modos de matching por prioridad descendente, con los siguientes scores:

| Score | Modo |
|-------|------|
| 1.0 | CamelHump prefix empezando en el token 0 |
| 0.8 | CamelHump prefix empezando en token > 0 |
| 0.6 | Iniciales |
| 0.4 | Abreviatura multi-palabra |
| 0.2 | Substring interno |
| 0.0 | Sin match |

1. **CamelHump prefix** — cada hump del query debe ser prefijo del token correspondiente en la secuencia:
   - Match empezando en el hump 0 (inicio del nombre). Ej. "Saf" → "Safari", "ActMon" → "Activity Monitor".
   - Match empezando en hump > 0 (interior del nombre). Ej. "Mon" puede coincidir con "Activity Monitor" desde el token "Monitor".
   - "af" NO coincide con "Safari" (no es prefijo de ningún token).
2. **Iniciales**: las iniciales concatenadas de todos los tokens empiezan por el query. "AM" → "Activity Monitor", "MON" → "Microsoft OneNote" (M=Microsoft, O=One, N=Note del CamelCase).
3. **Abreviatura multi-palabra**: los humps del query se distribuyen entre múltiples tokens consumiendo ≥1 carácter por token. A diferencia del modo Iniciales, cada hump puede consumir varios caracteres del token ("smifa" → "smi" de "smiling" + "fa" de "face"). El algoritmo prueba desde cada token de la cadena (no solo desde el inicio), por lo que "face" puede matchear "smiling face" comenzando desde el token 1. Solo se activa cuando el nombre tiene más de un token; no dispara sobre nombres de un único token.
4. **Substring interno**: el nombre contiene el query, solo para queries de longitud ≥ 3. "ari" → "Safari".

Sin match: devuelve 0.

Ver `Search/Application/NameMatcher.cs` y `NameMatcherTests.cs` para casos canónicos.

### SplitTokens — separadores y comportamiento con siglas

`SplitTokens` divide primero por espacio, guión y guión bajo; luego aplica split CamelCase (transición minúscula→mayúscula) dentro de cada palabra. Trata palabras completamente en mayúsculas (ej. `"AM"`) como una secuencia de tokens de un carácter (`["A","M"]`). Esto es clave para que `"AM"` coincida con `"Activity Monitor"` tanto por CamelHump como por iniciales: los humps de `"A"` y `"M"` son prefijo de los tokens `"A"` y `"M"` respectivamente.

### Pre-cómputo de tokens: EmojiSearch vs. ApplicationSearch

`EmojiSearch` almacena `NameTokens` en cada `EmojiEntry` (tokenización simple por espacios) y los pasa al overload con tokens pre-computados, evitando re-tokenizar en cada keystroke. `ApplicationSearch`, en cambio, llama al overload `Score(string name, string query)` directamente y deja que `NameMatcher` tokenice el nombre en cada búsqueda. Los nombres de app son cadenas simples separadas por espacios o CamelCase sin guiones, por lo que el coste de tokenizar en cada llamada es bajo.

### Condición de la retry en mayúsculas

El retry con `query.ToUpperInvariant()` solo se ejecuta si el score directo es `< 1.0`. Si la query en minúsculas ya alcanza el score máximo, se omite el segundo paso.

## Scoring de EmojiSearch

`EmojiSearch.MatchScore` añade una capa de scoring por encima de `NameMatcher` para filtrar emojis por nombre y keywords:

- **3.0** — el nombre del emoji coincide exactamente con el término (case-insensitive).
- **1.2–2.0** — el nombre tiene match en `NameMatcher` (score > 0): se devuelve `nameScore + 1`.
- **0.0–1.0** — el nombre no matchea pero algún keyword sí: se devuelve el máximo score de NameMatcher sobre los keywords.

Esta escala garantiza que "fire" aparezca antes que "fireworks" cuando ambos tienen score 1.0 en NameMatcher: el match exacto eleva "fire" a 3.0.

Cuando la query es solo `:` (término vacío), no se usa ningún score: los emojis se devuelven ordenados por `SortOrder` ascendente (los primeros 20 por orden Unicode).

## Scoring de UserDocumentSearch

Ver `docs/search-files.md` §Scoring.

## Scores entre fuentes

Los scores exactos de cada fuente se definen en cada clase de búsqueda y en `MainWindowViewModel.MakeGoogleItem()`. Los scores son ≤ 1 para resultados de fuentes de búsqueda (apps, documentos), mientras que calculadora, emoji y Google usan scores > 1 para garantizar su posición en el merge. El grid de emoji tiene score 3.5 y el item de Google tiene score 3; el ítem de calculadora/conversor tiene un score propio > 1 definido en `CalculatorSearch`. Ver `MainWindowViewModel.cs` y `docs/search-sources.md` §GlobalSearch para el flujo de merge.

### Límite por fuente y merge final

`MainWindowViewModel` pasa `limit: 10` (`SearchSourceLimit`) tanto a `SearchInstant` como a `SearchDeferredAsync`. Cada source devuelve como máximo 10 resultados. `GlobalSearch.SearchInstant` combina los resultados de todas las instant sources y aplica un segundo `OrderByDescending(Score).Take(10)` sobre la unión. El merge en `RefreshResults` ordena de nuevo los ítems de instant, deferred y Google por score descendente antes de poblar la lista visible.

### Selección automática al hacer merge

Tras cada merge en `RefreshResults`, si hay un resultado de categoría `"Calculator"` o `"Converter"` y el usuario aún no ha pulsado ninguna tecla de navegación, ese resultado queda seleccionado automáticamente independientemente de su posición en la lista por score. Esto es un bypass de la ordenación por score para dar prioridad al resultado de cálculo.
