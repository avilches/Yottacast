# Sistema de plugins

Yottacast soporta plugins de usuario que extienden las fuentes de busqueda y los temas visuales. Los plugins son ficheros JSON que el usuario coloca en el directorio de plugins (`AppPaths.PluginsDir`).

---

## 1. Tipos de plugin

| Tipo | Patron de fichero | Descripcion |
|---|---|---|
| WebSearch | `websearch.*.json` | Motor de busqueda web adicional |
| Theme | `theme.*.json` | Tema visual personalizado |

Estos son los **unicos** tipos de plugin. No existen plugins de codigo ni un mecanismo de ejecucion de scripts: un plugin es siempre un JSON declarativo (un motor de busqueda o un tema). El tipo se determina por el patron del nombre de fichero, no por un campo `type` dentro del JSON.

Ambos tipos se descubren automaticamente al arrancar y se recargan en caliente cuando cambian.

---

## 2. PluginService

Servicio singleton que vigila el directorio de plugins y gestiona la recarga.

### Ciclo de vida

1. `StartAsync()`: crea el directorio si no existe, ejecuta la carga inicial (`ReloadAsync`), inicia el `FileSystemWatcher`.
2. El watcher monitoriza `*.json` en `AppPaths.PluginsDir` (Created, Changed, Deleted, Renamed).
3. Cada cambio dispara `ReloadAsync` con un debounce de 300 ms (los editores suelen escribir en multiples pasos).
4. `ReloadAsync` re-lee todos los ficheros `websearch.*.json`, descarga iconos faltantes y emite `PluginsChanged`.

> **Bug conocido** - el handler del watcher (`OnDirectoryChanged`) es `async void` sin `try/catch`. Cualquier excepcion no capturada durante `ReloadAsync` (o, en particular, lanzada por un suscriptor de `PluginsChanged`) escapa al ThreadPool en un contexto `async void` y puede tumbar el proceso. Verificar en `Yottacast.Core/Services/PluginService.cs` -> `OnDirectoryChanged`.

> **Bug conocido** - acceso no sincronizado al diccionario de iconos. `ReloadAsync` muta `_icons` bajo `lock (_icons)`, pero `GetIcon(id)` lee con `_icons.GetValueOrDefault(id)` sin tomar ese lock. Una lectura concurrente con la recarga puede lanzar o devolver un estado inconsistente. Verificar en `Yottacast.Core/Services/PluginService.cs` -> `GetIcon` frente a `ReloadAsync`.

### Evento PluginsChanged

Consumidores:
- `ThemeService`: refresca la lista de temas disponibles (plugins `theme.*.json`).
- `WebSearchSource`: recarga la lista de motores de busqueda desde plugins.
- `SettingsWindowViewModel`: reconstruye la UI de motores de busqueda.

---

## 3. Plugins WebSearch

### Formato JSON

```json
{
  "id": "hackernews",
  "name": "Hacker News",
  "queryUrl": "https://hn.algolia.com/?q={0}",
  "iconUrl": "https://news.ycombinator.com/favicon.ico",
  "defaultPrefix": "hn",
  "showAlwaysPattern": null
}
```

| Campo | Requerido | Descripcion |
|---|---|---|
| `id` | Si | Identificador unico del plugin. Duplicados se ignoran con warning |
| `name` | Si | Nombre visible en la UI y en Settings |
| `queryUrl` | Si | URL de busqueda con placeholder `{0}` para la query |
| `iconUrl` | No | URL remota del icono. Se descarga una vez y se cachea en `AppPaths.PluginIconCacheDir` como `{id}.ico` |
| `defaultPrefix` | No | Prefijo por defecto para modo PrefixOnly (ej. `"hn"` activa con `hn query`) |
| `showAlwaysPattern` | No | Regex opcional. En modo ShowAlways, la query debe coincidir con este patron para que el motor aparezca. Si es invalido, se ignora |

### Campos del record WebSearchPlugin

El plugin se carga como `WebSearchPlugin` (record en `Yottacast.Core/Search/WebSearch/WebSearchPlugin.cs`). Ademas de los campos JSON, incluye `SourceFilePath` (ruta absoluta al fichero JSON de origen).

### Gestion de iconos

1. Al cargar un plugin con `iconUrl`, se comprueba si existe `{PluginIconCacheDir}/{id}.ico`.
2. Si no existe, se descarga via HTTP (timeout 10s). Los fallos se logean sin bloquear.
3. Los bytes del icono se mantienen en memoria en un `Dictionary<string, byte[]?>` dentro de `PluginService`.
4. Los consumidores acceden al icono via `PluginService.GetIcon(id)`.

### Interaccion con UserSettings

Cuando se detecta un plugin nuevo, `UserSettings.EnsurePluginSettings()` crea automaticamente una entrada `WebSearchEngineSettings` con valores por defecto (`Enabled=true`, `Mode=PrefixOnly`, `Prefix=defaultPrefix`).

---

## 4. Plugins de tema

Los temas de usuario siguen el mismo formato JSON que los temas built-in (ver `docs/ui-themes.md`), con la convencion de nombre `theme.*.json`.

| Aspecto | Comportamiento |
|---|---|
| Prefijo de ID | Los temas de plugin se registran con prefijo `user:` en el ID (ej. `user:mi-tema`) |
| Descubrimiento | `ThemeService` escanea `AppPaths.PluginsDir` para ficheros `theme.*.json` |
| Hot-reload | Si el tema activo es un plugin y su fichero cambia, se reaplicael tema automaticamente via `FileSystemWatcher` con debounce de 300 ms |
| Deduplicacion | Si existen multiples ficheros con el mismo `id` (comun con copias de iCloud), se toma el primero |

---

## 5. Invariantes

- Los plugins nunca bloquean el arranque: si un fichero es invalido o un icono no se puede descargar, se omite con un log de warning. (Excepcion: ver los bugs conocidos de `OnDirectoryChanged` y `GetIcon` mas arriba; una excepcion no capturada en la recarga puede afectar a la estabilidad del proceso.)
- El debounce de 300 ms en el watcher protege contra escrituras parciales de editores de texto.
- Los IDs duplicados se rechazan: solo el primero encontrado se carga.
- Los campos requeridos (`id`, `name`, `queryUrl`) se validan; si falta alguno, el plugin se omite.

> **Verificar en:** `Yottacast.Core/Services/PluginService.cs` (carga, watcher, iconos), `Yottacast.Core/Search/WebSearch/WebSearchPlugin.cs` (record), `Yottacast/Services/ThemeService.cs` (temas de plugin), `Yottacast.Core/Services/UserSettings.cs` (`EnsurePluginSettings`).
