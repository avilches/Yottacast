# Core IPC Daemon — Design Spec

## Contexto

Yottacast tiene toda la lógica de búsqueda en `Yottacast.Core` (biblioteca .NET sin dependencias de UI).
El objetivo es exponer ese Core como un **daemon headless** accesible vía gRPC, de modo que una futura UI en Swift pueda conectarse sin importar ensamblados .NET.

El front Avalonia actual no se migra: sigue funcionando directamente contra el Core como hasta ahora. El daemon convive con él. Una vez la UI Swift esté lista, Avalonia se retira.

## Alcance de este spec

Crear el proyecto `Yottacast.Ipc` — un proceso headless que expone `Yottacast.Core` vía gRPC sobre Unix domain socket. No incluye el frontend Swift.

---

## Modelo de procesos

- **Core daemon** (`yottacast-core`): proceso .NET headless, sin ventana, sin icono en el Dock. Lanzado por la app Swift al arrancar. Registra un PID file para evitar instancias duplicadas.
- **UI Swift** (futuro): app principal macOS. Registra el hotkey global. Gestiona la ventana. Conecta al daemon vía socket local.
- **Avalonia app** (actual): sigue funcionando sin cambios, no usa IPC.

El hotkey global y el show/hide de ventana son responsabilidad del cliente Swift — el daemon no tiene UI ni conocimiento de ventanas.

---

## Transporte

**Unix domain socket** en la ruta `AppPaths.IpcSocket` (a definir como `~/.cache/yottacast/core.sock`).

- Sin puerto TCP: evita conflictos y es más seguro (solo procesos locales).
- PID file en `AppPaths.IpcPidFile` (`~/.cache/yottacast/core.pid`).
- El daemon verifica al arrancar si ya hay una instancia corriendo; si la hay, sale.

---

## Estructura del proyecto

```
Yottacast.sln
├── Yottacast/              ← sin cambios
├── Yottacast.Core/         ← sin cambios
├── Yottacast.Core.Tests/   ← sin cambios
└── Yottacast.Ipc/          ← NUEVO
    ├── Yottacast.Ipc.csproj
    ├── Program.cs
    ├── Proto/
    │   ├── search.proto
    │   ├── settings.proto
    │   ├── icons.proto
    │   └── lifecycle.proto
    ├── Services/
    │   ├── SearchGrpcService.cs
    │   ├── SettingsGrpcService.cs
    │   ├── IconGrpcService.cs
    │   └── LifecycleGrpcService.cs
    └── Mapping/
        ├── ResultMapper.cs
        └── SettingsMapper.cs
```

**Dependencias del `.csproj`:**
- `Grpc.AspNetCore` (servidor gRPC sobre Kestrel)
- `Google.Protobuf`, `Grpc.Tools` (codegen desde `.proto`)
- Referencia a `Yottacast.Core`

---

## Servicios gRPC

### SearchService

```protobuf
service SearchService {
  rpc SearchInstant(SearchRequest) returns (SearchResponse);
  rpc SearchDeferred(SearchRequest) returns (stream SearchResponse);
  rpc Activate(ActivateRequest) returns (ActivateResponse);
  rpc Navigate(NavigateRequest) returns (NavigateResponse);
}

message SearchRequest {
  string query = 1;
  int32 limit = 2;
}

message SearchResponse {
  repeated ResultMessage results = 1;
  string hint = 2;           // sugerencia de calculadora (puede estar vacía)
  bool is_searching = 3;     // true mientras hay deferred en curso
}

message ResultMessage {
  string id = 1;             // ID opaco dentro de la sesión: "0", "1", ...
  string type = 2;           // "app", "calc", "emoji_grid", "web", "file", "dict", "conversion"
  string title = 3;
  string subtitle = 4;
  string category = 5;
  string icon_id = 6;        // clave para IconService.GetIcon
  double score = 7;
  bool bypass_limit = 8;
  bool paste_after_activate = 9;

  // Solo para tipo "emoji_grid"
  repeated EmojiCellMessage emoji_cells = 10;
  int32 selected_emoji_index = 11;

  // Solo para tipo "conversion"
  ConversionMessage conversion = 12;

  // Solo para tipo "dict"
  repeated DictionaryDefinitionMessage definitions = 13;
}

message EmojiCellMessage {
  string char = 1;
  string name = 2;
  string category = 3;
  repeated string keywords = 4;
  int32 section = 5;         // 0=Favorite, 1=MostUsed, 2=Default
  int32 usage_count = 6;
  bool is_favorite = 7;
  bool is_placeholder = 8;
}

message ConversionMessage {
  string from_short = 1;
  string from_long = 2;
  string to_short = 3;
  string to_long = 4;
  string norm_from_short = 5;
  string norm_from_long = 6;
  bool from_was_normalized = 7;
  int32 selected_cell = 8;   // 0=To, 1=NormFrom, 2=OrigFrom
}

message DictionaryDefinitionMessage {
  string part_of_speech = 1;
  string definition = 2;
  string example = 3;
  string example_translation = 4;
}

enum ActionType {
  DEFAULT = 0;
  COPY = 1;
  FAVORITE = 2;
}

message ActivateRequest {
  string result_id = 1;
  ActionType action = 2;
  int32 emoji_index = 3;     // solo si type="emoji_grid"
}

message ActivateResponse {
  bool paste_after_activate = 1;
  string clipboard_text = 2; // texto copiado (para que Swift lo ponga en clipboard)
}

enum Direction {
  LEFT = 0;
  RIGHT = 1;
  UP = 2;
  DOWN = 3;
}

message NavigateRequest {
  string result_id = 1;
  Direction direction = 2;
  int32 current_index = 3;
}

message NavigateResponse {
  bool consumed = 1;
  int32 new_index = 2;
}
```

