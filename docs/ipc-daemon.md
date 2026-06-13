# IPC Daemon (Yottacast.Ipc)

## Para qué sirve

`Yottacast.Ipc` es un proceso headless que expone toda la lógica de `Yottacast.Core` vía gRPC sobre un Unix domain socket. El propósito es permitir que una futura UI nativa en Swift se conecte al Core sin importar ensamblados .NET.

El daemon **no tiene ventana**, no aparece en el Dock y no registra hotkeys globales. Es un proceso de servidor puro.

La app Avalonia actual sigue funcionando directamente contra el Core sin usar IPC; el daemon convive con ella como proyecto independiente.

> **Estado: incompleto** - el daemon es un esqueleto funcional pero no tiene cliente real (la UI Swift no existe todavia). Registra un subconjunto reducido de fuentes respecto a la GUI y arrastra varios gaps de mapeo y robustez documentados mas abajo. No debe considerarse listo para produccion.

## Modelo de procesos

```
Swift UI (futuro)          Avalonia (actual)
      │                          │
      │ gRPC / Unix socket       │ referencia directa
      ▼                          ▼
 yottacast-core             Yottacast.Core
 (Yottacast.Ipc)
```

- **`yottacast-core`** - el binario del daemon, lanzado por la app Swift al arrancar.
- **PID file** (`~/.cache/yottacast/core.pid`) - evita instancias duplicadas. El guard de arranque solo trata el PID como un daemon vivo si ademas el `ProcessName` de ese PID coincide con el del propio proceso; asi un PID reciclado por el SO para otro proceso no impide arrancar. Verificar en `Yottacast.Ipc/Program.cs` (guard de PID file).
- **Unix socket** (`~/.cache/yottacast/core.sock`) - único punto de comunicación.

## Estructura del proyecto

```
Yottacast.sln
├── Yottacast/                   ← sin cambios (UI Avalonia)
├── Yottacast.Core/              ← sin cambios (lógica)
├── Yottacast.Core.Tests/        ← sin cambios
├── Yottacast.Ipc/               ← daemon headless
│   ├── Program.cs               ← host Kestrel, DI, PID guard, shutdown
│   ├── Proto/
│   │   ├── search.proto
│   │   ├── settings.proto
│   │   ├── icons.proto
│   │   └── lifecycle.proto
│   ├── Services/
│   │   ├── SearchGrpcService.cs
│   │   ├── SettingsGrpcService.cs
│   │   ├── IconGrpcService.cs
│   │   └── LifecycleGrpcService.cs
│   └── Mapping/
│       ├── ResultMapper.cs
│       └── SettingsMapper.cs
└── Yottacast.Ipc.Tests/         ← tests de mappers
```

## Servicios gRPC

### SearchService

Expone la búsqueda de `GlobalSearch`.

| RPC | Tipo | Descripción |
|-----|------|-------------|
| `SearchInstant` | unario | Devuelve resultados de fuentes instant |
| `SearchDeferred` | server-streaming | Emite snapshots progresivos de fuentes deferred (ficheros, diccionario), termina con `isSearching=false` |
| `Activate` | unario | Ejecuta la acción del resultado: default, copy o favorite |
| `Navigate` | unario | Navega dentro de un resultado grid (emoji, conversion) con direcciones LEFT/RIGHT/UP/DOWN |

`SearchGrpcService` mantiene un **registry** (`ConcurrentDictionary<string, BaseResultItemViewModel>`) con el último snapshot de resultados. Las claves son strings secuenciales (`"0"`, `"1"`, ...). `Activate` y `Navigate` buscan el resultado por su ID.

#### Token de generación (validez de los IDs)

Como los IDs son secuenciales y se reutilizan entre snapshots, cada `SearchResponse` lleva un campo `generation` (entero monotónico que se incrementa en cada snapshot). Un `Activate`/`Navigate` debe reenviar el `generation` del snapshot al que pertenece su `result_id`. Si no coincide con el snapshot actual, el RPC se rechaza con `FAILED_PRECONDITION` en vez de ejecutar otro resultado que casualmente reusa el mismo ID tras un swap. El cliente debe re-lanzar la búsqueda y reintentar con el nuevo token. El mensaje terminal de `SearchDeferred` (`is_searching=false`) también lleva el `generation` actual.

> **Verificar en:** `Yottacast.Ipc/Services/SearchGrpcService.cs` (`BuildResponse` asigna `generation` con `Interlocked.Increment`; `RequireCurrentGeneration` valida en `Activate` y `Navigate`) y `Yottacast.Ipc/Proto/search.proto` (campos `generation` en `SearchResponse`, `ActivateRequest`, `NavigateRequest`).

El campo `icon_id` en `ResultMessage` es el path del recurso (p. ej. `/Applications/Safari.app`), que se pasa directamente a `IconService.GetIcon`.

#### Fuentes registradas en el daemon

El daemon registra en su DI un subconjunto distinto al de la GUI. Solo expone estas fuentes (ver `Yottacast.Ipc/Program.cs`):

