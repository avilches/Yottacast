# Continuación: bugs post-merge running apps pills

## Estado general

Dos bugs identificados al hacer merge de la feature. Esta sesión resolvió Bug 2 completamente y avanzó mucho en Bug 1, que tiene su causa raíz identificada pero el fix final pendiente.

---

## Bug 2 — Pills con texto blanco (RESUELTO ✅)

**Síntoma:** La pill "from clipboard" (InfoTag) aparecía como texto blanco pequeño sin fondo ni borde visible.

**Causa raíz (dos capas):**

1. **ThemeService no llamaba `ApplyBuiltinDefault()` antes de aplicar el JSON** → los tokens `Theme.Results.Tag.*` (nuevos en esta feature) nunca llegaban a `app.Resources` si el bloque `if (tags != null)` no se ejecutaba. Fix: añadir `ApplyBuiltinDefault()` al inicio de `Apply()`. Commiteado en `9f417cb`.

2. **`x:CompileBindings="False"` en un DataTemplate compilado rompe `DynamicResource`** → Los Borders de las pills tenían `x:CompileBindings="False"` (necesario originalmente para el binding `IsVisible` con `ObjectConverters.IsNotNull`). Dentro de un DataTemplate con `x:DataType` (compiled), ese flag hace que los `{DynamicResource}` en esos elementos no se conecten al árbol dinámico de recursos — se resuelven como estáticos o fallan silenciosamente.

   Investigación:
   - Confirmado mediante hardcoding (`Background="Red"`, `Foreground="Lime"`): el elemento SÍ renderiza cuando los colores son literales → no es un problema de visibilidad del Border.
   - Confirmado que los tokens SÍ están en `app.Resources` (log: `Tag.Info.Color=#ff0a84ff`) → no es problema de ThemeService.
   - Conclusión: es el `x:CompileBindings="False"` dentro del DataTemplate el que rompe el lookup dinámico.

**Fix aplicado** (`MainWindow.axaml`):
- Eliminado `x:CompileBindings="False"` de ambos Borders (RunningTag y InfoTag) y sus TextBlocks hijos.
- El binding `{Binding InfoTag, Converter={x:Static ObjectConverters.IsNotNull}}` funciona correctamente con compiled bindings.
- Añadidos estilos `ListBoxItem:selected TextBlock.running-tag-text` y `ListBoxItem:selected TextBlock.info-tag-text` para que el color no se sobreescriba al seleccionar el item (patrón ya existente: `dict-pill`).

**Estado:** Verificado en runtime con tema Dark Default — pill en cyan brillante, visible y con color correcto.

---

## Bug 1 — Running pill no aparece (CAUSA IDENTIFICADA, FIX PENDIENTE ⚠️)

**Síntoma:** Al buscar "Safari" (u otra app en ejecución), no aparece la pill verde "Running" ni las acciones de gestión.

### Diagnóstico paso a paso

**1. GetRunningApps devuelve 0 apps siempre**
Log: `GetRunningApps: found 0 running apps, first 3:`

**2. El array SÍ tiene contenido (rawCount=107)**
Añadido log de `rawCount` antes del loop → `rawCount=107`. El NSArray tiene 107 entries; el loop las filtra todas.

**3. El guard `respondsToSelector:bundlePath` filtra todo — P/Invoke incorrecto**
Commit `9f417cb` añadió:
```csharp
if (ObjcMsgSendArgByte(app, selResponds, selBundlePath) == 0) continue;
```
`ObjcMsgSendArgByte` usa `byte` como return type para BOOL. En arm64 macOS, este P/Invoke retorna 0 para todos los objetos → filtra 107/107.

Intentado fix: cambiar return type a `IntPtr` (`RaMsgSendBoolSel`). Resultado: mismo comportamiento — todos filtrados.

**4. Sin el guard → crash por NSException**
Al eliminar el guard, la app crashea con:
```
*** Terminating app due to uncaught exception 'NSInvalidArgumentException',
reason: '-[NSRunningApplication bundlePath]: unrecognized selector sent to instance'
```
NSException desde ObjC no es interceptable por C# `try/catch`.

**5. isKindOfClass:NSRunningApplication devuelve YES (objetos válidos)**
```
GetRunningApps[0]: responds="0" isKindOfClass="1"
GetRunningApps[1]: responds="0" isKindOfClass="1"
GetRunningApps[2]: responds="0" isKindOfClass="1"
```
- Los objetos SÍ son NSRunningApplication (o subclase) según `isKindOfClass:`.
- `respondsToSelector:bundlePath` retorna NO correctamente — `bundlePath` NO existe en la subclase privada del runtime.

