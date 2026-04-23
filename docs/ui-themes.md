# UI: Sistema de temas

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
2. `Window.Resources` de SettingsWindow define sus propios ThemeDictionaries (Light/Dark) con colores nativos macOS para todos los tokens `Theme.*` que usa, que tienen prioridad sobre los de `Application.Resources`.

> **Verificar en:**
> - `SettingsWindow.axaml.cs` — constructor, detección OS y asignación de `RequestedThemeVariant`.
> - `SettingsWindow.axaml` — sección `Window.Resources` / `ResourceDictionary.ThemeDictionaries`.

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

Cada tema es un fichero `.json` en la carpeta `Themes/` del directorio de la aplicacion. La estructura agrupa propiedades por componente de UI: cada seccion contiene todos los atributos visuales (color, size, fontFamily, cornerRadius, opacity) de sus elementos.

| Seccion | Contenido |
|---|---|
| `name` | Nombre para mostrar en el picker |
| `variant` | `"light"` o `"dark"` (controla el `ThemeVariant` de Avalonia) |
| `window` | Fondo, ancho, cornerRadius y fontFamily de la ventana |
| `search` | Texto, placeholder, caret, seleccion, hint de error |
| `divider` / `spinner` | Color del separador y del spinner de carga |
| `results` | Titulo, subtitulo, categoria, icono, shortcut, seleccion, hover |
| `calculator` | Expresion, resultado, subtitulo, separador, celda |
| `converter` | Valor, subtitulo, flecha, hint, celda |
| `emoji` | Columnas, filas visibles, celda, caracter, nombre, keywords |
| `noResults` | Titulo y subtitulo cuando no hay resultados |
| `footer` | Borde y texto del pie |
| `escBadge` | Fondo, cornerRadius y texto del badge ESC |
| `updateBanner` | Fondo y texto del banner de actualizacion |

Si el valor de `variant` no es `"light"`, se asume `"dark"`.

> **Verificar en:**
> - `ThemeService.Apply()` -- lectura del JSON y asignacion de tokens.
> - Cualquier fichero en `Yottacast/Themes/*.json` como referencia de estructura.

---

## Tokens de tema disponibles

### Window

| JSON path | Recurso Avalonia |
|---|---|
| `window.background` | `Theme.Window.Background` |
| `window.width` | `Theme.Window.Width` |
| `window.cornerRadius` | `Theme.Window.CornerRadius` |
| `window.fontFamily` | `Theme.Window.FontFamily` |

### Search

| JSON path | Recurso Avalonia |
|---|---|
| `search.text.color` | `Theme.Search.Color` |
| `search.text.size` | `Theme.Search.Size` |
| `search.text.fontFamily` | `Theme.Search.FontFamily` |
| `search.placeholder.color` | `Theme.Search.Placeholder` |
| `search.caret.color` | `Theme.Search.Caret` |
| `search.selection.color` | `Theme.Search.Selection` |
| `search.hint.color` | `Theme.Search.Hint` |

### Divider / Spinner

| JSON path | Recurso Avalonia |
|---|---|
| `divider.color` | `Theme.Divider.Color` |
| `spinner.color` | `Theme.Spinner.Color` |

### Results

| JSON path | Recurso Avalonia |
|---|---|
| `results.cornerRadius` | `Theme.Results.CornerRadius` |
| `results.title.color` | `Theme.Results.Title.Color` |
| `results.title.size` | `Theme.Results.Title.Size` |
| `results.subtitle.color` | `Theme.Results.Subtitle.Color` |
| `results.subtitle.size` | `Theme.Results.Subtitle.Size` |
| `results.category.color` | `Theme.Results.Category.Color` |
| `results.category.size` | `Theme.Results.Category.Size` |
| `results.icon.background` | `Theme.Results.Icon.Background` |
| `results.icon.cornerRadius` | `Theme.Results.Icon.CornerRadius` |
| `results.shortcut.color` | `Theme.Results.Shortcut.Color` |
| `results.shortcut.background` | `Theme.Results.Shortcut.Background` |
| `results.shortcut.size` | `Theme.Results.Shortcut.Size` |
| `results.shortcut.cornerRadius` | `Theme.Results.Shortcut.CornerRadius` |
| `results.selection.background` | `Theme.Results.Selection.Background` |
| `results.selection.hoverBackground` | `Theme.Results.Selection.HoverBackground` |
| `results.selection.color` | `Theme.Results.Selection.Color` |
| `results.selection.iconBackground` | `Theme.Results.Selection.IconBackground` |
| `results.hover.background` | `Theme.Results.Hover.Background` |

