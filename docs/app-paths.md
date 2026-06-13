# Rutas y constantes de la aplicacion

## Todas las rutas de disco estan centralizadas

La aplicacion nunca construye rutas de fichero de forma local. Cualquier componente que necesite leer o escribir en
disco obtiene la ruta de una unica clase centralizada. Esto garantiza que:

- No existen rutas duplicadas ni inconsistentes entre componentes.
- Cambiar la ubicacion de un fichero requiere modificar un solo lugar.
- Es posible auditar todas las rutas de I/O del sistema inspeccionando una sola clase.

### Directorios base

| Directorio    | Proposito                           | macOS                                     | Windows                         |
|---------------|-------------------------------------|-------------------------------------------|---------------------------------|
| Configuracion | Ajustes de usuario y caches ligeras | `~/Library/Application Support/Yottacast` | `%APPDATA%/Yottacast`           |
| Logs          | Ficheros de log rotados diariamente | `~/Library/Logs/Yottacast`                | `%LOCALAPPDATA%/Yottacast/Logs` |
| Cache         | Datos regenerables (iconos de apps) | `~/.cache/yottacast`                      | `~/.cache/yottacast`            |
| Plugins       | Plugins del usuario (WebSearch, temas) | `{Configuracion}/plugins`              | `{Configuracion}/plugins`       |

### Ficheros concretos

| Fichero          | Directorio base | Nombre                        | Descripcion                                                          |
|------------------|-----------------|-------------------------------|----------------------------------------------------------------------|
| Settings         | Configuracion   | `settings.json`               | Preferencias del usuario (JSON)                                      |
| Emoji cache      | Configuracion   | `emoji-cache.json`            | Cache compacta de datos de emojis                                    |
| Emoji usage      | Configuracion   | `emoji-usage.json`            | Favoritos y contadores de uso de emojis (JSON)                       |
| History          | Configuracion   | `history.json`                | Historial de busquedas del usuario (JSON)                            |
| Launch history   | Configuracion   | `launch-history.json`         | Contador y ultimo uso por item lanzado (para scoring por uso, JSON)  |
| Clipboard history| Configuracion   | `clipboard-history.json`      | Historial de portapapeles (JSON)                                    |
| Log pattern      | Logs            | `yottacast-.log`              | Patron de Serilog para log diario                                    |
| App icons        | Cache           | `app-icons/`                  | Iconos de aplicaciones instaladas                                    |
| File icons       | Cache           | `file-icons/`                 | Iconos de tipo de fichero (por extension), cacheados                 |
| Badge icons      | Cache           | `badge-icons/`                | Iconos de la app predeterminada por extension de fichero             |
| Plugin icons     | Cache           | `plugin-icons/`               | Iconos descargados de plugins WebSearch                              |
| Favicons         | Cache           | `favicons/`                   | Favicons descargados para resultados de URL/web                      |
| Exchange rates   | Cache           | `exchange-rates.json`         | Cache de tasas de cambio descargadas (JSON)                          |
| Terminal scripts | Cache           | `terminal-scripts/`           | Scripts `.command` temporales para lanzar comandos en terminales sin API (macOS); barridos en cada ejecucion |
| Dict JSONL       | Cache           | `dictionary/{lang}.jsonl`     | Diccionario basico descargable (kaikki, 1 linea por entrada)         |
| Dict SQLite      | Cache           | `dictionary/{lang}.db`        | Diccionario local compilado; la app lo genera del JSONL si no existe |
| IPC socket       | Cache           | `core.sock`                   | Unix domain socket del daemon gRPC (creado al arrancar, borrado al salir) |
| IPC PID file     | Cache           | `core.pid`                    | PID del daemon en ejecucion; evita instancias duplicadas |

Ademas, `AppPaths` define rutas de solo lectura del sistema en macOS para la fuente System Settings: `SystemSettingsAppPath` (`/System/Applications/System Settings.app`), `SystemPreferencePanesDir` (`/Library/PreferencePanes`) y `UserPreferencePanesDir` (`~/Library/PreferencePanes`). No son ficheros que la app escriba, sino ubicaciones del SO que consulta.

### Invariantes

- Ningun fichero `.cs` fuera de la clase centralizada usa `Environment.SpecialFolder` para construir rutas de datos de
  la aplicacion. (Los `PlatformProvider` usan `SpecialFolder` para descubrir aplicaciones del sistema, lo cual es
  correcto y no viola esta regla.)
- Los directorios se crean bajo demanda: cada consumidor llama a `Directory.CreateDirectory` antes de escribir, por lo
  que la app no falla si el directorio no existe aun.
- `LogDir` es la unica propiedad de `AppPaths` que usa una comprobacion de SO (`OperatingSystem.IsMacOS()`) para
  decidir entre `~/Library/Logs/Yottacast` (macOS) y `%LOCALAPPDATA%/Yottacast/Logs` (resto). Es una excepcion
  consciente a la regla de no consultar el SO fuera de `PlatformProvider`: se acepta aqui porque `AppPaths` es estatica
  y se inicializa antes de que exista el contenedor DI, de modo que no puede delegar en `PlatformProvider`.

> **Verificar en:** `Yottacast.Core/AppPaths.cs` (definiciones), consumidores: `App.axaml.cs`, `AppIconCache.cs`,
`FileIconCache.cs`, `UserDocumentSearch.cs`, `UserSettings.cs`, `EmojiDataLoader.cs`, `ExchangeRateService.cs`,
`PluginService.cs`, `EmojiUsageStore.cs`, `HistoryService.cs`, `SystemSettingsSearch.cs`, `Yottacast.Ipc/Program.cs`.

