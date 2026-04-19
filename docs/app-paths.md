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

### Ficheros concretos

| Fichero      | Directorio base | Nombre             | Descripcion                                              |
|--------------|-----------------|--------------------|----------------------------------------------------------|
| Settings     | Configuracion   | `settings.json`    | Preferencias del usuario (JSON)                          |
| Emoji cache  | Configuracion   | `emoji-cache.json` | Cache compacta de datos de emojis                        |
| Log pattern  | Logs            | `yottacast-.log`   | Patron de Serilog para log diario                        |
| App icons    | Cache           | `app-icons/`       | Iconos de aplicaciones instaladas                        |
| File icons   | Cache           | `file-icons/`      | Iconos de tipo de fichero (por extension), cacheados     |
| Badge icons  | Cache           | `badge-icons/`     | Iconos de la app predeterminada por extension de fichero |

### Invariantes

- Ningun fichero `.cs` fuera de la clase centralizada usa `Environment.SpecialFolder` para construir rutas de datos de
  la aplicacion. (Los `PlatformProvider` usan `SpecialFolder` para descubrir aplicaciones del sistema, lo cual es
  correcto y no viola esta regla.)
- Los directorios se crean bajo demanda: cada consumidor llama a `Directory.CreateDirectory` antes de escribir, por lo
  que la app no falla si el directorio no existe aun.

> **Verificar en:** `Yottacast.Core/AppPaths.cs` (definiciones), consumidores: `App.axaml.cs`, `AppIconCache.cs`,
`FileIconCache.cs`, `UserDocumentSearch.cs`, `UserSettings.cs`, `EmojiDataLoader.cs`.

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
| Emojis            | Limite por defecto      | 20            | Emojis mostrados cuando el filtro esta vacio (solo `:`)      |
| Emojis            | Columnas del grid       | 8             | Columnas en la cuadricula del picker de emojis               |
| UI                | Delay de pegado         | 150 ms        | Espera antes de simular Cmd+V / Ctrl+V tras seleccionar      |
| Actualizaciones   | Timeout HTTP            | 10 s          | Timeout del request de comprobacion de version               |

### Invariantes

- Ningun componente usa numeros magicos para estos comportamientos; siempre referencia la clase central.
- Los valores son `const`, lo que permite usarlos en contextos que requieren constantes de compilacion (como parametros
  por defecto de metodos).

> **Verificar en:** `Yottacast.Core/AppDefaults.cs` (definiciones), consumidores: `MainWindowViewModel.cs`,
`MacAppHandler.cs`, `WindowsAppHandler.cs`, `UserDocumentSearch.cs`, `EmojiSearch.cs`, `EmojiGridResultViewModel.cs`,
`UpdateChecker.cs`.

## Convencion para nuevos elementos

- Nueva ruta de disco: definirla en `AppPaths`.
- Nueva constante o valor por defecto: definirla en `AppDefaults`.
- Los consumidores nunca deben hardcodear valores que pertenezcan a estas categorias.

## Acceso rapido a datos de runtime (desarrollo)

El directorio `user-data/` en la raiz del proyecto contiene symlinks a los directorios runtime de la maquina local. Esta
en `.gitignore` y no se sube al repositorio.

| Symlink  | Destino (macOS)                           | Contenido                           |
|----------|-------------------------------------------|-------------------------------------|
| `config` | `~/Library/Application Support/Yottacast` | `settings.json`, `emoji-cache.json` |
| `logs`   | `~/Library/Logs/Yottacast`                | Logs diarios (`yottacast-*.log`)    |
| `cache`  | `~/.cache/yottacast`                      | `app-icons/`, `file-icons/`, `badge-icons/` |

Si los symlinks se pierden, ejecutar:

```bash
./user-data/create-links.sh
```

> **Verificar en:** `user-data/create-links.sh` (creacion de links), `user-data/README.md` (documentacion local),
`.gitignore` (exclusion).