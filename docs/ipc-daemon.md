# IPC Daemon (Yottacast.Ipc)

## Para qué sirve

`Yottacast.Ipc` es un proceso headless que expone toda la lógica de `Yottacast.Core` vía gRPC sobre un Unix domain socket. El propósito es permitir que una futura UI nativa en Swift se conecte al Core sin importar ensamblados .NET.

El daemon **no tiene ventana**, no aparece en el Dock y no registra hotkeys globales. Es un proceso de servidor puro.

La app Avalonia actual sigue funcionando directamente contra el Core sin usar IPC — el daemon convive con ella como proyecto independiente.

## Modelo de procesos

```
Swift UI (futuro)          Avalonia (actual)
      │                          │
      │ gRPC / Unix socket       │ referencia directa
      ▼                          ▼
 yottacast-core             Yottacast.Core
 (Yottacast.Ipc)
```

- **`yottacast-core`** — el binario del daemon, lanzado por la app Swift al arrancar.
- **PID file** (`~/.cache/yottacast/core.pid`) — evita instancias duplicadas.
- **Unix socket** (`~/.cache/yottacast/core.sock`) — único punto de comunicación.

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
| `SearchInstant` | unario | Devuelve resultados de fuentes instant (apps, calc, emoji, web) |
| `SearchDeferred` | server-streaming | Emite snapshots progresivos de fuentes deferred (ficheros, diccionario), termina con `isSearching=false` |
| `Activate` | unario | Ejecuta la acción del resultado: default, copy o favorite |
| `Navigate` | unario | Navega dentro de un resultado grid (emoji, conversion) con direcciones LEFT/RIGHT/UP/DOWN |

`SearchGrpcService` mantiene un **registry** (`ConcurrentDictionary<string, BaseResultItemViewModel>`) con el último snapshot de resultados. Las claves son strings secuenciales (`"0"`, `"1"`, ...). `Activate` y `Navigate` buscan el resultado por su ID.

El campo `icon_id` en `ResultMessage` es el path del recurso (p. ej. `/Applications/Safari.app`), que se pasa directamente a `IconService.GetIcon`.

### SettingsService

| RPC | Tipo | Descripción |
|-----|------|-------------|
| `GetSettings` | unario | Devuelve el estado actual de `UserSettings` |
| `UpdateSettings` | unario | Aplica un `SettingsMessage` completo, guarda en disco y notifica |
| `WatchSettings` | server-streaming | Emite el settings actualizado cada vez que cambia (eventos `SearchSettingsChanged` o `AppDirectoriesChanged`) |

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

Cubren:
- `ResultMapper` — conversión de todos los tipos de ViewModel a `ResultMessage` (app, calc, emoji, conversion, dict)
- `SettingsMapper` — round-trip de `UserSettings` ↔ `SettingsMessage`, campos nullable, listas

> **Verificar en:** `Yottacast.Ipc/Program.cs` (startup y DI), `Yottacast.Ipc/Services/SearchGrpcService.cs` (registry + streaming), `Yottacast.Ipc/Services/LifecycleGrpcService.cs` (estados de arranque), `Yottacast.Core/AppPaths.cs` (rutas IPC).
