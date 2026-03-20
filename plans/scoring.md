# Plan de scoring y ranking — Yottacast

Análisis del código actual (marzo 2026) y propuestas ordenadas por área. Todo lo relativo a puntuación de resultados, boost por uso, y calidad del ranking vive aquí.

---

## 1. Normalización de escalas entre categorías

**Problema.** Los scores de `ApplicationSearch` están en [0, 1], el ítem de Google tiene score fijo 3, `CalculatorSearch` tiene score fijo 4, `EmojiSearch` usa scores [1, 6]. No existe normalización entre categorías. Un documento con score 0.75 pierde frente al ítem de Google (3) aunque el usuario esté claramente buscando un archivo. Un "ari" en Safari (score 0.2) pierde frente a cualquier documento con score 0.5, aunque el documento sea menos relevante.

La escala heterogénea hace imposible ordenar sensatamente el resultado fusionado en `MainWindowViewModel.RefreshResults()`, que simplemente hace `OrderByDescending(x => x.Score)` sobre todo.

**Solución propuesta.** Definir bandas explícitas de score global:

| Banda | Rango | Usos |
|---|---|---|
| Sistema / calculadora | 10–20 | Calculator, Converter, Emoji (exact) |
| Aplicación prefijo exacto | 5–7 | NameMatcher score 1.0 |
| Aplicación prefijo parcial | 3–5 | NameMatcher score 0.8 |
| Documento relevante | 2–4 | Doc score 0.75–1.0 |
| Aplicación iniciales/substring | 1–2 | NameMatcher score 0.6–0.2 |
| Google / fallback | 0.5 | siempre presente |

Esto requiere que cada `ISearchSource` escale sus scores a la banda correcta, o que `GlobalSearch` aplique un factor de escala por fuente declarado en la interfaz (p.ej. `double ScoreBias { get; }`).

**Ficheros a tocar**: `ApplicationSearch.cs`, `UserDocumentSearch.cs`, `CalculatorSearch.cs`, `EmojiSearch.cs`, `MainWindowViewModel.cs` (score del ítem de Google: actualmente hardcoded a `3` en `MakeGoogleItem()`).

**Impacto.** El resultado número 1 sería casi siempre el correcto.

**Complejidad.** Media. Requiere actualizar los tests que verifican valores exactos de score.

---

## 2. NameMatcher: factor de cobertura

**Problema.** En `NameMatcher.ScoreWith`, el score de CamelHump es siempre 1.0 si el match empieza en token 0, y 0.8 si empieza en token > 0, sin importar cuánto cubre la query. Una query "S" que hace prefijo de "Safari" vale 1.0 igual que una query "Safari" completa. Esto produce empates entre queries cortas y largas, y hace que el usuario que escribe más no sea recompensado con un mejor ranking.

```csharp
// NameMatcher.cs línea 31: score fijo independiente de cobertura
if (match) return start == 0 ? 1.0 : 0.8;
```

**Solución propuesta.** Incorporar un factor de cobertura: `coverage = totalCharsCoveredByQuery / name.Length`. Resultado: `baseScore * (0.5 + 0.5 * coverage)`. Así "Safari" (cobertura 1.0) vale 1.0, "Saf" (cobertura 0.5) vale ~0.75, "S" (cobertura ~0.16) vale ~0.58.

```csharp
if (match) {
    var covered = queryHumps.Select((h, k) => queryHumps[k].Length).Sum();
    var coverage = (double)covered / name.Length;
    var baseScore = start == 0 ? 1.0 : 0.8;
    return baseScore * (0.5 + 0.5 * coverage);
}
```

**Impacto.** Queries más específicas suben en el ranking. "Safari" supera a "S" al buscar Safari. Reduce empates arbitrarios.

**Complejidad.** Baja. Cambio localizado en `NameMatcher.ScoreWith`. Requiere actualizar `NameMatcherTests` y `ApplicationSearchTests` donde se fijan los valores exactos.

---

## 3. Score de substring interno demasiado alto

**Problema.** Una query "pdf" con score 0.2 puede aparecer antes que un documento con score 0.5 si no se ha normalizado la escala (ver §1). Más importante: "pdf" como substring de "OpenBSD PDF Viewer" tiene score 0.2, pero el usuario buscando "pdf" probablemente quiere un visor, no cualquier cosa que contenga "pdf". El threshold de 3 chars es razonable pero el score 0.2 es demasiado alto comparado con los documentos.

**Solución propuesta.** Reducir el score de substring interno de 0.2 a 0.1 en `NameMatcher`. Con la normalización de escalas (§1), este valor quedará por debajo de cualquier documento relevante.

