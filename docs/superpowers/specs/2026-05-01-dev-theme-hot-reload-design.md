# Dev Theme Hot-Reload

## Objetivo

Cuando la app arranca desde `dotnet run` (modo desarrollo), los temas built-in se recargan automáticamente al guardar el fichero JSON fuente, sin necesidad de copiarlos como plugins ni reiniciar la app.

---

## Detección de dev mode

`ThemeService.ThemesFolder` detecta si está en modo desarrollo comprobando si `AppContext.BaseDirectory` contiene el segmento `bin/` en su ruta. Si es así, sube 3 niveles (de `net9.0/Debug/bin/` al directorio del proyecto) y busca `Themes/dark-default.json` como sanity check.

| Condición | `ThemesFolder` apunta a |
|---|---|
| `BaseDirectory` contiene `/bin/` y existe source `Themes/` | Source tree: `<proyecto>/Themes/` |
| Cualquier otro caso (producción, CI) | Output: `AppContext.BaseDirectory + "/Themes"` |

Se registra un `LogInformation` al arrancar en dev mode: `"Dev mode: watching themes from source tree at {Path}"`.

---

## Hot-reload para temas built-in

`WatchActiveTheme` actualmente solo vigila temas de usuario (`if (!IsUserTheme(themeId)) return`). Se eliminan esas dos líneas para que todos los temas activos se vigilen, independientemente de su origen.

El directorio del watcher pasa de `AppPaths.PluginsDir` (hardcoded) a `Path.GetDirectoryName(filePath)!`, que en dev mode apuntará al source tree y en producción al directorio de output.

El resto del mecanismo (debounce 300ms, `Interlocked.Exchange`, `Dispatcher.UIThread.Post`) no cambia.

---

## Invariantes

- En producción, `ThemesFolder` siempre devuelve el path de output. El comportamiento es idéntico al actual.
- Si la heurística de detección falla (no encuentra `dark-default.json` en el source tree), cae silenciosamente al path de producción. No hay error.
- El log de "dev mode activo" solo aparece si la detección tiene éxito.

---

## Ficheros afectados

- `Yottacast/Services/ThemeService.cs` — únicos cambios: propiedad `ThemesFolder` y método `WatchActiveTheme`.

No se tocan: settings, UI, otros servicios, `.csproj`, ni ningún otro fichero.

---

## Verificar en

- `ThemeService.ThemesFolder` — lógica de detección
- `ThemeService.WatchActiveTheme` — guard eliminado, directorio del watcher dinámico
- Logs al arrancar en dev mode
