# SettingsWindow split Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Trocear `Yottacast/Views/SettingsWindow.axaml` (1683 lineas) en 12 UserControls (uno por seccion) mas 2 ficheros de recursos compartidos, sin cambiar el aspecto ni el comportamiento.

**Architecture:** El host `SettingsWindow.axaml` conserva el chrome (grid 3 columnas, sidebar, divider, scroll) y compone los 12 UserControls, cada uno envuelto con su `IsVisible`. Cada UserControl contiene solo el markup de su seccion, declara `x:DataType="vm:SettingsWindowViewModel"`, hereda el DataContext del host e incluye los recursos compartidos que use. Iconos y estilos comunes viven en `SettingsSharedResources.axaml` y `SettingsSharedStyles.axaml`.

**Tech Stack:** Avalonia 11.3.12 (UserControl, ResourceInclude, StyleInclude), compiled bindings, .NET 9. No hay proyecto de tests de UI: la verificacion es `dotnet build` + recorrido visual manual.

---

## Notas transversales (leer antes de empezar)

- **Sin tests automatizados de UI.** Cada tarea verifica con `dotnet build Yottacast.sln` (valida sintaxis AXAML y compiled bindings) y, en los checkpoints marcados, con `cd Yottacast && dotnet run` + revision visual de la(s) seccion(es) afectada(s).
- **No tocar** `SettingsWindow.axaml.cs` ni `SettingsWindowViewModel.cs`. Solo AXAML.
- **No cambiar** ningun color, fuente, tamanio ni estilo. El resultado debe ser identico pixel a pixel.
- **Convencion de includes** (Avalonia):
  - ResourceDictionary compartido en un control:
    ```xml
    <UserControl.Resources>
      <ResourceDictionary>
        <ResourceDictionary.MergedDictionaries>
          <ResourceInclude Source="avares://Yottacast/Views/Settings/SettingsSharedResources.axaml"/>
        </ResourceDictionary.MergedDictionaries>
      </ResourceDictionary>
    </UserControl.Resources>
    ```
  - Styles compartidos en un control:
    ```xml
    <UserControl.Styles>
      <StyleInclude Source="avares://Yottacast/Views/Settings/SettingsSharedStyles.axaml"/>
    </UserControl.Styles>
    ```
- **DataContext**: el host ya fija `x:DataType` y su `DataContext` es la instancia de `SettingsWindowViewModel`. Los UserControls hijos heredan ese DataContext; solo necesitan declarar `x:DataType="vm:SettingsWindowViewModel"` para que los compiled bindings resuelvan. No fijar `DataContext` en los UserControls.
- **Estilos especificos por seccion ya identificados**: los selectores `engine*` / mode toggle / inputs del flyout de edicion de URL son de **WebSearch**; los `example*` (filas de ejemplo) son de **Calculator**. El resto de selectores de campos son compartidos. En cada tarea de seccion, si al copiar el markup aparece un `Classes`/selector usado SOLO por esa seccion y no esta en `SettingsSharedStyles.axaml`, moverlo al `<UserControl.Styles>` de esa vista.

## File Structure

Crear (carpeta nueva `Yottacast/Views/Settings/`):
- `SettingsSharedResources.axaml` — ResourceDictionary con los 13 `Icon.*` (StreamGeometry).
- `SettingsSharedStyles.axaml` — Styles de campos compartidos.
- `SettingsGeneralView.axaml` (+ `.axaml.cs`)
- `SettingsAppSearchView.axaml` (+ `.axaml.cs`)
- `SettingsWebSearchView.axaml` (+ `.axaml.cs`)
- `SettingsFileSearchView.axaml` (+ `.axaml.cs`)
- `SettingsFileEditorView.axaml` (+ `.axaml.cs`)
- `SettingsCalculatorView.axaml` (+ `.axaml.cs`)
- `SettingsClipboardView.axaml` (+ `.axaml.cs`)
- `SettingsEmojiView.axaml` (+ `.axaml.cs`)
- `SettingsDictionaryView.axaml` (+ `.axaml.cs`)
- `SettingsDateSearchView.axaml` (+ `.axaml.cs`)
- `SettingsHistoryView.axaml` (+ `.axaml.cs`)
- `SettingsPermissionsView.axaml` (+ `.axaml.cs`)

