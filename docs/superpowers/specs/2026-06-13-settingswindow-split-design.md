# Split de SettingsWindow.axaml en UserControls por seccion

Fecha: 2026-06-13
Tipo: refactor de mantenimiento (sin cambio visual ni funcional)
Origen: ultimo item pendiente de Arquitectura del review `pending/review-result-20260612193846.md`.

## Problema

`Yottacast/Views/SettingsWindow.axaml` tiene 1683 lineas: un unico fichero que mezcla el chrome de la
ventana (grid, sidebar, divider, scroll), todos los estilos de campos, los iconos vectoriales y las 12
secciones de contenido. Es dificil de navegar y de editar con seguridad. El objetivo es trocearlo en
unidades enfocadas, **manteniendo identico el aspecto y el comportamiento**.

## Estado actual (lo que se trocea)

Layout en un `Grid` de 3 columnas:
- **Columna 0 — Sidebar**: 12 nav-buttons, cada uno con `Command="{Binding SelectXxxCommand}"` y
  `Classes.nav-selected="{Binding IsXxxSelected}"`. Usa el estilo `nav-item` y varios `Icon.*`.
- **Columna 1 — Divider** vertical.
- **Columna 2 — Contenido**: un `ScrollViewer` con un `StackPanel` que contiene 12 secciones, cada una
  `<StackPanel IsVisible="{Binding IsXxxSelected}">`.

Las 12 secciones (con su rango aproximado en el AXAML actual, como guia para la extraccion):

| Seccion     | Binding visibilidad     | Rango aprox. |
|-------------|-------------------------|--------------|
| General     | `IsGeneralSelected`     | 593-698      |
| AppSearch   | `IsAppSearchSelected`   | 699-768      |
| WebSearch   | `IsWebSearchSelected`   | 769-1007     |
| FileSearch  | `IsFileSearchSelected`  | 1008-1086    |
| FileEditor  | `IsFileEditorSelected`  | 1087-1135    |
| Calculator  | `IsCalculatorSelected`  | 1136-1400    |
| Clipboard   | `IsClipboardSelected`   | 1401-1461    |
| Emoji       | `IsEmojiSelected`       | 1462-1471    |
| Dictionary  | `IsDictionarySelected`  | 1472-1520    |
| DateSearch  | `IsDateSearchSelected`  | 1521-1556    |
| History     | `IsHistorySelected`     | 1557-1604    |
| Permissions | `IsPermissionsSelected` | 1605-~1660   |

Recursos y estilos:
- **`Window.Resources`** (lineas 17-34): 13 `StreamGeometry` con clave `Icon.*`. Usados por el sidebar y
  por algunas secciones de contenido (`Icon.Edit`, `Icon.FileSearch`, `Icon.Folder`, `Icon.General`,
  `Icon.Plugin`).
- **`Window.Styles`** (lineas 39-444): estilos de `nav-item` (solo sidebar) y de campos
  (section-heading, field-label, combobox, numeric-field, textbox, hotkey field, badges, botones,
  description text, checkboxes compactos, subsection heading...) mas estilos especificos de una sola
  seccion: `engine*` (tabla de motores, WebSearch) y `example*` (filas de ejemplo, Calculator).
- **Colores**: los del tema via `DynamicResource Theme.*` (se resuelven hacia arriba en el arbol, NO
  estan hardcodeados como ThemeDictionaries). Algunos literales inline: `#FF3B30` (rojo de captura de
  hotkey, intencional), `#868686`, `#565656`, `#707070`.

## Diseno objetivo

Nueva carpeta `Yottacast/Views/Settings/`.

### Host: `SettingsWindow.axaml`
Conserva el chrome: `Grid` de 3 columnas, sidebar (nav-buttons + estilos `nav-item` + los `Icon.*` del
sidebar), divider, y el `ScrollViewer`. El contenido pasa a componer los 12 UserControls, cada uno
envuelto con su `IsVisible` (la decision de visibilidad sigue en el host):

```xml
<settings:SettingsGeneralView    IsVisible="{Binding IsGeneralSelected}"/>
<settings:SettingsAppSearchView  IsVisible="{Binding IsAppSearchSelected}"/>
<!-- ... una linea por seccion ... -->
<settings:SettingsPermissionsView IsVisible="{Binding IsPermissionsSelected}"/>
```

