# System Settings Deep Search

**Fecha:** 2026-04-28
**Área:** `Yottacast.Core/Search/SystemSettings/`
**macOS verificado:** Ventura 13 / Sonoma 14

---

## Objetivo

Ampliar la búsqueda de System Settings para que el usuario encuentre sub-secciones concretas dentro de los paneles (p.ej. "Camera", "Keyboard Shortcuts", "Night Shift") y vea items dinámicos basados en el estado actual del sistema (red Wi-Fi conectada, VPN activa).

---

## Modelo de datos

### `SystemSettingsPanel`

Se añade el campo opcional `ParentName`:

```csharp
public sealed record SystemSettingsPanel(
    string Name,
    string UrlIdentifier,
    bool IsBuiltin = true,
    string? ParentName = null);
```

El subtítulo visible del resultado se deriva de `ParentName`:
- `ParentName = null` → `"System Settings"` (comportamiento existente para paneles de primer nivel)
- `ParentName = "Privacy & Security"` → `"System Settings › Privacy & Security"`

El `UrlIdentifier` ya soporta anchors de forma natural. La URL scheme de macOS acepta el formato `bundleId?anchor` y el comando `open` lo maneja sin cambios adicionales.

---

## Catálogo expandido (`BuiltinPanels.cs`)

El catálogo pasa de ~45 paneles de primer nivel a ~200 items organizados en dos grupos:

### Paneles de primer nivel
Los ~45 paneles existentes. Ningún cambio funcional; se añade `ParentName = null` explícito si es necesario para claridad.

### Sub-items curados
Items más granulares con anchors verificados en macOS Ventura+. Ejemplos:

| Name | UrlIdentifier | ParentName |
|------|---------------|------------|
| Camera | `com.apple.preference.security?Privacy_Camera` | Privacy & Security |
| Microphone | `com.apple.preference.security?Privacy_Microphone` | Privacy & Security |
| Location Services | `com.apple.preference.security?Privacy_LocationServices` | Privacy & Security |
| Full Disk Access | `com.apple.preference.security?Privacy_AllFiles` | Privacy & Security |
| Contacts | `com.apple.preference.security?Privacy_ContactsFull` | Privacy & Security |
| Calendars | `com.apple.preference.security?Privacy_Calendars` | Privacy & Security |
| Photos | `com.apple.preference.security?Privacy_Photos` | Privacy & Security |
| Screen Recording | `com.apple.preference.security?Privacy_ScreenCapture` | Privacy & Security |
| Accessibility | `com.apple.preference.security?Privacy_Accessibility` | Privacy & Security |
| FileVault | `com.apple.preference.security?FDE` | Privacy & Security |
| Firewall | `com.apple.preference.security?Firewall` | Privacy & Security |
| Login Password | `com.apple.preference.security?General` | Privacy & Security |
| Keyboard Shortcuts | `com.apple.preference.keyboard?Shortcuts` | Keyboard |
| Text Replacements | `com.apple.preference.keyboard?Text` | Keyboard |
| Dictation | `com.apple.preference.keyboard?Dictation` | Keyboard |
| Input Sources | `com.apple.preference.keyboard?InputSources` | Keyboard |
| Night Shift | `com.apple.preference.displays?nightShift` | Displays |
| Display Resolution | `com.apple.preference.displays?scaled` | Displays |
| Hot Corners | `com.apple.preference.exposeclassic?hotcorners` | Desktop & Dock |
| Login Items | `com.apple.preference.general?LoginItems` | General |
| AirDrop & Handoff | `com.apple.preference.general?AirDrop` | General |
| Language | `com.apple.Localization?language` | Language & Region |
| Region | `com.apple.Localization?region` | Language & Region |
| ... (hasta ~200 items) | | |

El catálogo se organiza en `BuiltinPanels.cs` agrupado por panel padre con comentarios de sección para facilitar el mantenimiento.

### Invariante de anchors
Si un anchor no existe o ha sido renombrado por Apple en una versión más nueva de macOS, `open` abre el panel padre sin navegar a la sub-sección. No hay error ni crash — degradación aceptable.

Un comentario en `BuiltinPanels.cs` anota la versión de macOS en que se verificó el catálogo:
```csharp
// Anchors verificados en macOS Ventura 13 / Sonoma 14.
// Al actualizar macOS, ejecutar tools/verify-settings-anchors.sh para re-verificar.
```

---

## Items dinámicos

Los items dinámicos reflejan el estado actual del sistema y se generan en cada llamada a `Search()`, con caché de 10 segundos.

### Items generados

