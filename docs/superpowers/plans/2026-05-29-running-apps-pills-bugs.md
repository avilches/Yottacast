# Bugs post-merge: running apps pills

## Contexto

Feature mergeada en `main` (commits `af2870a` → `4141dea`). El usuario probó la app y reportó dos bugs.

---

## Bug 1 — Running pill no aparece

**Síntoma:** Al buscar una app que está en ejecución, no aparece la pill verde "Running" ni las acciones "Bring to Front / Quit / Force Quit". El `RunningTag` es siempre `null`.

**Causa probable:** `MacOsPlatformProvider.GetRunningApps()` devuelve `[]` en runtime. El bloque original era `catch { return []; }` — tragaba cualquier excepción sin dejar rastro. No sabemos si:
- El P/Invoke a NSWorkspace falla (excepción ObjC, autorelease pool, etc.)
- Los paths de NSWorkspace no coinciden con los del caché de Spotlight

**Cambios aplicados para diagnosticar** (en `main`, sin commit aún):
- `MacOsPlatformProvider.GetRunningApps()`: check de `workspace == IntPtr.Zero` con warning; `catch (Exception ex)` con `LogWarning`; `LogDebug` al final con count y 3 primeros paths
- `ApplicationSearch.Search()`: `LogDebug` con el conteo de apps running y sample de paths cuando `runningByPath.Count > 0`

**Próximos pasos:**
1. Arrancar la app con `dotnet run`
2. Buscar una app que esté corriendo (ej. "Finder")
3. Revisar `user-data/logs/yottacast-*.log` filtrando por `GetRunningApps` y `AppSearch running=`
4. Si hay excepción en el log → investigar el error concreto
5. Si hay apps pero no coinciden paths → comparar los paths de NSWorkspace con los del `_apps` cache (Spotlight), puede ser un problema de resolución de symlinks o trailing slash

---

## Bug 2 — Pills con texto blanco sobre fondo transparente

**Síntoma:** La pill "from clipboard" (InfoTag) es visible pero muestra texto blanco sobre fondo transparente — sin color de texto, sin borde. El DynamicResource no resuelve los tokens `Theme.Results.Tag.*`.

**Causa raíz confirmada:** `ThemeService.Apply()` aplicaba tokens del JSON pero NO llamaba primero a `ApplyBuiltinDefault()`. Los recursos `Theme.Results.Tag.*` son nuevos en esta feature. Si el bloque `if (tags != null)` no se ejecuta (excepción silenciosa, tema sin esa sección), los recursos nunca entran en `Application.Resources`. El `DynamicResource` no los encuentra y el `TextBlock` hereda el blanco del padre.

La diferencia con tokens antiguos (ej. `Theme.Results.Title.Color`) es que estos llevan releases en todos los temas Y en `ApplyBuiltinDefault()`. Los tokens de tags son nuevos y sólo están en los temas actualizados.

**Fix aplicado** (en `main`, sin commit):
- `ThemeService.Apply()`: se llama `ApplyBuiltinDefault()` justo después de validar el JSON (antes de aplicar los tokens del JSON). Esto garantiza que todos los tokens tienen valor base antes del override del JSON.

**Ficheros modificados sin commit:**
- `Yottacast/Services/ThemeService.cs`
- `Yottacast.Core/Platform/MacOsPlatformProvider.cs`
- `Yottacast.Core/Search/Application/ApplicationSearch.cs`

---

## Estado de los cambios

```
git diff --stat HEAD
```

Los 3 ficheros arriba tienen cambios locales sin commitear. Para continuar:

```bash
cd /Users/avilches/Work/Proy/Other/Yottacast
git diff --stat HEAD
```

---

## Para continuar

1. Probar si Bug 2 (estilos) está resuelto con el fix de `ApplyBuiltinDefault()` first
2. Ejecutar la app y revisar logs para Bug 1 (running apps)
3. Según los logs: o bien arreglar el P/Invoke o bien normalizar los paths antes del dict lookup
4. Commitear los fixes (quitando los `LogDebug` de diagnóstico si ya no hacen falta, o dejándolos como `Debug` permanentes)
5. Tests: el fix de ThemeService no necesita tests nuevos (es infraestructura). Si Bug 1 resulta en cambio de código en GetRunningApps, añadir test en `ApplicationSearchTests` si aplica