El **code-behind `SettingsWindow.axaml.cs` (257 lineas) NO se toca**: el alcance es solo el AXAML.

### 12 UserControls (uno por seccion)
Nombres: `SettingsGeneralView`, `SettingsAppSearchView`, `SettingsWebSearchView`,
`SettingsFileSearchView`, `SettingsFileEditorView`, `SettingsCalculatorView`, `SettingsClipboardView`,
`SettingsEmojiView`, `SettingsDictionaryView`, `SettingsDateSearchView`, `SettingsHistoryView`,
`SettingsPermissionsView`.

Cada UserControl:
- Namespace `Yottacast.Views.Settings`.
- Contiene SOLO el markup de su `<StackPanel>` de seccion (sin el `IsVisible`, que vive en el host).
- Declara `x:DataType="vm:SettingsWindowViewModel"` para compiled bindings (habilitados globalmente).
- **No fija DataContext propio**: hereda el del host (la misma instancia de `SettingsWindowViewModel`).
  Los bindings (`{Binding SelectedTheme}`, commands, etc.) funcionan sin cambios.
- Incluye los recursos compartidos que use (ver abajo).
- Los estilos **especificos de esa seccion** (p.ej. `engine*` en WebSearch, `example*` en Calculator)
  se mueven a su propio `<UserControl.Styles>`.
- Su code-behind es el minimo de Avalonia (`InitializeComponent`), sin logica.

### Recursos compartidos (2 ficheros)
- **`SettingsSharedResources.axaml`** — `ResourceDictionary` con los 13 `StreamGeometry Icon.*`.
  Incluido via `MergedDictionaries` por el host (sidebar) y por cada UserControl que use iconos.
- **`SettingsSharedStyles.axaml`** — `Styles` con los estilos de campos compartidos por varias
  secciones (section-heading, field-label, combobox, numeric-field, textbox, hotkey field, badges,
  botones genericos, description text, checkboxes compactos, subsection heading). Incluido via
  `StyleInclude` por cada UserControl que los use.

Criterio compartido-vs-especifico: un estilo va a `SettingsSharedStyles.axaml` si lo usan >=2 secciones;
si solo lo usa una, va dentro de ese UserControl. Los de `nav-item` se quedan en el host (solo sidebar).

## Datos y comportamiento

Sin cambios. Misma instancia de `SettingsWindowViewModel` (sigue siendo transient, una por apertura),
mismas propiedades, commands y eventos. Mismo aspecto pixel a pixel, incluido el `#FF3B30` de captura de
hotkey y la resolucion de `DynamicResource Theme.*`.

## Verificacion

No hay proyecto de tests de UI, asi que la red de seguridad es manual:
1. `dotnet build Yottacast.sln` sin errores.
2. `cd Yottacast && dotnet run`, abrir Settings (Cmd+,) y recorrer **las 12 secciones** comprobando que
   cada una se ve y se comporta identica a antes: iconos, encabezados, combos, campos numericos (sin
   borde doble ni error de validacion), captura de hotkey con su color rojo, badges de modificadores,
   tabla de motores y su flyout de edicion de URL (WebSearch), filas de ejemplo (Calculator), toggles.
3. Confirmar navegacion del sidebar (seleccion resaltada) y scroll.

## Fuera de alcance (YAGNI)

- No se trocea ni se modifica el ViewModel ni el code-behind.
- No se renombran propiedades, commands ni bindings.
- No se cambia ningun color, fuente ni estilo (mismo look exacto).
- No se introducen abstracciones nuevas (clases base de seccion, etc.).

## Riesgos y mitigacion

- **StaticResource de iconos no resuelto en un UserControl**: mitigado moviendo los iconos a
  `SettingsSharedResources.axaml` e incluyendolo donde se use (las vistas quedan autocontenidas).
- **Compiled bindings que no resuelven**: mitigado con `x:DataType` en cada UserControl.
- **Regresion visual silenciosa** (sin tests): mitigada con el recorrido manual de las 12 secciones
  antes de dar por terminado, comparando con el estado actual.
