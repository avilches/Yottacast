## Tests

Al modificar esta area, actualizar los tests en `Yottacast.Core.Tests/Search/`:
- `GlobalSearchTests.cs` — orquestacion de busqueda, merge y ordenacion de resultados
- `ApplicationSearchTests.cs` — busqueda e indexacion de aplicaciones instaladas
- `NameMatcherTests.cs` — algoritmo de puntuacion y matching de nombres
- `MathJsUnitSnapshotTests.cs` — snapshot de unidades disponibles en math.js
- `LocalDictionaryTests.cs` — LocalDictionaryDb (lookup, case-insensitive, Exists) y LocalDictionaryConverter (conversion JSONL→SQLite)
- `LocalPathSearchTests.cs` — búsqueda de rutas de fichero locales
- `UrlSearchTests.cs` — conector de URLs con verificación HEAD y favicon