## Todos los valores por defecto estan centralizados

Los parametros numericos y temporales que controlan el comportamiento de la aplicacion (timeouts, limites, delays) se
definen en una unica clase de constantes. Esto permite:

- Ajustar el comportamiento sin buscar valores dispersos por el codigo.
- Promover cualquier constante a configuracion de usuario sin cambiar los puntos de consumo.

### Parametros actuales

| Categoria         | Parametro               | Valor         | Efecto                                                       |
|-------------------|-------------------------|---------------|--------------------------------------------------------------|
| Busqueda global   | Debounce                | 250 ms        | Espera antes de buscar tras dejar de teclear                 |
| Busqueda global   | Query minimo (ficheros) | 2 caracteres  | No busca ficheros con menos caracteres                       |
| Busqueda global   | Limite por fuente       | 10 resultados | Maximo de resultados que cada fuente devuelve                |
| Busqueda ficheros | Timeout                 | 20 s          | Tiempo maximo para una consulta a Spotlight / Windows Search |
| Busqueda ficheros | Intervalo de snapshot   | 200 ms        | Frecuencia minima de actualizacion progresiva de resultados  |
| Emojis            | Columnas del grid       | 10            | Columnas en la cuadricula del picker de emojis               |
| Emojis            | Filas del viewport      | 8             | Filas visibles simultaneamente en el picker de emojis        |
| Emojis            | Max favoritos           | 4             | Maximo de emojis marcados como favorito en la seccion pinned |
| Emojis            | Max pinned total        | 10            | Maximo total de emojis en la seccion pinned (fav + most-used)|
| Emojis            | Half-life (dias)        | 30            | Vida media del decay score de uso de emojis                  |
| UI                | Delay de pegado         | 150 ms        | Espera antes de simular Cmd+V / Ctrl+V tras seleccionar      |
| Actualizaciones   | Timeout HTTP            | 10 s          | Timeout del request de comprobacion de version               |
| Diccionario       | Timeout HTTP            | 5 s           | Timeout de peticion a la API de Wiktionary                   |
| Diccionario       | Max definiciones        | 5             | Definiciones mostradas por entrada (parte del discurso)      |
| Diccionario       | Prefijo por defecto     | `"define"`    | Prefijo en modo PrefixOnly                                   |
| Diccionario       | Idiomas kaikki          | 16 codigos    | Idiomas con soporte de DB local (ver `KaikkiLanguages`)      |
| Exchange rates    | Intervalo por defecto   | 4 h           | Frecuencia de refresco de tasas de cambio (configurable)     |
| Exchange rates    | Timeout HTTP            | 10 s          | Timeout de cada llamada a la API de tasas                    |
| Historial         | Max entradas            | 100           | Numero maximo de entradas en el historial de busqueda        |
| Clipboard         | Max entradas            | 200           | Numero maximo de entradas en el historial de portapapeles    |
| Clipboard         | Max dias                | 30            | Antiguedad maxima de una entrada de portapapeles             |
| Clipboard         | Half-life (dias)        | 30            | Vida media del decay score de uso de portapapeles            |
| Clipboard         | Debounce de guardado    | 1000 ms       | Espera antes de persistir el historial a disco               |
| Clipboard         | Intervalo de polling    | 500 ms        | Frecuencia de muestreo del monitor de portapapeles           |
| Ventana           | Decay timer             | 60 s          | Duracion por defecto antes de limpiar el texto al ocultar    |

### Invariantes

- Ningun componente usa numeros magicos para estos comportamientos; siempre referencia la clase central.
- Los valores son `const`, lo que permite usarlos en contextos que requieren constantes de compilacion (como parametros
  por defecto de metodos).

> **Verificar en:** `Yottacast.Core/AppDefaults.cs` (definiciones), consumidores: `MainWindowViewModel.cs`,
`MacAppHandler.cs`, `WindowsAppHandler.cs`, `UserDocumentSearch.cs`, `EmojiSearch.cs`, `EmojiGridResultViewModel.cs`,
`UpdateChecker.cs`, `DictionarySource.cs`, `LocalDictionaryConverter.cs`, `ExchangeRateService.cs`, `UserSettings.cs`.

## Convencion para nuevos elementos

- Nueva ruta de disco: definirla en `AppPaths`.
- Nueva constante o valor por defecto: definirla en `AppDefaults`.
- Los consumidores nunca deben hardcodear valores que pertenezcan a estas categorias.

## Acceso rapido a datos de runtime (desarrollo)

El directorio `user-data/` en la raiz del proyecto contiene symlinks a los directorios runtime de la maquina local. Esta
en `.gitignore` y no se sube al repositorio.

| Symlink  | Destino (macOS)                           | Contenido                           |
|----------|-------------------------------------------|-------------------------------------|
| `config` | `~/Library/Application Support/Yottacast` | `settings.json`, `emoji-cache.json`, `emoji-usage.json`, `history.json`, `plugins/` |
| `logs`   | `~/Library/Logs/Yottacast`                | Logs diarios (`yottacast-*.log`)    |
| `cache`  | `~/.cache/yottacast`                      | `app-icons/`, `file-icons/`, `badge-icons/`, `plugin-icons/`, `exchange-rates.json`, `dictionary/` |

Si los symlinks se pierden, ejecutar:

```bash
./user-data/create-links.sh
```

> **Verificar en:** `user-data/create-links.sh` (creacion de links), `user-data/README.md` (documentacion local),
`.gitignore` (exclusion).