# Scoring de resultados

Cada fuente de búsqueda asigna un score numérico a sus resultados. `GlobalSearch` y `MainWindowViewModel` mezclan los resultados de todas las fuentes ordenándolos por score descendente.

## NameMatcher — scoring de aplicaciones y emojis

`NameMatcher` (en `Search/Application/NameMatcher.cs`) es el motor de scoring de texto para `ApplicationSearch` y `EmojiSearch`.

Expone dos overloads públicos:
- `Score(string name, string query)` — tokeniza el nombre internamente con `SplitTokens` y delega en el otro overload.
- `Score(IReadOnlyList<string> tokens, string name, string query)` — acepta tokens pre-computados; usado por `EmojiSearch`, que almacena `NameTokens` en `EmojiEntry` para no re-tokenizar en cada keystroke.

Ambos overloads aplican el mismo algoritmo: evalúan el query tal como viene y, si la query es todo minúsculas y el score no es el máximo, reintenta con `query.ToUpperInvariant()` (para que "am" coincida con "Activity Monitor"). `SplitTokens` es público para que los consumidores puedan pre-computar tokens cuando la cadena de entrada es estable.

`ScoreWith` implementa cuatro modos de matching por prioridad descendente:

1. **CamelHump prefix** — cada hump del query debe ser prefijo del token correspondiente en la secuencia:
   - Match empezando en el hump 0 (inicio del nombre). Ej. "Saf" → "Safari", "ActMon" → "Activity Monitor".
   - Match empezando en hump > 0 (interior del nombre). Ej. "Mon" puede coincidir con "Activity Monitor" desde el token "Monitor".
   - "af" NO coincide con "Safari" (no es prefijo de ningún token).
2. **Iniciales**: las iniciales concatenadas de todos los tokens empiezan por el query. "AM" → "Activity Monitor", "MON" → "Microsoft OneNote" (M=Microsoft, O=One, N=Note del CamelCase).
3. **Abreviatura multi-palabra**: variante de abreviatura que cubre patrones de query más largos donde los humps del query se distribuyen entre múltiples tokens. Ver `NameMatcher.cs` para la lógica exacta.
4. **Substring interno**: el nombre contiene el query, solo para queries de una longitud mínima. "ari" → "Safari".

Sin match: devuelve 0.

Ver `Search/Application/NameMatcher.cs` y `NameMatcherTests.cs` para los valores exactos de scoring y casos canónicos.

### SplitTokens — comportamiento con siglas

`SplitTokens` trata palabras completamente en mayúsculas (ej. `"AM"`) como una secuencia de tokens de un carácter (`["A","M"]`). Esto es clave para que `"AM"` coincida con `"Activity Monitor"` tanto por CamelHump como por iniciales: los humps de `"A"` y `"M"` son prefijo de los tokens `"A"` y `"M"` respectivamente.

## Scoring de UserDocumentSearch

Ver `docs/search-files.md` §Scoring.

## Scores entre fuentes

Los scores exactos de cada fuente se definen en cada clase de búsqueda y en `MainWindowViewModel.MakeGoogleItem()`. Los scores son ≤ 1 para resultados de fuentes de búsqueda (apps, documentos, emojis), mientras que calculadora y Google usan scores > 1 para garantizar su posición en el merge. Ver `MainWindowViewModel.cs` y `docs/search-sources.md` §GlobalSearch para el flujo de merge.
