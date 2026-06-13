# Tests manuales

Casos de prueba que requieren arrancar la app y verificar comportamiento en runtime.
Este fichero se actualiza cada vez que se añade o modifica una feature que necesite verificación manual.

> **Convención**: cada sección corresponde a una feature o área. Dentro, los casos se listan como pasos verificables.
> Incluir el estado actual si un caso está pendiente de corregir (`⚠️ pendiente`) o confirmado funcional (`✅`).

---

## Plugins WebSearch

**Preparación**: El directorio de plugins es `~/Library/Application Support/Yottacast/plugins/` (macOS).
Accesible en este repo via `user-data/config/plugins/`.

### Carga al arrancar

1. Crea `user-data/config/plugins/websearch.test.json` con:
   ```json
   {
     "id": "hackernews",
     "name": "Hacker News",
     "queryUrl": "https://hn.algolia.com/?q={0}",
     "defaultPrefix": "hn"
   }
   ```
   > **Nota**: el tipo de plugin se decide por el nombre de fichero (`websearch.*.json`), no por un campo `type` dentro del JSON. `PluginService` solo deserializa `id`, `name`, `queryUrl`, `iconUrl`, `defaultPrefix` y `showAlwaysPattern`; cualquier otro campo (`type`, `defaultEnabled`, `defaultMode`) se ignora. El `Enabled`/`Mode`/`Prefix` por defecto los crea `UserSettings.EnsurePluginSettings` (Enabled=true, PrefixOnly, prefijo = `defaultPrefix`).
2. Arranca la app.
3. Abre Settings > Web Search.
4. **Verificar**: aparece sección "Plugins" con "Hacker News" en la tabla.
5. Escribe `hn rust` en el buscador.
6. **Verificar**: aparece "Hacker News: rust" como resultado, con score 3.5.
7. Pulsa Enter sobre él.
8. **Verificar**: se abre `https://hn.algolia.com/?q=rust` en el navegador configurado.

### Recarga en caliente (FileSystemWatcher)

1. Con la app en marcha, edita `user-data/config/plugins/websearch.test.json` y cambia `"name"` a `"HN"`.
2. Espera ~1 segundo.
3. Abre Settings > Web Search.
4. **Verificar**: la sección Plugins muestra "HN" sin reiniciar.

### Icono remoto

1. Añade `"iconUrl": "https://news.ycombinator.com/favicon.ico"` al plugin JSON.
2. Borra `user-data/cache/plugin-icons/hackernews.ico` si existe.
3. Reinicia la app (o guarda el fichero para triggear la recarga).
4. **Verificar**: `user-data/cache/plugin-icons/hackernews.ico` se crea.
5. Escribe `hn algo` en el buscador.
6. **Verificar**: el resultado muestra el icono de HN.

### Icono con URL inválida

1. Pon `"iconUrl": "https://dominio-inexistente-xyz.com/favicon.ico"`.
2. Borra el .ico de caché si existe.
3. Recarga el plugin.
4. **Verificar**: el resultado aparece sin icono, sin crash ni mensaje de error al usuario.
5. **Verificar**: en logs (`user-data/logs/`) aparece un `[DBG]` de descarga fallida.

### Estado vacío en Settings

1. Asegúrate de que no hay ficheros `.json` en `user-data/config/plugins/`.
2. Abre Settings > Web Search.
3. **Verificar**: debajo del separador "Plugins" aparece el texto descriptivo ("No plugins installed...").

### Plugin con ID duplicado a motor del sistema

1. Crea un plugin con `"id": "google"`.
2. **Verificar**: en Settings aparece en la sección Plugins, separado de Google del sistema.
3. Ambos funcionan de forma independiente en el buscador.

---

## Validación ShowAlwaysPattern

### Motor del sistema (sin patrón)

1. Asegúrate de que Google tiene `mode: ShowAlways`.
2. Escribe cualquier texto, p.ej. `hello`.
3. **Verificar**: aparece "Google: hello".

### Plugin con patrón (solo URLs)

1. Crea un plugin con `"showAlwaysPattern": "^https?://"` y, en Settings > Web Search, pon su modo en "Always" (el campo `defaultMode` del JSON se ignora; el modo se ajusta desde Settings o cambiando `Mode` en settings.json).
2. Escribe `hello` - **verificar**: el plugin NO aparece.
3. Escribe `https://example.com` - **verificar**: el plugin SÍ aparece.

### Plugin con patrón inválido (regex rota)

1. Crea un plugin con `"showAlwaysPattern": "["` (regex inválida) y modo "Always".
2. **Verificar**: el plugin aparece en ShowAlways (el sistema ignora el patrón inválido, no crashea).

---

## Recarga automática de settings.json

### Edición externa detectada

