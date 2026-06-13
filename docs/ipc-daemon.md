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
- **PID file** (`~/.cache/yottacast/core.pid`) - evita instancias duplicadas.
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

#### Gaps de mapeo de settings

> **Bug conocido** - el campo `clipboard_history_enabled` (numero 9 en `settings.proto`) esta huerfano: `SettingsMapper` no lo lee ni lo escribe en ninguna direccion. Un cliente IPC siempre lee `false` y no puede activar ni desactivar el historial de portapapeles. La fuente real de la verdad es `UserSettings.ClipboardSearchVisibility` (enum de tres valores), que el proto no representa. Verificar en `Yottacast.Ipc/Mapping/SettingsMapper.cs` (`ToProto`/`ApplyProto`, ningun uso de `ClipboardSearchVisibility`) y `Yottacast.Ipc/Proto/settings.proto` (campo 9).

> **Bug conocido** - `UserSettings.FileSearchVisibility` es un enum de tres valores (`Disabled`, `Always`, `ModeOnly`) pero el proto lo expone como el bool `enable_file_search`. `ToProto` mapea `!= Disabled` a `true`, y `ApplyProto` mapea `true` a `Always`. Un round-trip de un settings con `ModeOnly` lo convierte en `Always` y lo persiste, perdiendo el valor original. Verificar en `Yottacast.Ipc/Mapping/SettingsMapper.cs` (`EnableFileSearch`) y `Yottacast.Core/Search/SearchSourceVisibility.cs`.

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
  → verifica PID file (sale si ya corre)
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

El daemon nunca toca el portapapeles del sistema. Cuando un `Activate` desencadena una acción de copia, el texto se captura en `SearchGrpcService._lastCopiedText` y se devuelve en `ActivateResponse.clipboard_text`. La UI Swift es responsable de poner ese texto en el clipboard nativo.

El callback de lectura del portapapeles del daemon devuelve siempre `null` (`read: () => Task.FromResult<string?>(null)`), por lo que las features del Core que dependen de leer el clipboard (deteccion de URLs o rutas al abrir) no funcionan via IPC.

## Limitaciones de robustez conocidas

> **Bug conocido** - `_lastCopiedText` en `SearchGrpcService` es estado compartido por todo el servicio (singleton). Si dos `Activate` con accion de copia se ejecutan concurrentemente, el texto devuelto en una respuesta puede ser el de la otra llamada. Verificar en `Yottacast.Ipc/Services/SearchGrpcService.cs` (`Activate`, campo `_lastCopiedText`).

> **Bug conocido** - las escrituras sobre los server streams de notificacion (`WatchSettings`, `WatchIconsLoaded`, `WatchStatus`) no se serializan por cliente. `SettingsGrpcService` e `IconGrpcService` snapshotean la lista de watchers bajo lock pero hacen `WriteAsync` sin coordinar dos broadcasts simultaneos hacia el mismo stream; `LifecycleGrpcService.Transition` ademas escribe fire-and-forget (`_ = writer.WriteAsync(...)`). gRPC no permite escrituras concurrentes sobre un mismo `IServerStreamWriter`, asi que rafagas de eventos solapadas pueden corromper el stream. Verificar en `Yottacast.Ipc/Services/SettingsGrpcService.cs`, `IconGrpcService.cs`, `LifecycleGrpcService.cs`.

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
- `SettingsMapper` - algunos escalares, `window_x/window_y` nullable y un round-trip basico.

> **Estado: incompleto** - los tests de `SettingsMapper` no cubren lo que su nombre sugiere. No ejercitan los campos con gaps conocidos (`clipboard_history_enabled` huerfano, perdida de `ModeOnly` en `FileSearchVisibility`) ni las listas (`WebSearchEngines`, `SearchFolders`, etc.), por lo que los bugs de mapeo pasan desapercibidos. Verificar en `Yottacast.Ipc.Tests/Mapping/SettingsMapperTests.cs`.

> **Verificar en:** `Yottacast.Ipc/Program.cs` (startup, PID guard y DI con el subconjunto de fuentes), `Yottacast.Ipc/Services/SearchGrpcService.cs` (registry + streaming), `Yottacast.Ipc/Services/LifecycleGrpcService.cs` (estados de arranque), `Yottacast.Ipc/Mapping/SettingsMapper.cs` y `ResultMapper.cs` (gaps de mapeo), `Yottacast.Core/AppPaths.cs` (rutas IPC: `IpcPidFile`, `IpcSocket`).