### Calculator

| JSON path | Recurso Avalonia |
|---|---|
| `calculator.fontFamily` | `Theme.Calc.FontFamily` |
| `calculator.expression.color` | `Theme.Calc.Expression.Color` |
| `calculator.expression.size` | `Theme.Calc.Expression.Size` |
| `calculator.result.color` | `Theme.Calc.Result.Color` |
| `calculator.result.size` | `Theme.Calc.Result.Size` |
| `calculator.subtitle.color` | `Theme.Calc.Subtitle.Color` |
| `calculator.subtitle.size` | `Theme.Calc.Subtitle.Size` |
| `calculator.subtitle.opacity` | `Theme.Calc.Subtitle.Opacity` |
| `calculator.separator.color` | `Theme.Calc.Separator.Color` |
| `calculator.cell.background` | `Theme.Calc.Cell.Background` |
| `calculator.cell.cornerRadius` | `Theme.Calc.Cell.CornerRadius` |

### Converter

| JSON path | Recurso Avalonia |
|---|---|
| `converter.fontFamily` | `Theme.Conv.FontFamily` |
| `converter.value.color` | `Theme.Conv.Value.Color` |
| `converter.value.size` | `Theme.Conv.Value.Size` |
| `converter.subtitle.color` | `Theme.Conv.Subtitle.Color` |
| `converter.subtitle.size` | `Theme.Conv.Subtitle.Size` |
| `converter.subtitle.opacity` | `Theme.Conv.Subtitle.Opacity` |
| `converter.arrow.color` | `Theme.Conv.Arrow.Color` |
| `converter.hint.color` | `Theme.Conv.Hint.Color` |
| `converter.hint.size` | `Theme.Conv.Hint.Size` |
| `converter.cell.cornerRadius` | `Theme.Conv.Cell.CornerRadius` |

### Emoji

| JSON path | Recurso Avalonia |
|---|---|
| `emoji.columns` | `Theme.Emoji.Columns` |
| `emoji.viewportRows` | `Theme.Emoji.ViewportRows` |
| `emoji.cell.size` | `Theme.Emoji.Cell.Size` |
| `emoji.cell.cornerRadius` | `Theme.Emoji.Cell.CornerRadius` |
| `emoji.char.size` | `Theme.Emoji.Char.Size` |
| `emoji.char.fontFamily` | `Theme.Emoji.Char.FontFamily` |
| `emoji.name.color` | `Theme.Emoji.Name.Color` |
| `emoji.name.size` | `Theme.Emoji.Name.Size` |
| `emoji.keywords.color` | `Theme.Emoji.Keywords.Color` |
| `emoji.keywords.size` | `Theme.Emoji.Keywords.Size` |
| `emoji.keywords.opacity` | `Theme.Emoji.Keywords.Opacity` |

### No Results

| JSON path | Recurso Avalonia |
|---|---|
| `noResults.title.color` | `Theme.NoResults.Title.Color` |
| `noResults.title.size` | `Theme.NoResults.Title.Size` |
| `noResults.subtitle.color` | `Theme.NoResults.Subtitle.Color` |
| `noResults.subtitle.size` | `Theme.NoResults.Subtitle.Size` |

### Footer

| JSON path | Recurso Avalonia |
|---|---|
| `footer.border` | `Theme.Footer.Border` |
| `footer.text.color` | `Theme.Footer.Color` |
| `footer.text.size` | `Theme.Footer.Size` |

### ESC Badge