**6. Causa raíz confirmada: `bundlePath` eliminado en macOS 16 (Darwin 25)**
El sistema es Darwin 25.5.0 (macOS 16 beta / Tahoe). En esta versión, las instancias de NSRunningApplication retornadas por `NSWorkspace.runningApplications` son de una subclase privada que no expone el método `bundlePath` (deprecado). Hay que usar `bundleURL` → `path` en su lugar.

**7. Intentado fix: `bundleURL` → `path` (sin crash pero sigue devolviendo 0)**
Código actual:
```csharp
var nsUrl = RaMsgSend(app, selBundleUrl);   // bundleURL
if (nsUrl == IntPtr.Zero) continue;
var nsPath = RaMsgSend(nsUrl, selUrlPath);  // .path
if (nsPath == IntPtr.Zero) continue;
```
No crashea, pero devuelve 0 apps. Probable motivo: `bundleURL` también devuelve IntPtr.Zero para la mayoría de las apps (apps del sistema sin bundle, daemons, etc.) O `bundleURL` también es un selector no reconocido para estos objetos.

**No investigado todavía:** Si `bundleURL` también es unrecognized selector, podría confirmar con un log del tipo:
```
if (nsUrl == IntPtr.Zero) logger.LogDebug("bundleURL null for item {I}", i);
```

### Estado actual del código

Ficheros con cambios sin commitear:
- `Yottacast.Core/Platform/MacOsPlatformProvider.cs` — Guard usa `isKindOfClass:` + `bundleURL`→`path`, más logs de diagnóstico temporales
- `Yottacast/Views/MainWindow.axaml` — Fix Bug 2 aplicado (sin `x:CompileBindings="False"`)
- `Yottacast/Services/ThemeService.cs` — Log diagnóstico temporal de tokens (`Tag.Info.Color=...`)

### Próximos pasos para Bug 1

1. **Verificar si `bundleURL` también es unrecognized selector** — añadir log que distinga entre "bundleURL retorna null" vs "bundleURL no existe". Si crashea → mismo problema que bundlePath.

2. **Probar con `executableURL` → `deletingLastPathComponent` → `path`** — otra ruta para obtener el directorio del bundle desde la URL del ejecutable.

3. **Probar acceso vía KVC** — `[app valueForKey:@"bundleURL"]` puede funcionar cuando el selector directo no está disponible en la subclase privada.

4. **Comparar paths con el caché de Spotlight** — Una vez que GetRunningApps devuelva paths, hay que verificar que los paths del NSRunningApplication coincidan con los del `_apps` cache. Pueden diferir (symlinks, trailing slash, `/private/` prefix). La comparación ya usa `StringComparer.OrdinalIgnoreCase` pero los paths podrían ser lógicamente distintos.

5. **Limpiar logs de diagnóstico** antes de commitear.

6. **Commitear ambos fixes** (Bug 1 + Bug 2) cuando Bug 1 funcione.

---

## Resumen de cambios buenos que hay que conservar

| Fichero | Cambio | Estado |
|---|---|---|
| `ThemeService.cs` | `ApplyBuiltinDefault()` antes de aplicar JSON | Commiteado en `9f417cb` ✅ |
| `MainWindow.axaml` | Quitar `x:CompileBindings="False"` en Borders de pills + estilos de selección | Sin commitear, testeado ✅ |
| `MacOsPlatformProvider.cs` | Retain/release del array, `catch (Exception ex)` con log | Commiteado en `9f417cb` ✅ |
| `MacOsPlatformProvider.cs` | Guard `isKindOfClass:` + `bundleURL`→`path` | Sin commitear, en progreso ⚠️ |
| `ThemeService.cs` | Log diagnóstico temporal (`Tag.Info.Color=...`) | Sin commitear, ELIMINAR antes de commit |

---

## Contexto técnico clave

- **Darwin 25 = macOS 16 Tahoe beta** — APIs de NSRunningApplication cambiaron. `bundlePath` (NSString, convenience wrapper) parece eliminado. Solo queda `bundleURL` (NSURL).
- **`x:CompileBindings="False"` dentro de DataTemplate compilado** — En Avalonia 11 con `AvaloniaUseCompiledBindingsByDefault=true`, este flag rompe `{DynamicResource}` en los elementos afectados. Patrón correcto: no mezclar ambos modos en el mismo DataTemplate.
- **BOOL return type en P/Invoke arm64** — `byte` como return type de `objc_msgSend` funciona incorrectamente. `IntPtr` funciona correctamente (confirmado: `isKindOfClass:` devuelve 1 con IntPtr return type).
- **NSException no capturable desde C#** — Hay que evitar llamar selectores que no existen en la clase real. El guard debe verificar ANTES de llamar el método potencialmente crasheable.