Modificar:
- `Yottacast/Views/SettingsWindow.axaml` — quita el contenido de las 12 secciones (reemplazado por los controls), y al final migra iconos/estilos compartidos a los ficheros shared.

---

## Task 1: Recursos compartidos de iconos

**Files:**
- Create: `Yottacast/Views/Settings/SettingsSharedResources.axaml`
- Reference: `Yottacast/Views/SettingsWindow.axaml:20-33` (los 13 `StreamGeometry`)

- [ ] **Step 1: Crear el ResourceDictionary**

Crear `Yottacast/Views/Settings/SettingsSharedResources.axaml` con esta cabecera y, dentro, los 13 `<StreamGeometry x:Key="Icon.*">...</StreamGeometry>` copiados **literalmente** de `SettingsWindow.axaml:20-33` (no alterar los path data):

```xml
<ResourceDictionary xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <!-- Iconos vectoriales para Settings (Bootstrap Icons 16x16, MIT). Copiados de SettingsWindow.axaml. -->
    <!-- Icon.General, Icon.AppSearch, Icon.WebSearch, Icon.FileSearch, Icon.Calculator, Icon.Clipboard,
         Icon.Emoji, Icon.Plugin, Icon.Folder, Icon.Edit, Icon.Dictionary, Icon.History,
         Icon.SystemSettings, Icon.Permissions -->
</ResourceDictionary>
```

Nota: copiar todas las claves presentes en el rango (son las que listan los comentarios del AXAML original; incluir exactamente las que existan alli, ni mas ni menos).

- [ ] **Step 2: Build**

Run: `dotnet build Yottacast.sln`
Expected: 0 errores (los warnings preexistentes de Avalonia/xUnit son normales). El fichero aun no se usa; solo debe compilar como recurso embebido.

- [ ] **Step 3: Commit**

```bash
git add Yottacast/Views/Settings/SettingsSharedResources.axaml
git commit -m "refactor(settings): extraer iconos compartidos a SettingsSharedResources.axaml"
```

---

## Task 2: Estilos compartidos

**Files:**
- Create: `Yottacast/Views/Settings/SettingsSharedStyles.axaml`
- Reference: `Yottacast/Views/SettingsWindow.axaml:39-444` (bloque `Window.Styles`)

- [ ] **Step 1: Crear el fichero de Styles compartidos**

Crear `Yottacast/Views/Settings/SettingsSharedStyles.axaml`. Copiar dentro **todos** los `<Style Selector=...>` del bloque `Window.Styles` (lineas 39-444) EXCEPTO:
- `Button.nav-item` y `Button.nav-item:pointerover` (solo sidebar; se quedan en el host).
- Los selectores especificos de WebSearch (tabla de motores `engine*`, mode toggle, inputs del flyout de edicion de URL) — iran en `SettingsWebSearchView` (Task 5).
- Los selectores especificos de Calculator (`example*` filas de ejemplo) — iran en `SettingsCalculatorView` (Task 8).

Cabecera del fichero:

```xml
<Styles xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <!-- Estilos de campos compartidos por varias secciones de Settings.
         section-heading, field-label, combobox stretch, numeric-field, textbox interior,
         supresion de error de validacion, hotkey field, modifier badge, hotkey key text,
         remove/add button, folder missing indicator, description text, icon-only button,
         checkboxes compactos, subsection heading. -->
</Styles>
```

Copiar los estilos **literalmente** (mismos selectores, setters, templates y `DynamicResource`/`StaticResource`). No cambiar valores.

- [ ] **Step 2: Build**

Run: `dotnet build Yottacast.sln`
Expected: 0 errores. Standalone `Styles` debe compilar. Aun no se usa.

- [ ] **Step 3: Commit**

```bash
git add Yottacast/Views/Settings/SettingsSharedStyles.axaml
git commit -m "refactor(settings): extraer estilos de campos compartidos a SettingsSharedStyles.axaml"
```