| JSON path | Recurso Avalonia |
|---|---|
| `escBadge.background` | `Theme.Esc.Background` |
| `escBadge.cornerRadius` | `Theme.Esc.CornerRadius` |
| `escBadge.text.color` | `Theme.Esc.Color` |
| `escBadge.text.size` | `Theme.Esc.Size` |

### Update Banner

| JSON path | Recurso Avalonia |
|---|---|
| `updateBanner.background` | `Theme.Update.Background` |
| `updateBanner.text.color` | `Theme.Update.Color` |
| `updateBanner.text.size` | `Theme.Update.Size` |

> **Verificar en:**
> - `ThemeService.Apply()` -- mapeo token JSON a recurso Avalonia.
> - `ThemeService.ApplyBuiltinDefault()` -- lista canonica de todos los tokens con sus valores por defecto.

---

## Fallback incorporado

Existe un fallback hardcodeado que replica exactamente el tema `dark-default.json`. Este fallback se activa en cualquiera de estos casos:

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

La lista de temas disponibles se construye a partir de los ficheros `*.json` en la carpeta `Themes/`, con las siguientes reglas:

1. Los ficheros se ordenan alfabeticamente por nombre.
2. Si dos ficheros producen el mismo ID (nombre sin extension), solo se incluye el primero. Esto puede ocurrir con copias de conflicto de iCloud como `dark-default 2.json`.
3. El nombre para mostrar se extrae del campo `"name"` del JSON. Si falla el parsing, se usa el ID del fichero como nombre.
4. Despues de los temas built-in, se escanean los temas de usuario en `AppPaths.PluginsDir` (ver seccion "Temas de usuario").
5. Si no se encuentra ningun tema, se anade un fallback `"dark-default"` / `"Dark Default"`.

La carpeta de temas se resuelve relativa al directorio del ejecutable (`AppContext.BaseDirectory`), no al directorio de trabajo actual.

Los ficheros JSON se copian al directorio de salida del build con `CopyToOutputDirectory=PreserveNewest`.

> **Verificar en:**
> - `ThemeService.AvailableThemes()` -- enumeracion y filtrado.
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

Los usuarios pueden instalar temas personalizados colocando ficheros JSON en `AppPaths.PluginsDir` (la carpeta `plugins/` dentro del directorio de configuracion de la app). El fichero debe incluir `"type": "theme"` (case-insensitive) para ser reconocido como tema.

### Identificacion

Los temas de usuario se identifican con el prefijo `user:` en su ID. Un fichero `my-theme.json` en la carpeta de plugins produce el ID `"user:my-theme"`. Esto evita colisiones con temas built-in.

### Formato

El formato es identico al de los temas built-in (mismas secciones y tokens), con la adicion obligatoria del campo `"type": "theme"` en el nivel raiz del JSON.

### Recarga automatica del tema activo

Si el tema seleccionado es de usuario, ThemeService vigila su fichero con un `FileSystemWatcher`. Al detectar un cambio (con debounce de 300ms), el tema se re-aplica automaticamente en el UI thread sin necesidad de reseleccionarlo.

### Actualizacion del picker en Settings

Un segundo watcher vigila la carpeta de plugins para detectar temas anadidos o eliminados. Cuando cambia la lista de ficheros, se dispara el evento `ThemesChanged` y el picker de temas en Settings se actualiza automaticamente, preservando la seleccion actual si sigue disponible.

> **Verificar en:**
> - `ThemeService.StartWatching()` -- creacion de watchers.
> - `ThemeService.WatchActiveTheme()` -- watcher del fichero activo.
> - `ThemeService.AvailableThemes()` -- escaneo de plugins con filtro `"type": "theme"`.
> - `SettingsWindowViewModel.OnThemesChanged()` -- actualizacion del picker.

---

## Temas incluidos

| Fichero | Variante |
|---|---|
| `dark-default.json` | dark |
| `dark-raycast.json` | dark |
| `dark-macos.json` | dark |
| `light-blue.json` | light |
| `light-gray.json` | light |