### SettingsService

```protobuf
service SettingsService {
  rpc GetSettings(Empty) returns (SettingsMessage);
  rpc UpdateSettings(UpdateSettingsRequest) returns (Empty);
  rpc WatchSettings(Empty) returns (stream SettingsMessage);
}
```

`SettingsMessage` mapea 1:1 con `UserSettings`. `UpdateSettings` recibe el objeto completo (no patch — los settings no cambian frecuentemente). `WatchSettings` es un server-streaming que emite el estado actualizado cada vez que `UserSettings.SearchSettingsChanged` o `AppDirectoriesChanged` disparan.

### IconService

```protobuf
service IconService {
  rpc GetIcon(IconRequest) returns (IconResponse);
  rpc WatchIconsLoaded(Empty) returns (stream IconLoadedEvent);
}

message IconRequest {
  string icon_id = 1;   // path de app o fichero (emoji no necesita icono — se renderiza como texto)
  string type = 2;      // "app", "file", "badge"
}

message IconResponse {
  bytes png_data = 1;   // vacío si no disponible aún (icono en carga)
  bool available = 2;
}

message IconLoadedEvent {
  string icon_id = 1;   // el icono cuyo PNG ya está listo en cache
}
```

Si el icono no está listo (carga asíncrona), `available=false`. Swift puede suscribirse a `WatchIconsLoaded` para saber cuándo reintentar `GetIcon` sin polling.

### LifecycleService

```protobuf
service LifecycleService {
  rpc GetStatus(Empty) returns (StatusResponse);
  rpc WatchStatus(Empty) returns (stream StatusResponse);
  rpc Shutdown(Empty) returns (Empty);
}

message StatusResponse {
  enum State {
    STARTING = 0;
    INSTANT_READY = 1;   // fuentes instant listas → UI puede mostrarse
    FULLY_READY = 2;     // todas las fuentes listas
  }
  State state = 1;
}
```

---

## Gestión del resultado registry

`SearchGrpcService` es **singleton**. Mantiene un `ConcurrentDictionary<string, BaseResultItemViewModel>` con los resultados del último snapshot. Las claves son strings secuenciales (`"0"`, `"1"`, ...).

Cada vez que llega un nuevo snapshot (instant o deferred), el registry se reemplaza atómicamente. `Activate(result_id)` busca en el registry y ejecuta el delegate correspondiente.

Esto es seguro porque el usuario solo puede activar un resultado del snapshot actual.

---

## ClipboardService en el daemon

En el daemon no hay Avalonia, por lo que `ClipboardService.Initialize()` se llama con un callback que **no** copia al clipboard del sistema — en su lugar captura el texto y lo devuelve en `ActivateResponse.clipboard_text`. Swift recibe ese texto y lo pone en el clipboard nativo. Esto mantiene el clipboard fuera del proceso .NET headless.

---

## AppPaths nuevas

Añadir a `AppPaths.cs`:

```csharp
public static readonly string IpcSocket    // ~/.cache/yottacast/core.sock
public static readonly string IpcPidFile   // ~/.cache/yottacast/core.pid
```

---

## Startup sequence

```
Swift lanza yottacast-core
  → Daemon verifica PID file (sale si ya corre)
  → Escribe PID file
  → Inicia Kestrel en Unix socket
  → Llama globalSearch.Start() (fuego y olvida)
  → Disponible para conexiones

Swift conecta al socket
  → Llama WatchStatus() → recibe STARTING
  → Recibe INSTANT_READY → muestra ventana al usuario
  → Recibe FULLY_READY → deferred sources disponibles

Al salir Swift:
  → Llama Shutdown()
  → Daemon llama globalSearch.Stop(), borra PID file, sale
```

---

## Invariantes

- El daemon nunca tiene ventana ni icono en Dock.
- `Activate` solo funciona si el `result_id` existe en el registry actual — responde con error gRPC `NOT_FOUND` si no.
- `ClipboardService` en el daemon nunca toca el portapapeles del sistema.
- El daemon no registra hotkeys globales.
- Si el daemon recibe una señal SIGTERM, ejecuta el mismo shutdown graceful.

---

## Verificación

```bash
# Arrancar el daemon
cd Yottacast.Ipc && dotnet run

# Verificar que escucha en el socket
ls -la ~/.cache/yottacast/core.sock

# Probar con grpcurl
grpcurl -plaintext -unix ~/.cache/yottacast/core.sock \
  yottacast.LifecycleService/GetStatus

grpcurl -plaintext -unix ~/.cache/yottacast/core.sock \
  -d '{"query": "safari", "limit": 10}' \
  yottacast.SearchService/SearchInstant

# Tests existentes (no deben romperse)
cd Yottacast.Core.Tests && dotnet test
```

> **Verificar en:** `Yottacast.Ipc/Services/SearchGrpcService.cs` (registry + streaming), `Yottacast.Ipc/Services/LifecycleGrpcService.cs` (startup states), `Yottacast.Core/AppPaths.cs` (nuevas rutas IPC).