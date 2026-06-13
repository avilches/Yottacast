# Release workflow: assets, versiones y actualizaciones

Este documento describe los comportamientos y contratos del sistema de release de Yottacast: como se gestionan los
assets embebidos, como se detectan cambios de version y como se comprueba si hay actualizaciones disponibles.

---

## 1. Assets embebidos: obtencion y ciclo de vida

Algunos assets pesados no se guardan en el repositorio. El sistema de build los descarga o copia automaticamente la
primera vez, de forma idempotente (solo actua si el fichero no existe en el source tree).

### Contrato

- El desarrollador nunca necesita descargar manualmente ningun asset. El build se encarga.
- Si un asset ya existe en el source tree, el build no lo sobreescribe.
- Para forzar una actualizacion de cualquier asset, basta con borrar el fichero y recompilar.

### Assets gestionados

| Asset                           | Origen                                                     | Version fijada en                                  | Proposito                              |
|---------------------------------|------------------------------------------------------------|----------------------------------------------------|----------------------------------------|
| `Search/Calculator/math.min.js` | CDN cdnjs (descarga HTTP en build)                         | URL del target `DownloadMathJs` en el `.csproj`    | Motor de calculo matematico (Jint)     |
| `Search/Emoji/emoji-data.json`  | GitHub iamcal/emoji-data (descarga HTTP en build)          | URL del target `DownloadEmojiData` en el `.csproj` | Datos raw de emojis (~1.25 MB)         |
| `Search/Emoji/emoji-cache.json` | Copia desde AppData local (generado por la app en runtime) | N/A (derivado de `emoji-data.json`)                | Cache compacto de emojis (~100-150 KB) |

**Invariante**: la version de `math.min.js` esta fijada en la URL del target MSBuild. Recompilar siempre descarga la
misma version. Para actualizar a una version nueva de math.js hay que editar la URL en el target `DownloadMathJs`.

Todos los assets se declaran como `EmbeddedResource` condicionales: si el fichero no existe en el source tree, el item
se omite y el build no falla.

> **Verificar en:** `Yottacast.Core/Yottacast.Core.csproj` -- targets `DownloadMathJs`, `DownloadEmojiData`,
`CopyEmojiCache` y bloque `<ItemGroup>` con `EmbeddedResource`.

---

## 2. Cache de emojis: generacion, promocion y carga

El cache compacto de emojis permite un arranque rapido sin parsear el JSON raw completo en cada inicio.

### Contrato de generacion

1. La primera vez que se ejecuta la app sin cache disponible, se parsea `emoji-data.json` y se escribe el cache compacto
   en disco (directorio AppData del usuario).
2. En el siguiente build, el target `CopyEmojiCache` copia ese fichero al source tree (solo si no existe ya alli).
3. Al commitear `emoji-cache.json`, futuros clones del repo lo tendran desde el primer build.
4. En produccion, la app encuentra el cache embebido y lo carga directamente.

**Invariante**: la escritura del cache en disco es atomica (se escribe a `.tmp` y se mueve con
`File.Move(overwrite: true)`). Un proceso interrumpido nunca deja un cache corrupto.

**Invariante**: los emojis con el campo `obsoleted_by` relleno se descartan silenciosamente durante el parseo del JSON
raw.

### Cadena de carga en runtime

La carga sigue una cadena de fallback estricta. Cada nivel se intenta solo si el anterior fallo o no existe. Si todos
fallan, se retorna una lista vacia sin lanzar excepcion.

| Prioridad | Fuente                                                                           | Condicion                                                            |
|-----------|----------------------------------------------------------------------------------|----------------------------------------------------------------------|
| 1         | Cache en disco (`AppPaths.EmojiCacheFile`)                                       | Existe y es parseable                                                |
| 2         | Cache embebido en el ensamblado (`Yottacast.Core.Search.Emoji.emoji-cache.json`) | El disco fallo o no existe                                           |
| 3         | JSON raw embebido (`Yottacast.Core.Search.Emoji.emoji-data.json`)                | Los dos anteriores fallaron; parsea, escribe cache a disco y retorna |