**Ficheros a tocar**: `NameMatcher.cs`, tests asociados.

**Complejidad.** Baja.

---

## 4. Boost por frecuencia y recencia de uso (LaunchHistory)

**Problema.** El scoring es puramente textual. No hay ningún mecanismo para recordar qué apps lanza el usuario con frecuencia. "Chrome" y "ChromeDriver" tienen el mismo score para la query "chr"; tras el desempate por cobertura pueden quedar en el mismo puesto, aunque el usuario siempre abra Chrome. Alfred llama a esto "Learning"; Raycast lo llama "frecuency boosting".

### Ficheros nuevos

- `Yottacast.Core/Services/LaunchHistory.cs` — singleton. Persiste un `Dictionary<string, LaunchRecord>` donde la clave es el `Path` del item lanzado y `LaunchRecord` contiene `Count` y `LastUsed`. Expone `void Record(string path)` y `double Bonus(string path)`.

### Ficheros modificados

- `Yottacast.Core/ViewModels/ResultItemViewModel.cs` — añadir `string Path { get; init; }` (ya existe `Subtitle` con la ruta pero no hay un campo semántico dedicado).
- `ApplicationSearch.cs` — el `ResultItemViewModel` que construye pasa `Path = x.app.Path`.
- `UserDocumentSearch.cs` — idem con la ruta del fichero.
- `MainWindowViewModel.cs` — en `RefreshResults()`, antes de ordenar, sumar `LaunchHistory.Bonus(item.Path)` al score de cada item. El `OnActivate` de cada item se envuelve para llamar `LaunchHistory.Record(item.Path)` antes de la acción original.
- `App.axaml.cs` — registrar `LaunchHistory` en DI e inyectarla en `MainWindowViewModel`.

### Fórmula de bonus

```csharp
// LaunchHistory.cs
public double Bonus(string itemPath) {
    if (!_data.TryGetValue(itemPath, out var r)) return 0;
    var ageDays = (DateTimeOffset.UtcNow - r.LastUsed).TotalDays;
    var decay = Math.Exp(-ageDays / 30.0); // half-life ~21 días
    return Math.Log(r.Count + 1) * decay * 0.5; // max boost ~0.5 sobre el score base
}
```

En `RefreshResults()` de `MainWindowViewModel`, antes del `OrderByDescending`:

```csharp
var boosted = items.Select(x => (item: x, score: x.Score + _launchHistory.Bonus(x.Path)));
```

**Impacto.** El launcher se adapta al comportamiento del usuario con el tiempo.

**Complejidad.** Media. Requiere: campo nuevo en settings o fichero separado, incremento en `LaunchApp`, y pasar el usage store a `ApplicationSearch`. Los tests necesitan el usage store inyectable.

---

## 5. Score por relevancia del directorio en documentos

**Problema.** Un fichero `report.pdf` en `~/Desktop` tiene el mismo score que uno en `~/Library/Containers/com.something.app/report.pdf`. Los documentos en ubicaciones primarias (Desktop, Documents, Downloads) deberían puntuar más alto por ser más relevantes para el usuario.

El score en `UserDocumentSearch.SearchAsync` solo mira el nombre del fichero, nunca la ruta.

**Solución propuesta.** Aplicar un multiplicador según el directorio padre:

```csharp
var pathScore = GetPathRelevanceScore(r.Path, settings.ExpandedSearchFolders);
score *= pathScore;

// pathScore: 1.0 para Desktop/Downloads/Documents, 0.8 para raíz de carpeta configurada,
// 0.6 para subdirectorios de primer nivel, 0.4 para rutas más profundas
```

**Ficheros a tocar**: `UserDocumentSearch.SearchAsync`.

**Complejidad.** Baja.

---

## 6. Extraer FileNameMatcher de UserDocumentSearch

**Problema.** `UserDocumentSearch.cs:55-88` implementa inline una heurística de scoring de nombres de archivo (multi-token, prefijo, sufijo, exacto). Esta lógica no está testeada de forma unitaria (solo de forma integrada en `UserDocumentSearchTests`), y es difícil de comparar con el scoring de `NameMatcher`.

**Solución propuesta.** Extraer la función de scoring a un método estático `FileNameMatcher.Score(string name, string query)`, análogo a `NameMatcher.Score`. Esto permite tests unitarios directos sin construir un `UserDocumentSearch` completo y hace que los dos sistemas de scoring (apps y ficheros) sean comparables y evolucionen de forma coherente.

