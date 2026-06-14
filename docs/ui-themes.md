# UI: Sistema de temas

Para la referencia completa de tokens (cada propiedad del JSON y el recurso Avalonia al que mapea) ver `docs/ui-themes-tokens.md`.

## Objetivo

Yottacast permite personalizar la apariencia visual de la ventana principal mediante temas definidos en archivos JSON. El sistema garantiza que la aplicacion siempre arranque con un aspecto coherente, incluso si el fichero de tema esta corrupto, ausente o mal configurado.

---

## Comportamiento en el arranque

El tema se aplica de forma sincrona **antes** de crear la ventana principal. El usuario nunca ve la interfaz con un tema incorrecto o parcialmente aplicado.

El tema inicial se determina segun esta logica:

| Situacion | Tema seleccionado |
|---|---|
| Primera ejecucion, sistema en modo oscuro o indeterminado | `dark-default` |
| Primera ejecucion, sistema en modo claro | `light-gray` |
| Settings existente con campo `theme` valido | El tema guardado |
| Settings existente con campo `theme` vacio | Deteccion automatica del sistema (misma logica que primera ejecucion) |

> **Verificar en:**
> - `App.OnFrameworkInitializationCompleted()` -- llama `themeService.Apply(userSettings.Theme)` antes de instanciar `MainWindow`.
> - `PlatformProvider.DefaultTheme()` -- logica de deteccion oscuro/claro.
> - `UserSettings.Load()` -- fallback cuando `theme` esta vacio o el fichero no existe.

---

## Alcance de los temas

Los temas solo afectan a la **ventana principal de busqueda** (MainWindow). La ventana de Settings usa colores nativos de macOS independientemente del tema seleccionado.

**Invariante:** la ventana de Settings sigue siempre el modo claro/oscuro del sistema operativo, no el tema del buscador. Esto se logra mediante dos mecanismos:

1. `SettingsWindow.RequestedThemeVariant` se fija en el constructor según `PlatformSettings.GetColorValues().ThemeVariant`, desacoplándose del `Application.RequestedThemeVariant` que cambia ThemeService.
2. `ApplySettingsTheme()` (en el `AppHandler` de cada plataforma) inyecta en runtime, via C#, los `ThemeDictionaries` (Light/Dark) con colores nativos del OS para los tokens `Theme.*` que usa Settings; tienen prioridad sobre los de `Application.Resources`. Estos colores no se definen en el AXAML.

La ventana de Settings se compone de UserControls por sección bajo `Yottacast/Views/Settings/` (uno por sección: General, AppSearch, FileSearch, FileEditor, Clipboard, Emoji, Dictionary, DateSearch, History, Permissions), más los recursos compartidos `SettingsResources.axaml` (iconos) y `SettingsStyles.axaml` (estilos de campos). Las secciones WebSearch y Calculator permanecen inline en `SettingsWindow.axaml`, que además conserva el chrome (sidebar, divider) y sus estilos.

Algunos colores puntuales de Settings están hardcodeados directamente en el AXAML, no provienen de los temas. Por ejemplo, el rojo de captura de hotkey (`#FF3B30`) aparece literal en el estilo compartido `hotkey-field.capturing` de `Yottacast/Views/Settings/SettingsStyles.axaml` y en los campos de captura de `SettingsGeneralView.axaml` y `SettingsClipboardView.axaml` (y en `SettingsWindow.axaml` para las secciones inline). Cambiar ese color exige editar esos AXAML, no un tema.

> **Verificar en:**
> - `SettingsWindow.axaml.cs` - constructor, detección OS y asignación de `RequestedThemeVariant`.
> - `MacAppHandler.cs`, `LinuxAppHandler.cs`, `WindowsAppHandler.cs` - `ApplySettingsTheme()` inyecta `ThemeDictionaries` via C# (no hay bloque en AXAML).

---

## Cambio de tema en caliente

El usuario puede cambiar el tema desde la ventana de Settings sin reiniciar la aplicacion. Al seleccionar un tema en el picker:

1. Se persiste la seleccion en el fichero de settings.
2. Se aplica inmediatamente sobre los recursos de la aplicacion (MainWindow).
3. La ventana de búsqueda refleja el nuevo tema al instante; la ventana de Settings no cambia.

