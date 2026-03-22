# Release workflow: assets embebidos en el ensamblado

Algunos assets pesados se gestionan fuera del control de versiones y se incorporan al binario en tiempo de compilación. El `.csproj` de `Yottacast.Core` tiene targets `BeforeBuild` que los descargan o copian si no están presentes; todos usan `Condition="!Exists(...)"` para ser idempotentes.

## Assets y su origen

| Fichero | Cómo se obtiene | Cuándo regenerar |
|---|---|---|
| `Search/Calculator/math.min.js` | Descarga desde cdnjs en build | Borrar el fichero y recompilar |
| `Search/Emoji/emoji-data.json` | Descarga desde iamcal/emoji-data en build | Borrar el fichero y recompilar |
| `Search/Emoji/emoji-cache.json` | Copiado desde AppData en build (ver abajo) | Borrar el fichero y seguir el flujo de emoji |

## Ciclo de vida del emoji cache

`emoji-cache.json` es una representación compacta de `emoji-data.json` (~100-150 KB vs ~1.25 MB) que permite un arranque instantáneo sin parsear el JSON raw. Se genera en runtime y se promueve al ensamblado mediante el flujo siguiente:

```
1. dotnet run (primera vez)
      └─ no hay embedded cache ni disco → parsea emoji-data.json → escribe AppData/.../emoji-cache.json

2. dotnet build (tras haber ejecutado la app al menos una vez)
      └─ target CopyEmojiCache: copia AppData/.../emoji-cache.json → Search/Emoji/emoji-cache.json
                                 (solo si destino no existe)
      └─ EmbeddedResource condicional: lo embute en el ensamblado

3. git add Search/Emoji/emoji-cache.json && git commit
      └─ el repo queda con el cache; futuros clones lo tienen desde el primer build

4. Arranque en producción
      └─ EmojiDataLoader encuentra el embedded cache → carga directa, sin tocar disco
```

El target `CopyEmojiCache` resuelve la ruta de AppData por plataforma. Ver el `.csproj` para las rutas exactas.

Tanto `emoji-cache.json` como `emoji-data.json` se declaran como `EmbeddedResource` condicionales en el `.csproj`; si el fichero no existe en el source tree, el ítem se omite y el build no falla.

### Cadena de carga en runtime (EmojiDataLoader)

`EmojiDataLoader.LoadAsync` sigue una cadena de tres niveles de fallback, en orden de preferencia:

1. **Caché en disco** (`AppData/.../Yottacast/emoji-cache.json`) — si existe y es parseable, se usa directamente y se retorna sin tocar el ensamblado.
2. **Caché embebida** (`Yottacast.Core.Search.Emoji.emoji-cache.json`) — si el disco falló o no existe, se intenta la versión embebida en el ensamblado.
3. **JSON raw embebido** (`Yottacast.Core.Search.Emoji.emoji-data.json`) — último recurso: parsea el JSON completo, escribe el caché en disco y retorna.

Si todos los niveles fallan, `LoadAsync` retorna una lista vacía (sin excepción). Cada nivel registra tiempos de carga en los logs.

La escritura del caché en disco es atómica: primero se escribe a `emoji-cache.json.tmp` y luego se mueve con `File.Move(overwrite: true)`, evitando ficheros corruptos si el proceso termina durante la escritura.

Durante el parseo del JSON raw, los emojis con el campo `obsoleted_by` relleno se descartan silenciosamente (se omiten versiones obsoletas/generizadas).

### Regenerar el cache de emojis

Si se actualiza `emoji-data.json` (borrándolo para que el target lo descargue de nuevo):

1. Borrar `Search/Emoji/emoji-cache.json` del repo.
2. Ejecutar la app una vez para que genere el nuevo cache en AppData.
3. Hacer build: el target lo copiará al source tree.
4. Commitear el nuevo `emoji-cache.json`.

## Versiones y actualizaciones

### Qué ocurre al arrancar

Al iniciar la app, `App.RunMigrations()` compara `UserSettings.LastLaunchedVersion` con
`UpdateChecker.CurrentVersion` (leído del ensamblado en runtime vía
`Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)`, que produce la forma `Major.Minor.Patch`
sin el componente de build). Si difieren:

1. Se ejecuta el bloque de migraciones en `RunMigrations()` (`App.axaml.cs`).
2. Se actualiza `LastLaunchedVersion` al valor actual y se persiste con `UserSettings.Save()`.

En la primera instalación `LastLaunchedVersion` es `""`, por lo que las migraciones siempre
se ejecutan al estrenar la app.

El orden de arranque en `OnFrameworkInitializationCompleted` es relevante:
`RunMigrations` termina de forma síncrona → `mainWindowViewModel.Initialize()` dispara
`CheckForUpdateAsync()` como fire-and-forget → `globalSearch.Start()` inicia las sources →
`ShowWhenInstantReadyAsync` espera a que las instant sources estén listas antes de mostrar la ventana.
Las migraciones siempre se completan antes de que el update check y la búsqueda arranquen.

A continuación, `MainWindowViewModel.Initialize()` dispara `CheckForUpdateAsync()` como
fire-and-forget. Llama al endpoint `UpdateChecker.UpdateApiUrl` con timeout de 10 s. Si la respuesta
contiene una versión mayor a la actual (comparación con `System.Version`, por lo que
`1.10.0 > 1.9.0` funciona correctamente), se muestra el banner de actualización en la ventana
principal. Si la llamada falla (sin red, endpoint no configurado, etc.) se registra un warning y
no se muestra nada.

### Subir de versión en desarrollo

1. Editar `<Version>` en **ambos** `.csproj`, manteniéndolos sincronizados:
   - `Yottacast.Core/Yottacast.Core.csproj`
   - `Yottacast/Yottacast.csproj`

2. Si la nueva versión requiere limpiar o migrar datos del usuario, añadir el código necesario en
   `RunMigrations()` dentro de `App.axaml.cs`. El bloque ya incluye un comentario que señala dónde añadirlo.

3. Ejecutar la app. En los logs aparecerá:
   ```
   Version changed: '1.0.0' → '1.1.0' — running migrations
   ```
   En sucesivos arranques el mensaje no vuelve a aparecer.

4. Commitear el cambio de versión. El campo `lastLaunchedVersion` en el JSON de settings de cada
   usuario se actualiza automáticamente en el próximo arranque; no hay que tocar el fichero a mano.

### Checker de actualizaciones

`UpdateChecker` (ver `Yottacast.Core/Services/UpdateChecker.cs`) llama una vez al arranque al
endpoint definido en `UpdateApiUrl`. Respuesta esperada: `{ "version": "1.2.0" }`. El endpoint
es un placeholder; reemplazarlo con la URL real cuando esté disponible.

`UpdateChecker` expone tres propiedades: `CurrentVersion` (versión del ensamblado en ejecución),
`LatestVersion` (versión del servidor, `null` hasta que `CheckAsync` complete con éxito), y
`UpdateAvailable` (booleano derivado de la comparación). `HttpClient` se crea internamente con
timeout de 10 s; no se reutiliza ni se inyecta desde fuera.

### Banner de actualización

Cuando `UpdateChecker.UpdateAvailable` es `true`, `MainWindowViewModel` activa `UpdateAvailable`
y rellena `UpdateBannerText` con `"Yottacast {LatestVersion} available — click to download"`,
lo que muestra una franja clicable al pie de la ventana principal.
El comando `UpdateBannerClickCommand` es un placeholder — conectarlo a la URL de descarga en el
siguiente plan.
