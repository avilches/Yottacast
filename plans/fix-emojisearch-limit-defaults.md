# Fix: EmojiSearch — respetar el parámetro `limit` en resultados por defecto

## Problema

`GetDefaultResults()` ignora el parámetro `limit` que recibe `SearchAsync`:

```csharp
// SearchAsync pasa limit al filtro...
var results = string.IsNullOrEmpty(term)
    ? GetDefaultResults()          // ← limit no se pasa aquí
    : FilterEmojis(term, limit);   // ← FilterEmojis sí lo usa

// GetDefaultResults siempre devuelve exactamente 6 resultados
private IReadOnlyList<ResultItemViewModel> GetDefaultResults() =>
    Defaults                        // array de 6 elementos fijo
        .Select(...)
        .OfType<EmojiEntry>()
        .Select(e => MakeResult(e, 3.5))
        .ToList();
```

Por coherencia de la interfaz `ISearchSource`, el parámetro `limit` debe respetarse
en todos los caminos. Si GlobalSearch solicita `limit=3`, EmojiSearch devuelve 6,
lo que puede saturar la lista de resultados con emojis por defecto.

## Solución

Pasar `limit` a `GetDefaultResults` y aplicar `.Take(limit)`.

### Cambios en `EmojiSearch.cs`

```csharp
// En SearchAsync:
var results = string.IsNullOrEmpty(term)
    ? GetDefaultResults(limit)
    : FilterEmojis(term, limit);

// GetDefaultResults con limit:
private IReadOnlyList<ResultItemViewModel> GetDefaultResults(int limit) =>
    Defaults
        .Take(limit)                        // ← añadir esta línea
        .Select(c => Entries.Value.FirstOrDefault(e => e.Char == c))
        .OfType<EmojiEntry>()
        .Select(e => MakeResult(e, 3.5))
        .ToList();
```

El `.Take(limit)` se aplica sobre `Defaults` (antes de buscar en `Entries.Value`)
para evitar trabajo innecesario cuando `limit < 6`.

## Archivos a modificar

- `Yottacast.Core/Search/EmojiSearch.cs` — métodos `SearchAsync` y `GetDefaultResults`

## Tests

Añadir en `EmojiSearchTests.cs` (ver plan #4):

```csharp
[Fact]
public async Task DefaultResults_RespectLimit() {
    var results = await SearchAsync(search, ":", limit: 3);
    Assert.Equal(3, results.Count);
}

[Fact]
public async Task DefaultResults_LimitLargerThanDefaults_ReturnsAll() {
    var results = await SearchAsync(search, ":", limit: 10);
    Assert.Equal(6, results.Count); // Defaults tiene 6
}
```

## Criterio de aceptación

- Query `:` con `limit=3` devuelve 3 resultados
- Query `:` con `limit=10` devuelve 6 (el máximo de Defaults)
- Query `:smile` con `limit=3` ya funcionaba — sin regresión