| Condición del sistema | Item visible | UrlIdentifier |
|-----------------------|--------------|---------------|
| Wi-Fi conectada a "MyNetwork" | `"Wi-Fi · MyNetwork"` | `com.apple.preference.network` |
| VPN "Work VPN" activa | `"VPN · Work VPN"` | `com.apple.preference.network` |
| Wi-Fi desconectada | *(no aparece item)* | — |
| Sin VPN activa | *(no aparece item)* | — |

Los items dinámicos usan el mismo `NameMatcher.Score` que los estáticos y compiten por posición en igualdad de condiciones.

### Consultas al sistema

`PlatformProvider` gana dos métodos virtuales (devuelven valores vacíos/nulos por defecto en plataformas no-macOS):

```csharp
public virtual string? GetCurrentWifiNetworkName() => null;
public virtual IReadOnlyList<string> GetActiveVpnNames() => [];
```

`MacOsPlatformProvider` los implementa usando `Process.Start` con `RedirectStandardOutput = true` (mismo patrón que otros comandos del provider):
- **Wi-Fi**: ejecuta `networksetup -getairportnetwork en0`, parsea `"Current Wi-Fi Network: {name}"`. Devuelve `null` si está desconectada o si el comando falla.
- **VPN**: ejecuta `scutil --nc list`, filtra líneas con estado `Connected`, extrae los nombres de conexión.

Ambos comandos son rápidos (<50 ms) pero se cachean igualmente para no añadir latencia perceptible al tipado.

### Caché

`SystemSettingsSearch` mantiene:
```csharp
private IReadOnlyList<SystemSettingsPanel> _dynamicCache = [];
private DateTime _dynamicCacheTime = DateTime.MinValue;
private static readonly TimeSpan DynamicCacheTtl = TimeSpan.FromSeconds(10);
```

En `Search()`, si han pasado menos de 10s desde `_dynamicCacheTime`, se reutiliza `_dynamicCache` sin invocar el sistema.

---

## Herramienta de verificación

`tools/verify-settings-anchors.sh` — script de shell que abre cada URL del catálogo con un delay de 1s entre ellas. Permite al developer verificar visualmente que cada anchor navega a la sección correcta. No es parte del build ni de los tests automáticos.

---

## Tests (`SystemSettingsSearchTests.cs`)

| Test | Qué verifica |
|------|-------------|
| SubItem con `ParentName` genera subtítulo `"System Settings › X"` | Formato de subtítulo |
| Panel sin `ParentName` mantiene subtítulo `"System Settings"` | Compatibilidad hacia atrás |
| Wi-Fi conectada → item dinámico aparece con nombre de red | Items dinámicos |
| Wi-Fi desconectada → ningún item dinámico de Wi-Fi | Items dinámicos vacíos |
| Segunda llamada en <10s → `PlatformProvider` no se invoca de nuevo | Caché |
| Segunda llamada en >10s → `PlatformProvider` se invoca | Expiración de caché |
| `[Fact(Skip = "manual")]` lanza todas las URLs del catálogo | Verificación visual opt-in |

Los tests de items dinámicos usan una subclase de `PlatformProvider` que sobreescribe `GetCurrentWifiNetworkName` y `GetActiveVpnNames`, inyectada en el constructor de `SystemSettingsSearch` (que ya recibe `PlatformProvider platform` como parámetro).

---

## Ficheros afectados

| Fichero | Tipo de cambio |
|---------|---------------|
| `Yottacast.Core/Search/SystemSettings/SystemSettingsPanel.cs` | Añadir `ParentName` |
| `Yottacast.Core/Search/SystemSettings/SystemSettingsSearch.cs` | Subtítulo dinámico, merge dinámico, caché |
| `Yottacast.Core/Search/SystemSettings/BuiltinPanels.cs` | Expandir a ~200 items |
| `Yottacast.Core/Platform/PlatformProvider.cs` | Añadir `GetCurrentWifiNetworkName`, `GetActiveVpnNames` |
| `Yottacast.Core/Platform/MacOsPlatformProvider.cs` | Implementar ambos métodos |
| `Yottacast.Core.Tests/Search/SystemSettingsSearchTests.cs` | Nuevos tests |
| `docs/search-sources.md` | Actualizar sección 7 |
| `tools/verify-settings-anchors.sh` | Nuevo — script de verificación manual |

---

## Invariantes

- En plataformas no-macOS: `SystemSettingsSearch` no se registra en DI. Los métodos nuevos de `PlatformProvider` devuelven vacío/nulo. Sin impacto cross-platform.
- Si `EnableSystemSettings = false`: `Search()` devuelve `[]` (comportamiento existente, sin cambio).
- Si un anchor falla: abre el panel padre. No hay error visible al usuario.
- Los items dinámicos nunca bloquean el resultado: si la consulta al sistema falla, `_dynamicCache` permanece vacío y se devuelven solo los estáticos.