---

## Task 3: Vista piloto — SettingsGeneralView (patron completo)

Esta tarea define el **procedimiento de extraccion de seccion** que reutilizan las Tasks 4-14. Hazla con cuidado: es la plantilla.

**Files:**
- Create: `Yottacast/Views/Settings/SettingsGeneralView.axaml`
- Create: `Yottacast/Views/Settings/SettingsGeneralView.axaml.cs`
- Modify: `Yottacast/Views/SettingsWindow.axaml` (anadir xmlns + reemplazar el bloque de la seccion General, ~593-698)

- [ ] **Step 1: Crear el code-behind**

`Yottacast/Views/Settings/SettingsGeneralView.axaml.cs`:

```csharp
using Avalonia.Controls;

namespace Yottacast.Views.Settings;

public partial class SettingsGeneralView : UserControl {
    public SettingsGeneralView() {
        InitializeComponent();
    }
}
```

- [ ] **Step 2: Crear el UserControl AXAML**

`Yottacast/Views/Settings/SettingsGeneralView.axaml`. Estructura: cabecera de UserControl con `x:DataType`, includes de recursos/estilos compartidos, y como raiz el `<StackPanel>` que hoy es la seccion General en `SettingsWindow.axaml` (lineas ~593-698) **sin** el atributo `IsVisible` (la visibilidad la pone el host). Copiar el contenido interno literalmente.

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:Yottacast.ViewModels"
             x:Class="Yottacast.Views.Settings.SettingsGeneralView"
             x:DataType="vm:SettingsWindowViewModel">
    <UserControl.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ResourceInclude Source="avares://Yottacast/Views/Settings/SettingsSharedResources.axaml"/>
            </ResourceDictionary.MergedDictionaries>
        </ResourceDictionary>
    </UserControl.Resources>
    <UserControl.Styles>
        <StyleInclude Source="avares://Yottacast/Views/Settings/SettingsSharedStyles.axaml"/>
    </UserControl.Styles>

    <!-- Pegar aqui el contenido de la seccion General de SettingsWindow.axaml (~593-698),
         empezando por el <StackPanel Spacing="20" ...> pero SIN el atributo
         IsVisible="{Binding IsGeneralSelected}". -->