**Invariante:** el picker de temas nunca queda sin seleccion. Si el tema guardado no existe en la lista, se selecciona el primero disponible.

**Invariante:** la seleccion inicial en el constructor del ViewModel se asigna al campo interno (no a la propiedad), evitando que se dispare el callback de cambio y se sobreescriban los settings durante la inicializacion.

> **Verificar en:**
> - `SettingsWindowViewModel.OnSelectedThemeChanged()` -- persiste y aplica.
> - Constructor de `SettingsWindowViewModel` -- asignacion al campo `_selectedTheme`.

---

## Formato de un fichero de tema

Cada tema es un fichero `.json` en la carpeta `Themes/` del directorio de la aplicacion. La estructura agrupa propiedades por componente de UI: cada seccion contiene todos los atributos visuales de sus elementos. La referencia completa de cada seccion y cada token (propiedad JSON a recurso Avalonia) vive en `docs/ui-themes-tokens.md`.

**Secciones heterogeneas entre temas:** no todos los ficheros contienen todas las secciones. `editor` solo existe en `dark-default.json`; el resto de temas no lo definen y `ThemeService` cae a los valores hardcodeados de `ApplyBuiltinDefault()` para los tokens `Theme.Editor.*`. Cualquier seccion ausente se omite sin error y conserva el valor previo (ver "Comportamiento silencioso ante valores invalidos").

> **Verificar en:**
> - `ThemeService.Apply()` -- lectura del JSON y asignacion de tokens.
> - `docs/ui-themes-tokens.md` -- referencia de todas las secciones y tokens.

---

## Fallback incorporado

Existe un fallback hardcodeado (`ApplyBuiltinDefault()`) que produce un tema oscuro funcional sin leer ningun fichero. No es una copia exacta de `dark-default.json`: ambos comparten origen pero han divergido en algunos valores (por ejemplo el estilo, color y opacidad de `matchHighlight`, o el ancho de ventana). El fallback garantiza un arranque coherente; el tema JSON puede diferir en detalles visuales. Este fallback se activa en cualquiera de estos casos:

- El fichero de tema no existe en disco.
- El JSON no se puede parsear.
- Se produce cualquier excepcion durante la aplicacion del tema.
- `Application.Current` es null (por ejemplo, en tests unitarios o durante un arranque muy temprano).

**Invariante:** la aplicacion siempre arranca con un tema funcional. Nunca se muestra una UI sin estilos.

**Invariante:** cuando se activa el fallback por fichero no encontrado o error de parsing, se registra un warning en el log.

> **Verificar en:**
> - `ThemeService.Apply()` -- bloques catch y comprobaciones de null.
> - `ThemeService.ApplyBuiltinDefault()` -- valores hardcodeados.

---

## Descubrimiento de temas

La lista de temas disponibles se construye escaneando dos fuentes en orden:

1. Ficheros `*.json` en `Themes/` (built-in), ordenados alfabeticamente.
2. Ficheros `theme.*.json` en `AppPaths.PluginsDir` (user themes), ordenados alfabeticamente.

Reglas de deduplicacion y resolucion:

- El `id` de cada tema se lee del campo `"id"` del JSON. Si no existe, se usa el nombre del fichero sin extension como fallback (solo built-ins).
- Si dos ficheros tienen el mismo `id`, solo se carga el primero. Esto filtra automaticamente las copias de conflicto de iCloud (`dark-default 2.json`) y los duplicados de user themes.
- El nombre para mostrar se extrae del campo `"name"` del JSON. Si falla el parsing, se usa el `id` como nombre.
- Si no se encuentra ningun tema, se anade un fallback `"dark-default"` / `"Dark Default"`.

Al escanear, `ThemeService` construye un diccionario `id → path` que cubre todos los temas. Todas las operaciones posteriores (aplicar, vigilar cambios) resuelven la ruta del fichero a traves de este diccionario - el nombre del fichero no interviene en ninguna operacion posterior al escaneo.

La carpeta de temas se resuelve relativa al directorio del ejecutable (`AppContext.BaseDirectory`), no al directorio de trabajo actual. En ejecucion desde un build de desarrollo (cuando `BaseDirectory` esta dentro de `bin/`), `ThemesFolder` prefiere la carpeta `Themes/` del source tree (`../../../Themes` respecto al ejecutable) si existe y contiene `dark-default.json`, de modo que editar un tema en el repo recargue en caliente sin recompilar. En el resto de casos usa `Themes/` bajo `BaseDirectory`.