- Instant: `ApplicationSearch`, `CalculatorSearch`, `EmojiSearch`, `WebSearchSource`.
- Deferred: `UserDocumentSearch`, `DictionarySource`.

> **Estado: incompleto** - el daemon no registra varias fuentes que si tiene la GUI (`LocalPathSearch`, `UrlSearch`, `DateSearch`, `NewlyInstalledAppsSource`, `SystemSettingsSearch`). Cualquier cliente IPC ve menos resultados que la GUI Avalonia. Verificar en `Yottacast.Ipc/Program.cs` (registro de `IInstantSearchSource`/`IDeferredSearchSource`) frente a `Yottacast/App.axaml.cs` -> `BuildServices`.

> **Bug conocido** - `ResultMapper.DetermineType` clasifica el `type` de un `ResultItemViewModel` por su `Category` y tiene un fallback silencioso a `"app"` para cualquier categoria no contemplada. Un resultado de una fuente futura con categoria nueva se etiqueta erroneamente como `"app"`, lo que ademas hace que su `icon_id` se rellene con el subtitulo (path) en vez del icono real. Verificar en `Yottacast.Ipc/Mapping/ResultMapper.cs` -> `DetermineType`.

### SettingsService

| RPC | Tipo | Descripción |
|-----|------|-------------|
| `GetSettings` | unario | Devuelve el estado actual de `UserSettings` |
| `UpdateSettings` | unario | Aplica un `SettingsMessage` completo, guarda en disco y notifica |
| `WatchSettings` | server-streaming | Emite el settings actualizado cada vez que cambia (eventos `SearchSettingsChanged` o `AppDirectoriesChanged`) |

#### Mapeo de settings

`UserSettings.FileSearchVisibility` es un enum de tres valores (`Disabled`, `Always`, `ModeOnly`). El proto lo representa con un enum equivalente `SearchVisibility` en el campo `file_search_visibility` (tag 11), mapeado 1:1 en ambas direcciones por `SettingsMapper`, de modo que un round-trip conserva `ModeOnly` sin aplanarlo a `Always`. Verificar en `Yottacast.Ipc/Mapping/SettingsMapper.cs` (`ToProtoVisibility`/`FromProtoVisibility`) y `Yottacast.Core/Search/SearchSourceVisibility.cs`.

`UpdateSettings` compara `AppDirectories` antes y despues de aplicar el mensaje; si cambian, dispara `AppDirectoriesChanged` ademas de `SearchSettingsChanged`, para que un cliente que reconfigura los directorios de apps por IPC provoque rescaneo. Verificar en `Yottacast.Ipc/Services/SettingsGrpcService.cs` (`UpdateSettings`).

> **Estado: incompleto** - el proto no representa el estado de clipboard. El campo huerfano `clipboard_history_enabled` (tag 9) fue eliminado y su tag reservado (`reserved 9`). La fuente real de la verdad (`UserSettings.ClipboardSearchVisibility`, enum de tres valores, mas `ClipboardHotkey`/`ClipboardHistoryMaxEntries`/`ClipboardHistoryMaxDays`) todavia no se expone por IPC, asi que un cliente no puede configurar el historial de portapapeles. Verificar en `Yottacast.Ipc/Proto/settings.proto` (tag 9 reservado) y `Yottacast.Ipc/Mapping/SettingsMapper.cs` (sin uso de `ClipboardSearchVisibility`).

### IconService

| RPC | Tipo | Descripción |
|-----|------|-------------|
| `GetIcon` | unario | Devuelve el PNG de un icono por `icon_id` + `type` ("app"/"file"/"badge"). Si no está listo, devuelve `available=false` y lanza la carga async |
| `WatchIconsLoaded` | server-streaming | Emite un evento cuando algún icono pasa a estar disponible; el cliente puede re-solicitar los iconos que estaban pendientes |

### LifecycleService

| RPC | Tipo | Descripción |
|-----|------|-------------|
| `GetStatus` | unario | Estado actual: `STARTING`, `INSTANT_READY` o `FULLY_READY` |
| `WatchStatus` | server-streaming | Emite transiciones de estado según arrancan las fuentes |
| `Shutdown` | unario | Para el daemon limpiamente (borra PID file y socket) |

## Secuencia de arranque

```
Swift lanza yottacast-core
  → verifica PID file (sale solo si el PID existe Y su ProcessName coincide con el nuestro)
  → escribe PID file
  → Kestrel escucha en Unix socket
  → globalSearch.Start() en background

Swift conecta:
  → WatchStatus() → STARTING
  → INSTANT_READY → la UI puede mostrarse (apps, calc, emoji listos)
  → FULLY_READY   → fuentes deferred disponibles

Al salir Swift:
  → Shutdown()
  → globalSearch.Stop(), borra PID file y socket
```

## ClipboardService en el daemon

El daemon nunca toca el portapapeles del sistema. Cuando un `Activate` desencadena una acción de copia, el texto se captura en un colector per-call (`AsyncLocal<StrongBox<string?>>` instalado por la llamada `Activate` en curso) y se devuelve en `ActivateResponse.clipboard_text`. La UI Swift es responsable de poner ese texto en el clipboard nativo. Como cada `Activate` instala su propio colector en su flujo asíncrono, dos `Activate` concurrentes nunca se mezclan el texto copiado.

