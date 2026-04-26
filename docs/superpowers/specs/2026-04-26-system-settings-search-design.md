 # System Settings Search — Design Spec

**Fecha:** 2026-04-26
**Plataforma objetivo:** macOS 13+ (Ventura)
**Tipo de fuente:** Instant (`IInstantSearchSource`)

---

## Objetivo

Permitir al usuario buscar y abrir paneles de System Settings directamente desde Yottacast, sin prefijo especial, compitiendo en score con el resto de resultados.

Escribir "wifi" muestra el panel Wi-Fi. Escribir "display" muestra Displays. Al pulsar Enter se abre System Settings en el panel correspondiente.

---

## Componentes

### `Yottacast.Core/Search/SystemSettings/SystemSettingsPanel.cs`

Record inmutable con los campos de un panel:

- `Name` — nombre visible (ej: `"Wi-Fi"`)
- `UrlIdentifier` — anchor de la URL scheme (ej: `"com.apple.preference.network"`)
- `IsBuiltin` — true para paneles Apple, false para terceros

### `Yottacast.Core/Search/SystemSettings/BuiltinPanels.cs`

Array estático con los ~40 paneles conocidos de Apple en macOS 13+. Incluye al menos:

| Nombre | URL identifier |
|--------|---------------|
| Wi-Fi | `com.apple.preference.network` |
| Bluetooth | `com.apple.preferences.Bluetooth` |
| Displays | `com.apple.preference.displays` |
| Sound | `com.apple.preference.sound` |
| Notifications | `com.apple.preference.notifications` |
| Privacy & Security | `com.apple.preference.security` |
| Battery | `com.apple.preference.battery` |
| Keyboard | `com.apple.preference.keyboard` |
| Trackpad | `com.apple.preference.trackpad` |
| Mouse | `com.apple.preference.mouse` |
| General | `com.apple.preference.general` |
| Appearance | `com.apple.preference.general` |
| Accessibility | `com.apple.preference.universalaccess` |
| Screen Time | `com.apple.preference.screentime` |
| Focus | `com.apple.preference.notifications` |
| Siri & Spotlight | `com.apple.preference.speech` |
| Apple ID | `com.apple.systempreferences.AppleIDPrefPane` |
| Family Sharing | `com.apple.systempreferences.FamilySharingPrefPane` |
| Internet Accounts | `com.apple.preference.internetaccounts` |
| Passwords | `com.apple.Passwords` |
| Touch ID & Password | `com.apple.systempreferences.LocalAuthenticationPrefPane` |
| Users & Groups | `com.apple.preferences.users` |
| Printers & Scanners | `com.apple.preference.printfax` |
| Date & Time | `com.apple.preference.datetime` |
| Language & Region | `com.apple.Localization` |
| Software Update | `com.apple.preferences.softwareupdate` |
| Startup Disk | `com.apple.preference.startupdisk` |
| Time Machine | `com.apple.prefs.backup` |
| Energy Saver | `com.apple.preference.battery` |
| Lock Screen | `com.apple.preference.security` |
| Desktop & Dock | `com.apple.preference.exposeclassic` |
| Mission Control | `com.apple.preference.exposeclassic` |
| Stage Manager | `com.apple.preference.exposeclassic` |
| Wallet & Apple Pay | `com.apple.systempreferences.WalletPrefPane` |
| Game Center | `com.apple.systempreferences.GameCenterPrefPane` |
| Sharing | `com.apple.preferences.sharing` |
| Network | `com.apple.preference.network` |
| VPN | `com.apple.preference.network` |
| Storage | `com.apple.preference.storage` |
| Extensions | `com.apple.preference.extensions` |

### `Yottacast.Core/Search/SystemSettings/SystemSettingsSearch.cs`

Implementa `IInstantSearchSource`. Solo debe instanciarse en macOS (el registro DI está condicionado).

