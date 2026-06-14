# Motores de busqueda web

Este documento describe los motores de busqueda web que el usuario puede usar y personalizar en Yottacast. Queda paralelo a `docs/user-settings-browser.md` (que navegador abre las URLs) y `docs/user-settings-terminal.md`. La persistencia general de preferencias, el modelo y la serializacion viven en `docs/user-settings.md`.

---

## 1. Proposito

Un motor de busqueda web genera una URL de busqueda a partir de la query del usuario (p.ej. Google, DuckDuckGo, GitHub). Al activar un resultado Web Search, esa URL se abre en el navegador configurado (ver `docs/user-settings-browser.md`). Cada motor se puede personalizar de forma independiente.

---

## 2. Configuracion por motor

Cada motor de busqueda web tiene su propia configuracion que el usuario puede personalizar:

| Campo | Descripcion |
|---|---|
| Id | Identificador unico del motor (p.ej. `"google"`) |
| Enabled | Si el motor aparece en resultados |
| Mode | `PrefixOnly` (solo se activa con el alias) o `ShowAlways` (aparece siempre) |
| Prefix | Alias de teclado que activa el motor (p.ej. `"g"` para Google) |
| QueryUrl | URL personalizada con placeholder `{0}`. `null` significa usar la URL por defecto del motor |

La aplicacion incluye un catalogo de motores preconfigurados, organizados en grupos (General, Shopping, Video, Social, Knowledge, Dev, Entertainment, Maps). Por defecto, solo Google usa el modo `ShowAlways`; el resto usa `PrefixOnly`. La lista de motores y sus defaults por motor (id, grupo, prefijo, mode, enabled) viven en `WebSearchDefaults.Engines` y `WebSearchDefaults.DefaultSettingsFor`; los docs no la duplican.

**Invariante:** si el campo `Mode` del JSON contiene un valor no reconocido, se interpreta como `PrefixOnly`. El `QueryUrl` solo se escribe en el JSON si tiene un valor no vacio (se omite cuando es `null`, usando la URL por defecto del motor).

> **Verificar en:** `WebSearchEngine`, `WebSearchEngineSettings`, `WebSearchMode`, `WebSearchDefaults.Engines` y `WebSearchDefaults.DefaultSettingsFor` en `Yottacast.Core/Search/WebSearch/WebSearchEngine.cs`. Record DTO `WebSearchEngineSettingsData` en `Yottacast.Core/Services/UserSettings.cs`.

---

## 3. Merge de motores guardados con los predeterminados

Al cargar las preferencias, se fusionan los motores guardados con los predeterminados: las personalizaciones del usuario se conservan y los motores nuevos (anadidos en actualizaciones de la app) aparecen automaticamente con sus defaults. Asi, actualizar la app nunca borra la configuracion del usuario ni oculta motores recien incorporados.

> **Verificar en:** `UserSettings.MergeWebSearchEngines()` en `Yottacast.Core/Services/UserSettings.cs`.

---

## 4. UI de edicion en Settings (seccion "Web Search")

La seccion "Web Search" de Settings agrupa los motores por grupo, cada grupo en su propia tabla estilo lista. Cada fila muestra icono, nombre, prefijo, checkbox Enabled y un boton de settings (icono engranaje) que abre un flyout con toggle Mode, editar Prefix y editar QueryUrl.

Un checkbox "Show disabled engines" en la parte superior controla si se muestran los motores deshabilitados (preferencia `ShowDisabledWebSearchEngines`); si un grupo no tiene ningun motor visible, el grupo entero se oculta. Los motores mantienen siempre su posicion original (no se reordenan al deshabilitar). Los cambios se guardan automaticamente y disparan refresco de la busqueda activa (ver seccion 12 de `docs/user-settings.md`).

> **Verificar en:** `BuildWebSearchGroups()`, `WebSearchGroupViewModel` y `WebSearchEngineRowViewModel` en `Yottacast/ViewModels/SettingsWindowViewModel.cs`. UI inline en `Yottacast/Views/SettingsWindow.axaml` y `SettingsWindow.axaml.cs`. Refresco via `NotifySearchSettingsChanged()`.

---

## 5. Motores de plugin

Ademas de los motores preconfigurados, el usuario puede instalar motores adicionales como plugins. Un plugin WebSearch es un fichero JSON en `AppPaths.PluginsDir` (`~/Library/Application Support/Yottacast/plugins/` en macOS) cuyo nombre sigue el patron `websearch.*.json`. `PluginService` los carga al arranque y vigila la carpeta para hot-reload. El formato del JSON y el sistema de plugins se documentan en `docs/plugin-system.md`.

Los plugins aparecen en la UI de Settings igual que los motores built-in, con un icono de plugin (puzzle piece) junto al nombre. Un plugin sin grupo cae en el grupo "general". El flyout de cada plugin incluye dos botones adicionales: "Show plugin folder" (abre la carpeta de plugins en el gestor de archivos) y "Edit plugin source" (abre el JSON del plugin con la app por defecto).

Al cargar un plugin nuevo, `UserSettings.EnsurePluginSettings()` crea su entrada en `WebSearchEngines` con defaults (`Enabled=true`, `Mode=PrefixOnly`, `Prefix` = `defaultPrefix` del plugin). A partir de ahi, la configuracion del plugin se persiste y personaliza igual que cualquier motor built-in.

Ver `docs/examples/hackernews.json` para un ejemplo de plugin.

> **Verificar en:** `UserSettings.EnsurePluginSettings()` en `Yottacast.Core/Services/UserSettings.cs`. `PluginService`, `WebSearchPlugin` en `Yottacast.Core/`. Agrupacion de plugins (`Group` vacio -> "general") en `BuildWebSearchGroups()` de `SettingsWindowViewModel`. Detalle del formato de plugin en `docs/plugin-system.md`.