**Invariante**: el usuario nunca ve un error si los datos de emojis no estan disponibles; la funcionalidad simplemente
no muestra resultados.

**Invariante**: cada nivel de la cadena registra tiempos de carga en los logs.

### Formato del cache compacto

Array JSON de arrays: `[char, name, [keywords], category, sortOrder]`. Los metodos `ParseRawJson` y `ParseCompactCache`
son `internal` y estan expuestos a tests via `InternalsVisibleTo`.

### Regenerar el cache de emojis

1. Borrar `Search/Emoji/emoji-cache.json` del source tree (y opcionalmente `emoji-data.json` para forzar descarga de
   datos nuevos).
2. Ejecutar la app una vez para que genere el nuevo cache en AppData.
3. Recompilar: el target `CopyEmojiCache` copiara el cache al source tree.
4. Commitear el nuevo `emoji-cache.json`.

> **Verificar en:** `Yottacast.Core/Search/Emoji/EmojiDataLoader.cs` -- metodo `LoadAsync` (cadena de fallback),
`WriteCompactCache` (escritura atomica), `ParseRawJson` (filtro `obsoleted_by`). Rutas de AppData:
`Yottacast.Core/AppPaths.cs` campo `EmojiCacheFile`. Target de copia: `Yottacast.Core/Yottacast.Core.csproj` target
`CopyEmojiCache`.

---

## 3. Arranque: migraciones y orden de inicializacion

### Contrato de migraciones

Al arrancar, la app compara la version persistida del ultimo arranque (`UserSettings.LastLaunchedVersion`) con la
version actual del ensamblado. Si difieren, se ejecuta el bloque de migraciones y se actualiza el valor persistido.

**Invariante**: en la primera instalacion, `LastLaunchedVersion` es `""`, por lo que las migraciones siempre se
ejecutan.

**Invariante**: `UserSettings.Load` siempre llama a `Save()` al final, independientemente de si el fichero existia o fue
creado. Esto normaliza el JSON en disco (anade campos nuevos con sus defaults si faltaban).

**Invariante**: la version actual se obtiene del ensamblado en runtime via
`Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)`, produciendo la forma `Major.Minor.Patch` sin
componente de build.

### Orden de arranque

El orden es determinista y cada paso depende del anterior:

| Paso | Que ocurre                                                                            | Bloqueante                                      |
|------|---------------------------------------------------------------------------------------|-------------------------------------------------|
| 1    | `RunMigrations` compara versiones y ejecuta migraciones si es necesario               | Si (sincrono)                                   |
| 2    | `MainWindowViewModel.Initialize()` dispara `CheckForUpdateAsync` como fire-and-forget | No                                              |
| 3    | `GlobalSearch.Start()` inicia todas las search sources                                | No                                              |
| 4    | `ShowWhenInstantReadyAsync` espera a que las instant sources esten listas             | Si (la ventana no se muestra hasta que termine) |

**Invariante**: las migraciones siempre se completan antes de que la comprobacion de actualizaciones y la busqueda
arranquen.

**Invariante**: el usuario nunca ve la ventana principal hasta que todas las instant search sources estan listas.

> **Verificar en:** `Yottacast/App.axaml.cs` -- metodo `OnFrameworkInitializationCompleted` (orden de llamadas),
`RunMigrations` (logica de comparacion). `Yottacast.Core/Services/UserSettings.cs` -- metodo `Load` (llamada a `Save()`
> al final).

---

## 4. Subir de version en desarrollo

1. Editar `<Version>` en **ambos** `.csproj`, manteniendolos sincronizados:
    - `Yottacast.Core/Yottacast.Core.csproj`
    - `Yottacast/Yottacast.csproj`

2. Si la nueva version requiere limpiar o migrar datos del usuario, anadir el codigo en `RunMigrations()` dentro de
   `App.axaml.cs`.

3. Ejecutar la app. En los logs aparecera:
   ```
   Version changed: '1.0.0' → '1.1.0' - running migrations
   ```
   En sucesivos arranques el mensaje no vuelve a aparecer.