**`Start()`**:
1. Si `!settings.EnableSystemSettings` → marca ready y retorna.
2. Carga `BuiltinPanels.All` en el cache en memoria.
3. Escanea `/Library/PreferencePanes/` y `~/Library/PreferencePanes/` buscando archivos `*.prefPane`.
4. Para cada bundle encontrado, lee el `Info.plist` para obtener:
   - Nombre: `CFBundleDisplayName` → `CFBundleName` → nombre del fichero sin extensión (fallback en orden).
   - Identifier: `CFBundleIdentifier`.
   - Si el plist no existe o falla, se ignora silenciosamente.
5. Añade los paneles de terceros al cache (sin duplicar si el identifier ya existe como builtin).
6. Precarga el icono de System Settings via `AppIconCache`.
7. Marca ready.

**`Search(query, limit)`**:
1. Si `!settings.EnableSystemSettings` → devuelve `[]`.
2. Filtra el cache con `NameMatcher.Score(panel.Name, query) > 0`.
3. Ordena por score descendente, toma hasta `limit`.
4. Devuelve `ResultItemViewModel` por cada panel:
   - `Title`: `panel.Name`
   - `Subtitle`: `"System Settings"` (builtin) o `"System Settings · Preference Pane"` (tercero)
   - `Category`: `"System Settings"`
   - `Icon`: emoji fallback `"⚙️"` (el icono real viene de `IconBytes`)
   - `IconBytes`: bytes del icono de System Settings.app (de `AppIconCache`)
   - `Score`: score de `NameMatcher`
   - `OnActivate`: llama a `platform.LaunchUrl($"x-apple.systempreferences:{panel.UrlIdentifier}")`

**`WhenReady()`**: `Task` que completa cuando el startup termina.

**`Stop()`**: limpia el cache, cancela cualquier escaneo en curso.

---

## Capa de plataforma

Se añade a `PlatformProvider`:

```csharp
public virtual void LaunchUrl(string url) { }
```

`MacOsPlatformProvider` lo sobreescribe:

```csharp
public override void LaunchUrl(string url) {
    Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
}
```

No se añade en `WindowsPlatformProvider` ni `LinuxPlatformProvider` (la base no-op es suficiente; la fuente solo se registra en macOS).

---

## Registro DI (`App.axaml.cs`)

```csharp
if (OperatingSystem.IsMacOS()) {
    services.AddSingleton<SystemSettingsSearch>();
    services.AddSingleton<IInstantSearchSource>(
        sp => sp.GetRequiredService<SystemSettingsSearch>());
}
```

---

## Settings

Se añade `EnableSystemSettings` (bool, default `true`) a `UserSettings`. Se expone en la UI de Settings junto a los otros toggles de fuentes. Al cambiar, se dispara `SearchSettingsChanged` para refrescar la búsqueda activa.

---

## Scoring

`NameMatcher.Score(panel.Name, query)` — mismo algoritmo que apps, rango 0.0–1.0. Los paneles compiten en igualdad de condiciones con el resto de resultados. No hay boost ni penalización.

**Invariantes:**
- Solo se muestran paneles con score > 0.
- Las queries que empiezan por `:` (modo emoji) no activan esta fuente (comportamiento heredado del flujo de búsqueda).
- Si `EnableSystemSettings = false`, `Search()` siempre devuelve `[]`.

---

## Tests (`Yottacast.Core.Tests/Search/SystemSettingsSearchTests.cs`)

- Panel builtin matchea por nombre exacto → score 1.0, resultado presente.
- Panel builtin matchea por CamelHump (`"bt"` → `"Bluetooth"`).
- Query sin match devuelve lista vacía.
- Panel de tercero cargado desde directorio temporal con plist mínimo aparece en resultados con subtítulo `"System Settings · Preference Pane"`.
- `EnableSystemSettings = false` → `Search()` devuelve siempre `[]`.
- `OnActivate` llama a `platform.LaunchUrl` con la URL correcta.

Los tests de terceros usan un directorio temporal con un `.prefPane` sintético (plist XML mínimo) para no depender del filesystem real.

---

## Documentación

Se añade sección en `docs/search-sources.md` describiendo la nueva fuente, sus invariantes y los ficheros donde verificar el comportamiento.