Los ficheros JSON se copian al directorio de salida del build con `CopyToOutputDirectory=PreserveNewest`.

> **Verificar en:**
> - `ThemeService.AvailableThemes()` -- enumeracion, deduplicacion por `id` y construccion de `_themePaths`.
> - `ThemeService.ThemesFolder` -- resolucion de la ruta.
> - `Yottacast.csproj` -- regla de copia de `Themes/**`.

---

## Comportamiento silencioso ante valores invalidos

Los tokens que no se pueden interpretar se omiten silenciosamente, conservando el valor anterior del recurso:

| Tipo de token | Metodo | Que ocurre si el valor es invalido |
|---|---|---|
| Color | `SetBrush` | `Color.TryParse` falla: el brush no se asigna, sin warning |
| Numero (fuente/ancho) | `SetDouble` | Nodo JSON null: se omite sin aviso |
| Corner radius | `SetCornerRadius` | Nodo JSON null: se omite sin aviso |
| Opacidad | `SetOpacity` | Nodo JSON null: se omite sin aviso |
| Font family | `SetFontFamily` | Nodo JSON null: se omite; cadena vacia: usa `FontFamily.Default` |

**Invariante:** un token ausente o invalido en el JSON nunca provoca una excepcion. El peor caso es que ese elemento de la UI conserve el valor del tema anterior o del fallback.

> **Verificar en:**
> - `ThemeService.SetBrush()`, `ThemeService.SetDouble()`, `ThemeService.SetCornerRadius()`, `ThemeService.SetOpacity()`, `ThemeService.SetFontFamily()` -- comprobaciones de null y TryParse.

---

## Temas de usuario

Los usuarios pueden instalar temas personalizados colocando ficheros JSON en `AppPaths.PluginsDir` (la carpeta `plugins/` dentro del directorio de configuracion de la app). Los temas se detectan por convencion de nombre de archivo: `theme.*.json` (ej. `theme.my-custom.json`).

### Requisitos del JSON

El fichero debe incluir un campo `"id"` obligatorio en el nivel raiz. Sin este campo, el tema se ignora con un warning en el log. El `id` se usa como identificador interno del tema (prefijado con `user:`).

### Identificacion

Los temas de usuario se identifican con el prefijo `user:` en su ID. Un fichero `theme.my-theme.json` con `"id": "my-theme"` produce el ID `"user:my-theme"`. Esto evita colisiones con temas built-in. El nombre del fichero y el valor de `id` son independientes - la ruta se resuelve siempre a traves del diccionario `_themePaths` construido al escanear.

### Formato

El formato es identico al de los temas built-in (mismas secciones y tokens). No se requiere campo `"type"` - la deteccion es por nombre de archivo.

### Recarga automatica del tema activo

Si el tema seleccionado es de usuario, ThemeService vigila su fichero con un `FileSystemWatcher`. Al detectar un cambio (con debounce de 300ms), el tema se re-aplica automaticamente en el UI thread sin necesidad de reseleccionarlo.

### Actualizacion del picker en Settings

ThemeService se suscribe al evento `PluginsChanged` del `PluginService` central (que vigila `*.json` en la carpeta de plugins). Cuando cambia la lista de ficheros, se dispara el evento `ThemesChanged` y el picker de temas en Settings se actualiza automaticamente, preservando la seleccion actual si sigue disponible.

> **Verificar en:**
> - `PluginService.SetupWatcher()` -- watcher central del directorio de plugins.
> - `ThemeService.StartWatching()` -- suscripcion a `PluginService.PluginsChanged` y watcher del tema activo.
> - `ThemeService.WatchActiveTheme()` -- watcher del fichero activo para hot-reload.
> - `ThemeService.AvailableThemes()` -- escaneo de plugins con filtro `theme.*.json` y validacion de `id`.
> - `SettingsWindowViewModel.OnThemesChanged()` -- actualizacion del picker.

La lista de temas incluidos (built-in) y su variante esta en `docs/ui-themes-tokens.md`.
