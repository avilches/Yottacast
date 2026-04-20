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

Los temas solo afectan a la **ventana principal de búsqueda** (MainWindow). La ventana de Settings usa colores nativos de macOS independientemente del tema seleccionado.

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

Cada tema es un fichero `.json` en la carpeta `Themes/` del directorio de la aplicacion. La estructura contiene cuatro secciones:

| Seccion | Contenido | Ejemplo de token |
|---|---|---|
| `name` | Nombre para mostrar en el picker | `"Dark Default"` |
| `variant` | `"light"` o `"dark"` (controla el `ThemeVariant` de Avalonia) | `"dark"` |
| `colors` | 22 tokens de color en formato Avalonia (`#AARRGGBB` o `#RRGGBB`) | `"windowBackground": "#F21C1C22"` |
| `fonts` | 5 tamanos de fuente (numeros) | `"search": 18` |
| `layout` | 5 corner radius + ancho de ventana (numeros) | `"windowWidth": 700` |

Los campos `author` y `url` estan presentes en los JSON pero no se utilizan actualmente. Estan reservados para una futura funcionalidad de descarga de temas.

Si el valor de `variant` no es `"light"`, se asume `"dark"`.

> **Verificar en:**
> - `ThemeService.Apply()` -- lectura del JSON y asignacion de tokens.
> - Cualquier fichero en `Yottacast/Themes/*.json` como referencia de estructura.

---

## Tokens de tema disponibles

### Colores (seccion `colors`)

| Token JSON | Recurso Avalonia | Zona de la UI |
|---|---|---|
| `windowBackground` | `Theme.WindowBackground` | Fondo de la ventana principal |
| `searchText` | `Theme.SearchText` | Texto de la barra de busqueda |
| `searchPlaceholder` | `Theme.SearchPlaceholder` | Placeholder de la barra de busqueda |
| `searchCaret` | `Theme.SearchCaret` | Cursor de texto |
| `searchSelection` | `Theme.SearchSelection` | Seleccion de texto |
| `icon` | `Theme.Icon` | Icono principal |
| `divider` | `Theme.Divider` | Linea separadora |
| `itemIconBackground` | `Theme.ItemIconBackground` | Fondo del icono de cada resultado |
| `itemTitle` | `Theme.ItemTitle` | Titulo de cada resultado |
| `itemSubtitle` | `Theme.ItemSubtitle` | Subtitulo de cada resultado |
| `itemCategory` | `Theme.ItemCategory` | Etiqueta de categoria |
| `itemShortcutText` | `Theme.ItemShortcutText` | Texto del atajo de teclado |
| `itemShortcutBackground` | `Theme.ItemShortcutBackground` | Fondo del atajo de teclado |
| `itemSelection` | `Theme.ItemSelection` | Fondo del resultado seleccionado |
| `itemSelectionHover` | `Theme.ItemSelectionHover` | Fondo al hacer hover sobre el seleccionado |
| `itemHover` | `Theme.ItemHover` | Fondo al hacer hover sobre cualquier resultado |
| `itemSelectionText` | `Theme.ItemSelectionText` | Texto del resultado seleccionado |
| `itemSelectionIconBackground` | `Theme.ItemSelectionIconBackground` | Fondo del icono en el resultado seleccionado |
| `escBadgeBackground` | `Theme.EscBadgeBackground` | Fondo del badge ESC |
| `escBadgeText` | `Theme.EscBadgeText` | Texto del badge ESC |
| `footerBorder` | `Theme.FooterBorder` | Borde del footer |
| `footerText` | `Theme.FooterText` | Texto del footer |
| `noResultsTitle` | `Theme.NoResultsTitle` | Titulo cuando no hay resultados |
| `noResultsSubtitle` | `Theme.NoResultsSubtitle` | Subtitulo cuando no hay resultados |

### Fuentes (seccion `fonts`)

| Token JSON | Recurso Avalonia |
|---|---|
| `search` | `Theme.FontSizeSearch` |
| `title` | `Theme.FontSizeTitle` |
| `subtitle` | `Theme.FontSizeSubtitle` |
| `small` | `Theme.FontSizeSmall` |
| `noResults` | `Theme.FontSizeNoResults` |

### Layout (seccion `layout`)

| Token JSON | Recurso Avalonia |
|---|---|
| `windowCornerRadius` | `Theme.CornerRadiusWindow` |
| `itemCornerRadius` | `Theme.CornerRadiusItem` |
| `iconCornerRadius` | `Theme.CornerRadiusIcon` |
| `escCornerRadius` | `Theme.CornerRadiusEsc` |
| `shortcutCornerRadius` | `Theme.CornerRadiusShortcut` |
| `windowWidth` | `Theme.WindowWidth` |

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

1. Se excluye `settings.json` (usado para configuracion, no es un tema).
2. Los ficheros se ordenan alfabeticamente por nombre.
3. Si dos ficheros producen el mismo ID (nombre sin extension), solo se incluye el primero. Esto puede ocurrir con copias de conflicto de iCloud como `dark-default 2.json`.
4. El nombre para mostrar se extrae del campo `"name"` del JSON. Si falla el parsing, se usa el ID del fichero como nombre.
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

**Invariante:** un token ausente o invalido en el JSON nunca provoca una excepcion. El peor caso es que ese elemento de la UI conserve el valor del tema anterior o del fallback.

> **Verificar en:**
> - `ThemeService.SetBrush()`, `ThemeService.SetDouble()`, `ThemeService.SetCornerRadius()` -- comprobaciones de null y TryParse.

---

## Temas incluidos

| Fichero | Variante |
|---|---|
| `dark-default.json` | dark |
| `dark-raycast.json` | dark |
| `dark-macos.json` | dark |
| `light-blue.json` | light |
| `light-gray.json` | light |