1. Arranca la app. Configura un prefijo para Google (p.ej. cambia de `g` a `gg`) desde Settings.
2. Abre `user-data/config/settings.json` en un editor externo.
3. Cambia el prefijo de `google` de `"gg"` a `"gx"` manualmente.
4. Guarda el fichero.
5. Espera ~1 segundo.
6. Escribe `gx search` en el buscador.
7. **Verificar**: aparece "Google: search" (la recarga surtió efecto sin reiniciar).

### Guard anti-circular (guardar desde la app no provoca reload)

1. Abre Settings y cambia cualquier valor (p.ej. activa/desactiva un motor).
2. **Verificar**: no hay ningún reinicio, parpadeo ni comportamiento extraño en la app.
3. **Verificar en logs**: no aparece ningún mensaje de "Settings reloaded" tras guardar desde la UI.

---

## Configuración de plugins en Settings

### Activar/desactivar plugin

1. Con un plugin cargado, abre Settings > Web Search > sección Plugins.
2. Desmarca el checkbox del plugin.
3. Escribe su prefijo en el buscador.
4. **Verificar**: el plugin no aparece en resultados.
5. Vuelve a activarlo - **verificar**: aparece de nuevo.

### Cambiar prefijo de plugin

1. En Settings, haz doble clic en el prefijo del plugin y cámbialo a `xx`.
2. Pulsa Enter o haz clic fuera.
3. Escribe `xx algo` en el buscador.
4. **Verificar**: aparece el resultado del plugin con la nueva query.

### Modo toggle (PrefixOnly ↔ ShowAlways)

1. Cambia el modo del plugin a "Always".
2. Escribe cualquier texto.
3. **Verificar**: el plugin aparece (si no hay prefijo de otro motor activo).
4. Cambia de vuelta a "Prefix".
5. **Verificar**: solo aparece al usar su prefijo.

### Persistencia entre reinicios

1. Configura un plugin con prefijo personalizado.
2. Cierra y vuelve a abrir la app.
3. **Verificar**: la configuración del plugin se mantiene.

---

## Suite automatizada (xUnit)

Dos proyectos de test, cada uno se ejecuta con `dotnet test` desde su carpeta:

```bash
cd Yottacast.Core.Tests && dotnet test   # lógica del Core (search sources, services, scoring, viewmodels)
cd Yottacast.Ipc.Tests   && dotnet test   # mappers IPC (ResultMapper, SettingsMapper)
```

`Yottacast.Core.Tests` agrupa sus tests por area en subcarpetas (`Search/`, `Services/`, `Platform/`, `ViewModels/`); cada `CLAUDE.md` de paquete lista los ficheros relevantes de su area. `Yottacast.Ipc.Tests` solo contiene `Mapping/ResultMapperTests.cs` y `Mapping/SettingsMapperTests.cs`.

### Riesgos y gaps conocidos de la suite

> **Bug conocido** - varios tests llaman `UserSettings.Load(platform)` SIN pasar `settingsPath`, por lo que cargan y reescriben el `settings.json` REAL del usuario (`AppPaths.SettingsFile`), no un fichero temporal. Esto puede mutar la configuracion local al ejecutar los tests. Los tests bien aislados pasan un path temporal (ver `UserSettingsTests`, `HistoryServiceTests`, `SettingsMapperTests`). Verificar en `Yottacast.Core.Tests/Search/*` (ej. `EmojiSearchTests`, `UrlSearchTests`, `SystemSettingsSearchTests`, `UserDocumentSearchTests`) frente a `Yottacast.Core/Services/UserSettings.cs` -> `Load` (default `settingsPath = AppPaths.SettingsFile`).

> **Estado: incompleto** - `SettingsMapperTests` no cubre lo que su nombre promete: no ejercita las listas (`WebSearchEngines`, `SearchFolders`, `AppDirectories`, `DictionaryLanguages`) ni los campos con bugs de mapeo conocidos (`clipboard_history_enabled` huerfano, perdida de `ModeOnly` en `FileSearchVisibility`). Ver `docs/ipc-daemon.md` (gaps de mapeo). Verificar en `Yottacast.Ipc.Tests/Mapping/SettingsMapperTests.cs`.

> **Bug conocido** - `Yottacast.Ipc.Tests.csproj` referencia el proyecto `Yottacast.Ipc` Y ademas vuelve a incluir los mismos `.proto` con `Grpc.Tools`, generando los tipos protobuf por duplicado. Compilar los tests produce warnings `CS0436` (tipo definido en dos ensamblados). No rompe la build pero ensucia la salida. Verificar en `Yottacast.Ipc.Tests/Yottacast.Ipc.Tests.csproj` (item `Protobuf` mas `ProjectReference` a `Yottacast.Ipc`).