**Ficheros a tocar**: nuevo `Yottacast.Core/Search/FileNameMatcher.cs`, `UserDocumentSearch.cs`.

**Complejidad.** Baja. Refactor puro.

---

## 7. Score visible en producción (ruido visual)

**Problema.** `MainWindow.axaml` (línea 217-220) muestra `{Binding Score, StringFormat={}{0:F2}}` con opacidad 0.6 para todos los resultados. Esto es ruido visual que expone un detalle de implementación interno.

```xml
<TextBlock Text="{Binding Score, StringFormat={}{0:F2}}"
           Foreground="{DynamicResource Theme.ItemCategory}"
           FontSize="{DynamicResource Theme.FontSizeSmall}"
           Opacity="0.6"
           VerticalAlignment="Center"/>
```

Alfred, Raycast y Spotlight no muestran scores al usuario.

**Solución**: eliminar ese `TextBlock` de `Yottacast/Views/MainWindow.axaml`.

**Complejidad.** Baja. Una línea eliminada.

---

## 8. Spotlight metadata como alternativa al LaunchHistory

**Problema.** `MacOsPlatformProvider.ScanAppsAsync` no recupera `kMDItemLastUsedDate` (última vez que el usuario abrió la app). Estos metadatos permitirían implementar el boost por frecuencia (§4) sin necesidad de persistencia propia.

**Solución propuesta.** Ampliar `SpotlightInterop` para recuperar atributos adicionales junto con `kMDItemPath`. `kMDItemLastUsedDate` es un `CFDate` que requiere conversión adicional desde el runtime de CoreFoundation.

**Alternativa práctica.** Usar `MDItemCopyAttribute` con `kMDItemLastUsedDate` solo para apps, y mapear esa fecha a un score bonus. Si la app se usó en los últimos 7 días: +0.05; en los últimos 30 días: +0.02.

**Evaluación.** La serialización de `CFDate` desde P/Invoke es delicada y no es multiplataforma. Preferir la solución de persistencia propia (§4) por robustez y compatibilidad con Windows/Linux. Esta opción solo vale si se quiere evitar el fichero de persistencia en macOS.

**Complejidad.** Alta. Diferir hasta tener §4 implementado.

---

## 9. Tests de calidad de ranking

### 9.1 Tests de ranking end-to-end (ausentes)

Los tests actuales verifican scores individuales y comportamiento de la pipeline, pero no verifican que para una query concreta el resultado número 1 sea el correcto.

```csharp
[Theory]
// El match exacto de nombre siempre supera al prefijo parcial
[InlineData("safari", new[]{"Safari", "Safari Extensions"}, "Safari")]
// El prefijo de token 0 supera al prefijo de token > 0
[InlineData("Mon", new[]{"Activity Monitor", "Monster Hunter"}, "Monster Hunter")]
// Queries más largas superan a queries más cortas (tras fix §2)
[InlineData("Safari", new[]{"Safari", "Safarish App"}, "Safari")]
public void Ranking_Query_ReturnsExpectedFirstResult(
    string query, string[] apps, string expectedFirst)
```

### 9.2 Tests de normalización de scores entre categorías

```csharp
[Fact]
public void AppScore_AlwaysBeatsDocumentScore_ForPrefixMatch() {
    // Un app con prefijo exacto (score ≥ 5 tras normalización) siempre supera
    // a un documento con score base (≤ 4)
    Assert.True(appPrefixScore > docBaseScore);
}
```

**Complejidad.** Baja. Son tests de alto valor y bajo coste, añadir en paralelo con cada cambio de scoring.

---

## Resumen priorizado

| # | Propuesta | Impacto | Complejidad | Prioridad |
|---|---|---|---|---|
| 7 | Ocultar score numérico en producción | Medio (UX) | Baja | 1 — quick win |
| 1 | Normalización de escalas de score | Alto | Media | 2 |
| 2 | Factor de cobertura en NameMatcher | Alto | Baja | 3 |
| 3 | Reducir score de substring (0.2 → 0.1) | Medio | Baja | 4 |
| 6 | Extraer FileNameMatcher | Bajo (calidad) | Baja | 5 |
| 4 | Boost por frecuencia/recencia (LaunchHistory) | Alto | Media | 6 |
| 5 | Score por relevancia de directorio | Medio | Baja | 7 |
| 9 | Tests de calidad de ranking | Alto (seguridad) | Baja | En paralelo con cada cambio |
| 8 | Spotlight kMDItemLastUsedDate | Medio | Alta | Diferir |