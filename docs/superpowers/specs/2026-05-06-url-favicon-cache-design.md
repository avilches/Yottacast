# Spec: URL sin validación + caché de favicons en disco

**Fecha:** 2026-05-06

## Contexto

`UrlSearch` detecta si la query parece una URL y muestra un resultado para abrirla en el navegador.
Actualmente hay dos problemas:

1. Cuando `EnableUrlValidation = false`, el source devuelve `[]` — el usuario no ve ninguna opción de URL aunque la query sea claramente una URL.
2. Los favicons (obtenidos de `google.com/s2/favicons`) solo se cachean en memoria. Se descargan de nuevo en cada sesión.

## Objetivo

- Cuando la validación está desactivada, mostrar igualmente el resultado si la query parece una URL (sin hacer DNS/HEAD).
- Extraer la lógica de favicons a un servicio `FaviconCache` con caché en disco, análogo a `AppIconCache`.

---

## Diseño

### 1. `FaviconCache` (nueva clase)

**Archivo:** `Yottacast.Core/Services/FaviconCache.cs`

Responsabilidad única: obtener y persistir favicons por host de dominio.

**Constructor:** `HttpClient httpClient, ILogger<FaviconCache> logger`

**Caché en memoria:** `ConcurrentDictionary<string, byte[]?>` keyed por host (`github.com`).

**Caché en disco:** `AppPaths.FaviconCacheDir/{host}.png`
- Nombre de fichero directo (los hostnames son safe como nombres de fichero: letras, dígitos, puntos, guiones).
- Sin TTL: los favicons cambian raramente; se invalida manualmente borrando el fichero si fuese necesario.

**API pública:**

```csharp
/// Devuelve bytes en memoria si disponibles, dispara carga async si es el primer acceso para este host.
public byte[]? GetOrLoad(string host);

/// Fires on a thread-pool thread when a favicon finishes loading with non-null bytes.
public event Action? FaviconLoaded;

/// Clears in-memory cache. Disk cache persists across sessions.
public Task Stop();
```

**Flujo interno de `GetOrLoad`:**

1. Si `_memory.ContainsKey(host)` → devolver valor (puede ser null si ya intentó y falló).
2. Si no: `_memory.GetOrAdd(host, null)` como sentinel, disparar `_ = Task.Run(() => LoadAsync(host))`.

**Flujo de `LoadAsync(host)`:**

1. Comprobar `AppPaths.FaviconCacheDir/{host}.png` en disco.
   - Si existe: leer bytes → `_memory[host] = bytes` → `FaviconLoaded?.Invoke()` → return.
2. Fetch HTTP: `https://www.google.com/s2/favicons?sz=64&domain={host}` con timeout `AppDefaults.FaviconTimeoutSeconds`.
   - Éxito y bytes > 0: `Directory.CreateDirectory(dir)` + escribir disco + `_memory[host] = bytes` + `FaviconLoaded?.Invoke()`.
   - Fallo o bytes vacíos: `_memory[host] = null` (no reintenta en la misma sesión).

> **Verificar en:** `FaviconCache.cs`, `FaviconCacheTests.cs`

---

### 2. Cambios en `UrlSearch`

**Archivo:** `Yottacast.Core/Search/Url/UrlSearch.cs`

**Constructor:** añadir `FaviconCache faviconCache`. Eliminar `_favicons`. Eliminar `LoadFaviconAsync`.

**Wiring de eventos (en constructor o en `Start`):**
```csharp
faviconCache.FaviconLoaded += () => ResultChanged?.Invoke();
```

**Nuevo `Search()`:**

```
EnableWebSearch = false  →  return []
TryNormalizeUrl = false  →  return []

EnableUrlValidation = false:
    faviconCache.GetOrLoad(host)          // dispara carga async si nueva
    iconBytes = faviconCache.GetOrLoad(host) // null si aún no llegó
    return [BuildResult(url, iconBytes, errorHint: null)]

EnableUrlValidation = true:
    (flujo actual con _reachability — sin cambios excepto que
     CheckReachabilityAsync llama a faviconCache.GetOrLoad(host)
     en lugar de LoadFaviconAsync)
```

**`Stop()`:** eliminar `_favicons.Clear()`. Añadir `await faviconCache.Stop()` (o simplemente limpiar `_reachability` y `_reachabilityError` como ahora).

> **Verificar en:** `UrlSearch.cs` línea 38-55, `UrlSearchTests.cs`

---

### 3. Plomería

**`AppPaths`** — nueva entrada:
```csharp
/// Favicon disk cache directory.
public static readonly string FaviconCacheDir = Path.Combine(CacheDir, "favicons");
```

**`AppDefaults`** — nueva constante (extrae el `5` hardcodeado):
```csharp
/// HTTP timeout for favicon requests.
public const int FaviconTimeoutSeconds = 5;
```

**DI:** registrar `FaviconCache` como singleton en el mismo lugar donde se registra `AppIconCache`.

---

### 4. Tests

**`UrlSearchTests.cs`** — cambios:

- `BuildSearch()`: añadir `FaviconCache` (instanciado con `FakeHttpMessageHandler`).
- `Search_ValidationOff_ReturnsEmpty` → renombrar y cambiar a `Search_ValidationOff_ReturnsPendingResult`: debe devolver **un resultado** con título = URL normalizada.
- Añadir: `Search_ValidationOff_DoesNotStartDnsOrHead` — tras 50 ms, `handler.CallCount == 0` para HEAD/DNS (solo puede haber llamada de favicon).

**`FaviconCacheTests.cs`** — nuevo archivo:

- `GetOrLoad_DiskHit_DoesNotFetch`: si el fichero existe en disco, `handler.CallCount == 0`.
- `GetOrLoad_DiskMiss_FetchesAndWritesToDisk`: fetch → escribe fichero en `_tempDir`.
- `GetOrLoad_SameHostTwice_OnlyOneFetch`: llamar dos veces al mismo host → `handler.CallCount <= 1`.
- `FaviconLoaded_FiredAfterLoad`: evento se dispara cuando llegan bytes.

---

## Invariantes

- `EnableWebSearch = false` → `UrlSearch` devuelve siempre `[]`.
- `EnableWebSearch = true, EnableUrlValidation = false` → devuelve resultado si la query parece URL; sin DNS ni HEAD.
- `EnableWebSearch = true, EnableUrlValidation = true` → flujo actual con DNS + favicon.
- Un mismo host solo provoca una descarga de favicon por sesión (dedup vía `GetOrAdd`).
- El disco persiste entre sesiones; la memoria se limpia en `Stop()`.