</UserControl>
```

Si el markup copiado usa namespaces adicionales (p.ej. `xmlns:svc`, `xmlns:sys`, conversores), anadirlos a la cabecera del UserControl igual que estan en `SettingsWindow.axaml:1-5`. La seccion General usa bindings simples (`SelectedTheme`, hotkey, `SelectedTerminal`, `SelectedKeepValuePreset`); revisar y anadir solo los xmlns que aparezcan en el markup pegado.

- [ ] **Step 3: Referenciar el control desde el host**

En `Yottacast/Views/SettingsWindow.axaml`, anadir el namespace en la cabecera `<Window ...>` (junto a los otros xmlns, lineas 1-5):

```xml
xmlns:settings="using:Yottacast.Views.Settings"
```

Luego, en el `ScrollViewer` (columna 2), reemplazar **todo** el `<StackPanel ... IsVisible="{Binding IsGeneralSelected}"> ... </StackPanel>` de la seccion General por:

```xml
<settings:SettingsGeneralView IsVisible="{Binding IsGeneralSelected}"/>
```

- [ ] **Step 4: Build**

Run: `dotnet build Yottacast.sln`
Expected: 0 errores. Si falla por binding no resuelto, revisar que `x:DataType` y los xmlns esten correctos en el UserControl.

- [ ] **Step 5: Checkpoint visual**

Run: `cd Yottacast && dotnet run`
Abrir Settings (Cmd+,), seccion **General**. Verificar identico a antes: selector de tema, captura de hotkey global (con el color rojo al capturar), terminal, sticky window, keep-value. Cerrar la app.

- [ ] **Step 6: Commit**

```bash
git add Yottacast/Views/Settings/SettingsGeneralView.axaml Yottacast/Views/Settings/SettingsGeneralView.axaml.cs Yottacast/Views/SettingsWindow.axaml
git commit -m "refactor(settings): extraer seccion General a SettingsGeneralView"
```

---

## Tasks 4-14: Resto de secciones (mismo procedimiento que Task 3)

Para cada seccion, repetir EXACTAMENTE los 6 pasos de la Task 3 con los parametros de la fila correspondiente:
- Step 1: code-behind identico al de Task 3 cambiando el nombre de clase (`public partial class <ClassName> : UserControl`).
- Step 2: UserControl con la misma cabecera/includes de Task 3 cambiando `x:Class`, pegando el rango indicado **sin** su `IsVisible`, y moviendo los **estilos especificos** indicados a `<UserControl.Styles>` (despues del `StyleInclude`). Anadir los xmlns extra que el markup pegado use.
- Step 3: en el host, reemplazar el `<StackPanel ... IsVisible="{Binding <Binding>}">...</StackPanel>` por `<settings:<ClassName> IsVisible="{Binding <Binding>}"/>` (el `xmlns:settings` ya se anadio en Task 3).
- Step 4: `dotnet build Yottacast.sln` -> 0 errores.
- Step 5: checkpoint visual de esa seccion.
- Step 6: commit `refactor(settings): extraer seccion <X> a <ClassName>`.

| Task | ClassName / fichero            | Binding                  | Rango aprox. | Estilos especificos a mover a la vista |
|------|--------------------------------|--------------------------|--------------|----------------------------------------|
| 4    | `SettingsAppSearchView`        | `IsAppSearchSelected`    | 699-768      | ninguno (usa solo compartidos)         |
| 5    | `SettingsWebSearchView`        | `IsWebSearchSelected`    | 769-1007     | `engine*` (tabla de motores: column header, alias TextBlock/TextBox, custom URL TextBox, edit-url button), mode toggle button, inputs del flyout de edicion de URL |
| 6    | `SettingsFileSearchView`       | `IsFileSearchSelected`   | 1008-1086    | ninguno (folder missing indicator es compartido)         |
| 7    | `SettingsFileEditorView`       | `IsFileEditorSelected`   | 1087-1135    | ninguno                                 |
| 8    | `SettingsCalculatorView`       | `IsCalculatorSelected`   | 1136-1400    | `example*` (example row button, expression column, description column) |
| 9    | `SettingsClipboardView`        | `IsClipboardSelected`    | 1401-1461    | ninguno                                 |
| 10   | `SettingsEmojiView`            | `IsEmojiSelected`        | 1462-1471    | ninguno                                 |
| 11   | `SettingsDictionaryView`       | `IsDictionarySelected`   | 1472-1520    | ninguno                                 |
| 12   | `SettingsDateSearchView`       | `IsDateSearchSelected`   | 1521-1556    | ninguno                                 |
| 13   | `SettingsHistoryView`          | `IsHistorySelected`      | 1557-1604    | ninguno                                 |
| 14   | `SettingsPermissionsView`      | `IsPermissionsSelected`  | 1605-fin del ScrollViewer | ninguno                    |

Notas por seccion:
- **Task 5 (WebSearch)** es la mas grande y la unica con muchos estilos propios. Mover TODOS los selectores `engine*`, el mode toggle button y los estilos de inputs del flyout de edicion de URL desde donde quedaron (o desde el host, si no se movieron en Task 2) al `<UserControl.Styles>` de esta vista, debajo del `StyleInclude`. Verificar a fondo en el checkpoint: tabla de motores, edicion de alias, flyout de URL custom, toggle de modo (prefix/showAlways).
- **Task 8 (Calculator)** mover los `example*` a su `<UserControl.Styles>`. Verificar las filas de ejemplo "try-it" y los campos numericos.
- En cualquier seccion, si al pegar aparece un selector usado solo por ella y aun presente en `SettingsSharedStyles.axaml`, moverlo aqui y quitarlo del shared.

---

## Task 15: Limpieza del host

Tras extraer las 12 secciones, el `ScrollViewer` del host ya solo contiene 12 lineas `<settings:...View IsVisible=.../>`. Ahora se eliminan del host los recursos/estilos ya migrados y se hace que el sidebar use el fichero compartido de iconos.

**Files:**
- Modify: `Yottacast/Views/SettingsWindow.axaml`

- [ ] **Step 1: Migrar los iconos del host al include compartido**

En `SettingsWindow.axaml`, reemplazar el `Window.Resources` que hoy define los 13 `StreamGeometry` inline (lineas ~17-34) por un merge del fichero compartido, para que el sidebar siga resolviendo `Icon.*`:

```xml
<Window.Resources>
    <ResourceDictionary>
        <ResourceDictionary.MergedDictionaries>
            <ResourceInclude Source="avares://Yottacast/Views/Settings/SettingsSharedResources.axaml"/>
        </ResourceDictionary.MergedDictionaries>
    </ResourceDictionary>