4. Commitear el cambio de version. El campo `lastLaunchedVersion` en el JSON de settings se actualiza automaticamente en
   el proximo arranque.

**Invariante**: el desarrollador nunca necesita editar manualmente el fichero de settings del usuario.

> **Verificar en:** `Yottacast/App.axaml.cs` -- metodo `RunMigrations`. `Yottacast.Core/Yottacast.Core.csproj` y
`Yottacast/Yottacast.csproj` -- campo `<Version>`.

---

## 5. Comprobacion de actualizaciones

### Contrato

Al arrancar, la app comprueba una vez si existe una version mas reciente. Si la hay, muestra un banner en la ventana
principal. Si la comprobacion falla (sin red, endpoint no disponible, etc.), no se muestra nada y se registra un
warning.

> **Estado: incompleto** - la comprobacion de actualizaciones NO es funcional hoy. `UpdateChecker.UpdateApiUrl` apunta a un placeholder (`https://example.com/yottacast/latest.json`) que no devuelve datos validos, asi que `CheckAsync` siempre falla o no encuentra version y `UpdateAvailable` nunca pasa a `true`. El banner no llega a mostrarse, y aunque se mostrara, su comando `UpdateBannerClick` es un placeholder sin accion. Para activar la feature hay que reemplazar la URL y conectar la descarga. Verificar en `Yottacast.Core/Services/UpdateChecker.cs` (`UpdateApiUrl`, `CheckAsync`) y `Yottacast/ViewModels/MainWindowViewModel.cs` (`CheckForUpdateAsync`, `UpdateBannerClick`).

> **Bug conocido** - `UpdateChecker` crea su propio `HttpClient` en un campo de instancia pero no implementa `IDisposable` ni libera ese cliente. Como el servicio es singleton durante toda la vida del proceso el impacto practico es bajo, pero el recurso queda sin liberar. Verificar en `Yottacast.Core/Services/UpdateChecker.cs` (campo `_http`).

**Invariante**: el usuario nunca ve un error ni una interrupcion si la comprobacion falla.

**Invariante**: la comparacion de versiones usa `System.Version`, por lo que `1.10.0 > 1.9.0` funciona correctamente.

### Configuracion

| Parametro                     | Valor actual                                              | Donde se define                                        |
|-------------------------------|-----------------------------------------------------------|--------------------------------------------------------|
| URL del endpoint              | `https://example.com/yottacast/latest.json` (placeholder) | Constante `UpdateApiUrl` en `UpdateChecker`            |
| Timeout HTTP                  | 10 segundos                                               | Constante `UpdateCheckTimeoutSeconds` en `AppDefaults` |
| Formato de respuesta esperado | `{ "version": "1.2.0" }`                                  | --                                                     |

### Propiedades expuestas por UpdateChecker

| Propiedad         | Tipo      | Descripcion                                                             |
|-------------------|-----------|-------------------------------------------------------------------------|
| `CurrentVersion`  | `string`  | Version del ensamblado en ejecucion                                     |
| `LatestVersion`   | `string?` | Version del servidor (`null` hasta que `CheckAsync` complete con exito) |
| `UpdateAvailable` | `bool`    | `true` si la version del servidor es mayor que la actual                |

### Comportamiento del banner

Cuando `UpdateAvailable` es `true`, `MainWindowViewModel` muestra un banner con el texto
`"Yottacast {v} available -- click to download"` y expone el comando `UpdateBannerClick`. El comando es actualmente un
placeholder sin accion: la conexion a la URL de descarga esta pendiente.

**Estado**: el endpoint de actualizaciones es un placeholder. Hay que reemplazar la URL antes de que esta funcionalidad
sea operativa.

> **Verificar en:** `Yottacast.Core/Services/UpdateChecker.cs` -- constante `UpdateApiUrl`, metodo `CheckAsync`,
> propiedades. `Yottacast/ViewModels/MainWindowViewModel.cs` -- metodo `CheckForUpdateAsync`, comando `UpdateBannerClick`.
`Yottacast.Core/AppDefaults.cs` -- constante `UpdateCheckTimeoutSeconds`.
