# Conectores de ruta local y URL

**Fecha**: 2026-05-05

## Objetivo

Añadir dos nuevos `IInstantSearchSource` que detectan patrones especiales en la query y los presentan como resultados accionables:

1. **`LocalPathSearch`** — detecta si la query es una ruta a un fichero o directorio existente.
2. **`UrlSearch`** — detecta si la query parece una URL, la verifica con un HEAD request y ofrece abrirla en el browser.

Ambos fuentes son siempre activas (sin toggle en settings) y producen `ResultItemViewModel` estándar, por lo que no requieren nuevos DataTemplates.

---

## 1. LocalPathSearch

### Detección de ruta

La query se considera una posible ruta si:
- Empieza por `/`, `~/`, `./` o `../` (macOS/Linux)
- Empieza por `[A-Za-z]:\` (Windows)
- Tiene al menos 2 caracteres

El carácter `~` se expande con `Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)`.

### Verificación de existencia

Tras expandir, se comprueba `File.Exists(expandedPath) || Directory.Exists(expandedPath)`. Si no existe, la fuente devuelve `[]`.

### Resultado

| Campo | Valor |
|---|---|
| `Title` | `Path.GetFileName(path)` (o el path completo si `GetFileName` devuelve vacío, p.ej. raíz) |
| `Subtitle` | Ruta completa expandida |
| `Category` | `"Files"` |
| `Score` | `1.0` |
| `IconBytes` | `fileIconCache.GetOrPreload(path)` — carga async; se actualiza via `IconLoaded` existente |
| `OnActivate` | `platform.LaunchApp(path)` |
| `OnCopy` | `clipboard.CopyText(path)` |
| `CopiedMessage` | `"Path copied!"` |

### Ciclo de vida

- `WhenReady()` → `Task.CompletedTask` (sin inicio en background)
- `Stop()` → `Task.CompletedTask`
- Siempre activo; no depende de `EnableFileSearch` ni de ningún setting

### Dependencias

`FileIconCache`, `PlatformProvider`, `ClipboardService`

---

## 2. UrlSearch

### Detección de URL (`IsLikelyUrl`)

La query se considera posible URL si cumple **una** de estas condiciones:

| Condición | Normalización |
|---|---|
| Empieza por `http://` o `https://` | Se usa tal cual |
| Empieza por `www.` | Se prefija `https://` |
| Coincide con `<algo>.<tld>[/...]` sin espacios, donde tld ∈ `{com, net, org, io, co, uk, de, es, fr, dev, app, ai, edu, gov}` | Se prefija `https://` |

La query debe tener al menos 4 caracteres y no contener espacios para los dos últimos casos.

### Estado interno (por URL normalizada)

```
enum UrlReachability { Pending, Valid, Invalid }

Dictionary<string, UrlReachability> _reachability   // thread-safe: ConcurrentDictionary
Dictionary<string, byte[]?>        _favicons         // thread-safe: ConcurrentDictionary
```

La cache vive durante toda la sesión. Si el usuario escribe la misma URL dos veces, se reutiliza el estado previo.

### Flujo de `Search(query)`

1. Si `IsLikelyUrl(query)` devuelve false → `[]`
2. Normalizar URL
3. Si `_reachability[url] == Invalid` → `[]`
4. Si `Pending` o `Valid` → construir y devolver `ResultItemViewModel`
5. Si no está en cache → insertar `Pending`, lanzar tarea background, devolver `ResultItemViewModel`

El resultado se devuelve en todos los casos excepto `Invalid`, para que aparezca inmediatamente mientras se verifica.

### Tarea background

Para cada URL nueva:

1. **HEAD request** con `HttpClient`, timeout 3 s
2. Si respuesta `>= 200` y `< 400`: marcar `Valid`, lanzar carga de favicon (ver abajo), disparar `ResultChanged`
3. Si falla (timeout, error de red, `>= 400`): marcar `Invalid`, disparar `ResultChanged`

**Carga de favicon**:
- `GET https://www.google.com/s2/favicons?sz=64&domain=<domain>` con el `HttpClient` existente
- Si la respuesta es 200 y content-type es imagen: guardar bytes en `_favicons[url]`, disparar `ResultChanged`
- Si falla: `_favicons[url] = null` (el icono del browser se usará como fallback)

### Evento `ResultChanged`

```csharp
public event Action? ResultChanged;
```

`MainWindowViewModel` se suscribe y re-lanza `SearchInstant` (mismo patrón que `fileIconCache.IconLoaded`). Esto provoca que:
- Si el HEAD falla: el resultado desaparece (la fuente devolverá `[]`)
- Si el favicon carga: el icono se actualiza

### Resultado

| Campo | Valor |
|---|---|
| `Title` | URL normalizada (truncada a 80 chars con `…` si es más larga) |
| `Subtitle` | `"Open in <BrowserName>"` o `"Open in browser"` si no hay browser activo |
| `Category` | `"Web"` |
| `Score` | `3.0` |
| `BypassLimit` | `true` |
| `IconBytes` | `_favicons[url]` si disponible; si no, `appIconCache.Get(browser.ExecutablePath)` |
| `OnActivate` | `browserDiscovery.OpenUrl(url, browser)` (no-op si `ActiveBrowser == null`) |

### Ciclo de vida

- `WhenReady()` → `Task.CompletedTask`
- `Stop()` → limpiar `_reachability` y `_favicons`; `Task.CompletedTask`
- Siempre activo; sin toggle en settings

### Dependencias

`HttpClient`, `UserSettings`, `BrowserDiscovery`, `AppIconCache`, `ILogger<UrlSearch>`

---

## Registro en DI (App.axaml.cs)

```csharp
services.AddSingleton<LocalPathSearch>();
services.AddSingleton<UrlSearch>();
services.AddSingleton<IInstantSearchSource>(sp => sp.GetRequiredService<LocalPathSearch>());
services.AddSingleton<IInstantSearchSource>(sp => sp.GetRequiredService<UrlSearch>());
```

`MainWindowViewModel` recibe `UrlSearch` inyectado para suscribirse a `ResultChanged`.

---

## Tests

- `LocalPathSearchTests` — detección de patrones de ruta (positivos/negativos), expansión de `~`, result cuando existe vs no existe
- `UrlSearchTests` — detección de URLs (positivos/negativos), normalización, comportamiento con estado `Pending/Valid/Invalid`

Los tests usan un `FakePlatformProvider` y un `MockHttpMessageHandler` para aislar de disco y red.

---

## Invariantes

- `LocalPathSearch` nunca lanza excepciones si el path es inválido para el SO (se atrapa `ArgumentException` etc. en el guard de detección).
- `UrlSearch` nunca bloquea `Search()` más de microsegundos; todo I/O es async en background.
- Un `Invalid` en `_reachability` no se borra durante la sesión. Si el usuario quiere reintentar, debe borrar el texto y volver a escribir la misma URL (al ser la misma clave, seguirá `Invalid`).

> **Nota**: el último invariante puede revisarse si se decide añadir un TTL a las entradas `Invalid` en el futuro.