El callback de lectura del portapapeles del daemon devuelve siempre `null` (`read: () => Task.FromResult<string?>(null)`), por lo que las features del Core que dependen de leer el clipboard (deteccion de URLs o rutas al abrir) no funcionan via IPC.

## Limitaciones de robustez conocidas

El texto copiado en `Activate` se aisla por llamada mediante un colector `AsyncLocal<StrongBox<string?>>`: el callback de copia compartido escribe en el box que instalo el `Activate` que corre en ese flujo asincrono, asi que dos `Activate` concurrentes no se mezclan el texto. Verificar en `Yottacast.Ipc/Services/SearchGrpcService.cs` (`_copyCollector`, `Activate`) y `Yottacast.Ipc.Tests/Services/SearchGrpcServiceTests.cs`.

Las escrituras sobre los server streams de notificacion (`WatchSettings`, `WatchIconsLoaded`, `WatchStatus`) se serializan por cliente: cada suscriptor tiene su propio `Channel<T>` y el unico escritor de su `IServerStreamWriter` es su propio metodo `Watch*`, que consume el canal en bucle. Los broadcasts solo hacen `TryWrite` al canal (no tocan el stream), por lo que gRPC nunca ve escrituras concurrentes sobre el mismo stream aunque lleguen rafagas de eventos solapadas. Los handlers de broadcast construyen el mensaje dentro de try/catch para que una excepcion al mapear no tumbe el proceso. Verificar en `Yottacast.Ipc/Services/SettingsGrpcService.cs`, `IconGrpcService.cs`, `LifecycleGrpcService.cs`.

## Cómo arrancar el daemon

```bash
cd Yottacast.Ipc
dotnet run
```

O con el binario publicado:

```bash
dotnet publish -c Release -r osx-arm64 --self-contained
./bin/Release/net9.0/osx-arm64/publish/yottacast-core
```

## Cómo probar con grpcurl

```bash
# Instalar si no está disponible
brew install grpcurl

# Variables de entorno
SOCK=~/.cache/yottacast/core.sock
PROTO_DIR="<ruta-del-repo>/Yottacast.Ipc/Proto"

# Estado del daemon
grpcurl -plaintext -unix "$SOCK" \
  -import-path "$PROTO_DIR" -proto lifecycle.proto \
  yottacast.LifecycleService/GetStatus

# Búsqueda instant
grpcurl -plaintext -unix "$SOCK" \
  -import-path "$PROTO_DIR" -proto search.proto \
  -d '{"query": "safari", "limit": 5}' \
  yottacast.SearchService/SearchInstant

# Leer settings
grpcurl -plaintext -unix "$SOCK" \
  -import-path "$PROTO_DIR" -proto settings.proto \
  yottacast.SettingsService/GetSettings

# Obtener icono de Safari
grpcurl -plaintext -unix "$SOCK" \
  -import-path "$PROTO_DIR" -proto icons.proto \
  -d '{"icon_id": "/Applications/Safari.app", "type": "app"}' \
  yottacast.IconService/GetIcon

# Apagar el daemon
grpcurl -plaintext -unix "$SOCK" \
  -import-path "$PROTO_DIR" -proto lifecycle.proto \
  yottacast.LifecycleService/Shutdown
```

## Tests

Los tests de mappers viven en `Yottacast.Ipc.Tests/`:

```bash
cd Yottacast.Ipc.Tests && dotnet test
```

Cubren parcialmente:
- `ResultMapper` - conversión de tipos de ViewModel a `ResultMessage`.
- `SettingsMapper` - algunos escalares, `window_x/window_y` nullable, round-trip basico, round-trip de `FileSearchVisibility` (incluido `ModeOnly`) y ausencia del campo eliminado `clipboard_history_enabled`.
- `SearchGrpcService` - token de generación (incremento por snapshot, rechazo `FAILED_PRECONDITION` de `Activate`/`Navigate` con generación stale) y aislamiento per-call del texto copiado entre `Activate` concurrentes. Verificar en `Yottacast.Ipc.Tests/Services/SearchGrpcServiceTests.cs`.

> **Estado: incompleto** - los tests de `SettingsMapper` aun no ejercitan las listas (`WebSearchEngines`, `SearchFolders`, etc.) ni todos los escalares, por lo que pueden quedar gaps de mapeo sin detectar. Verificar en `Yottacast.Ipc.Tests/Mapping/SettingsMapperTests.cs`.

> **Verificar en:** `Yottacast.Ipc/Program.cs` (startup, PID guard y DI con el subconjunto de fuentes), `Yottacast.Ipc/Services/SearchGrpcService.cs` (registry + streaming), `Yottacast.Ipc/Services/LifecycleGrpcService.cs` (estados de arranque), `Yottacast.Ipc/Mapping/SettingsMapper.cs` y `ResultMapper.cs` (gaps de mapeo), `Yottacast.Core/AppPaths.cs` (rutas IPC: `IpcPidFile`, `IpcSocket`).