</Window.Resources>
```

- [ ] **Step 2: Eliminar del host los estilos ya compartidos**

En el bloque `Window.Styles`, eliminar todos los `<Style Selector=...>` que se copiaron a `SettingsSharedStyles.axaml` en Task 2 (los de campos compartidos). **Conservar** unicamente `Button.nav-item` y `Button.nav-item:pointerover` (los usa el sidebar, que sigue en el host). Si quedaron en el host estilos `engine*`/`example*` que ya se movieron a sus vistas, eliminarlos tambien.

- [ ] **Step 3: Build**

Run: `dotnet build Yottacast.sln`
Expected: 0 errores. Si algun `Icon.*` o estilo no resuelve, revisar que el `ResourceInclude` del sidebar apunte bien y que `nav-item` siga en el host.

- [ ] **Step 4: Checkpoint visual COMPLETO**

Run: `cd Yottacast && dotnet run`
Abrir Settings y recorrer **las 12 secciones** una por una, comparando con el comportamiento previo:
- Sidebar: iconos correctos y resaltado de seleccion.
- General: tema, captura de hotkey (color rojo), terminal, sticky, keep-value.
- AppSearch / FileSearch: carpetas, indicador de carpeta ausente.
- WebSearch: tabla de motores, edicion de alias, flyout de URL custom, toggle de modo.
- Calculator: filas de ejemplo, campos numericos sin borde doble ni error de validacion.
- Clipboard, Emoji, Dictionary, DateSearch, History, Permissions: campos, combos, toggles.
Scroll y navegacion fluidos. Cerrar la app.

- [ ] **Step 5: Commit**

```bash
git add Yottacast/Views/SettingsWindow.axaml
git commit -m "refactor(settings): el host usa recursos compartidos y conserva solo el chrome"
```

---

## Task 16: Verificacion final y docs

**Files:**
- Modify (si aplica): `docs/ui-themes.md` (nota sobre donde viven ahora los recursos de Settings)

- [ ] **Step 1: Build + tests completos**

Run:
```bash
dotnet build Yottacast.sln
cd Yottacast.Core.Tests && dotnet test
cd ../Yottacast.Ipc.Tests && dotnet test
```
Expected: build 0 errores; Core 1387 pasan / 1 skip; IPC 26 pasan (el split no toca Core/IPC, deben seguir verdes).

- [ ] **Step 2: Actualizar doc si corresponde**

Revisar `docs/ui-themes.md`: la nota dice que los colores nativos de Settings estan hardcodeados en `SettingsWindow.axaml`. Tras el split, los **estilos e iconos** compartidos viven en `Yottacast/Views/Settings/SettingsShared*.axaml` y cada seccion en su `Settings<X>View.axaml`; los colores siguen via `DynamicResource Theme.*` inyectados por `AppHandler.ApplySettingsTheme`. Actualizar la nota para reflejar la nueva ubicacion **sin** referenciar el estado anterior (describir solo el estado actual). Si se edita, commit:

```bash
git add docs/ui-themes.md
git commit -m "docs: actualizar ui-themes con la nueva estructura de Settings"
```

- [ ] **Step 3: Confirmar arbol limpio**

Run: `git status`
Expected: working tree limpio (salvo handoffs sin trackear preexistentes). Plan completado.
