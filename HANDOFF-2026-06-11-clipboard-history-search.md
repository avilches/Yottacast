# Handoff — Clipboard History Search

**Fecha:** 2026-06-11
**Rama:** main (trabajo directo en main, pendiente push a origin — 15 commits por encima)
**Estado:** implementación completa, 1317 tests pasando, prueba manual pendiente

---

## Qué se hizo en esta sesión

Se diseñó e implementó la feature **ClipboardHistorySearch** completa (brainstorming + spec + plan + subagent-driven development, 10 tareas).

**Artefactos generados:**
- Spec: `docs/superpowers/specs/2026-06-11-clipboard-history-search-design.md`
- Plan: `docs/superpowers/plans/2026-06-11-clipboard-history-search.md`

**Commits de esta sesión (más antiguo a más reciente):**
```
d894d8f feat: foundation para ClipboardHistory (AppPaths, AppDefaults, ActionHotkey.Delete, IClipboardMonitor)
883a787 feat: ClipboardHistoryEntry + ClipboardHistoryStore con tests
ed2cc7d fix: ClipboardHistoryStore — thread safety, save inmediato en Remove, clock injection, tests
d7c0434 feat: ClipboardHistorySearch con tests (search, scoring, actions)
529e0ea feat: UserSettings — ClipboardHistoryEnabled, MaxEntries, MaxDays (migración de EnableClipboard)
43352fb feat: MacClipboardMonitor — polling NSPasteboard via P/Invoke
957690e fix: MacClipboardMonitor — Interlocked.Exchange para CTS, cachear selectores Obj-C
d378f95 feat: WindowsClipboardMonitor — polling via Win32 OpenClipboard
afb879f feat: DI y wiring ClipboardHistoryStore, ClipboardHistorySearch, ClipboardMonitor
61967c1 feat: SettingsWindowViewModel — propiedades Clipboard (visibility, maxEntries, maxDays, hotkey capture)
22d329a feat: SettingsWindow — sección Clipboard History expandida con hotkey configurador
9660838 chore: Fix trailing whitespace in dark-default.json theme
853ccd9 fix: suscribir ClipboardHistorySearch.ResultChanged en MainWindowViewModel
```

---

## Lo que se implementó

### Archivos nuevos
| Fichero | Descripción |
|---------|-------------|
| `Yottacast.Core/Services/IClipboardMonitor.cs` | Interfaz: `TextCopied, Start(), Task Stop()` |
| `Yottacast.Core/Search/Clipboard/ClipboardHistoryEntry.cs` | Record `(Text, CopiedAt, UsageCount, LastUsedAt)` |
| `Yottacast.Core/Search/Clipboard/ClipboardHistoryStore.cs` | Store thread-safe, dedup, límites, debounce save, clock injection |
| `Yottacast.Core/Search/Clipboard/ClipboardHistorySearch.cs` | `IInstantSearchSource + ISearchModeSource`, scoring con decay, acciones Paste y Delete |
| `Yottacast/Services/MacClipboardMonitor.cs` | Polling NSPasteboard via P/Invoke, selectores cacheados, `Interlocked.Exchange` |
| `Yottacast/Services/WindowsClipboardMonitor.cs` | Polling Win32 `OpenClipboard/GetClipboardData` |
| `Yottacast.Core.Tests/Search/ClipboardHistoryStoreTests.cs` | 16 tests (Add, dedup, límites, Remove, RecordUsage, EntriesChanged, round-trip) |
| `Yottacast.Core.Tests/Search/ClipboardHistorySearchTests.cs` | 16 tests (IsActiveIn, Search, scoring, acciones, ResultChanged, formato) |

### Archivos modificados clave
- `Yottacast.Core/AppPaths.cs` — `ClipboardHistoryFile`
- `Yottacast.Core/AppDefaults.cs` — constantes clipboard history (MaxEntries, MaxDays, HalfLifeDays, MaxBonus, DebounceMs, MonitorIntervalMs)
- `Yottacast.Core/ViewModels/ActionHotkey.cs` — `ActionHotkey.Delete = new("Delete")`
- `Yottacast.Core/Services/UserSettings.cs` — `ClipboardHistoryEnabled` (reemplaza `EnableClipboard`), `ClipboardHistoryMaxEntries`, `ClipboardHistoryMaxDays`, `ParsedClipboardHotkey`
- `Yottacast.Ipc/Proto/settings.proto` — campo 9: `enable_clipboard` → `clipboard_history_enabled`
- `Yottacast.Ipc/Mapping/SettingsMapper.cs` — usa `ClipboardHistoryEnabled`
- `Yottacast/App.axaml.cs` — DI de store/search, `_ = clipboardStore.LoadAsync()`, `SetupClipboardMonitor()`
- `Yottacast/ViewModels/MainWindowViewModel.cs` — suscripción a `ClipboardHistorySearch.ResultChanged`
- `Yottacast/ViewModels/SettingsWindowViewModel.cs` — `ClipboardSearchVisibility` radio buttons, `MaxEntries/MaxDays`, hotkey capture state
- `Yottacast/Views/SettingsWindow.axaml` — sección Clipboard History completa (toggle + radio buttons + hotkey widget + NumericUpDowns)
- `Yottacast/Views/SettingsWindow.axaml.cs` — handlers `OnClipboardHotkeyPointerPressed`, bloques `IsCapturingClipboardHotkey` en `OnKeyDown/OnKeyUp`

### Comportamiento implementado
- Captura texto copiado al portapapeles cada 500ms (macOS: `NSPasteboard.changeCount`; Windows: `GetClipboardData`)
- Deduplicación: si el mismo texto se copia de nuevo, se mueve al principio con timestamp actualizado
- Score: exact match (4.0) > startsWith (3.5) > contains (3.0) + bonus de uso con decay (`log(n+1) * e^(-días/30)`)
- Acción "Paste" (Enter): copia al clipboard + cierra ventana + `PasteAfterClose=true` (simula Cmd+V como emoji)
- Acción "Delete" (Supr): elimina la entrada sin cerrar la ventana, la lista se refresca automáticamente
- Settings: toggle on/off, visibilidad (Off/Always/⌘F only), hotkey configurable, MaxEntries y MaxDays

---

## Pendiente para la próxima sesión

### 1. Push a origin (inmediato)
```bash
git push origin main
```
15 commits en main local sin pushear.

### 2. Prueba manual end-to-end
La feature NO se ha probado en la app real (subagents solo ejecutan tests). Verificar:
1. Settings → Clipboard History → Enable → "⌘F only" → configurar hotkey (ej. `⌥Space`)
2. Copiar texto en otra app → activar Yottacast con el hotkey → verificar que aparece en la lista
3. Copiar el mismo texto → verificar que hay UNA entrada (dedup) con timestamp actualizado
4. Buscar texto → verificar filtrado y que exact match aparece primero
5. Activar entrada → verificar paste automático en la app anterior
6. Borrar entrada con Supr → verificar que desaparece sin cerrar la ventana
7. Activar la misma entrada varias veces → verificar que sube en ranking de búsqueda

### 3. Posibles bugs / puntos de atención
- **Hotkey global no es reactivo**: si el usuario cambia el hotkey o visibilidad en Settings, necesita reiniciar para que el hotkey global se re-registre (igual que el hotkey principal — comportamiento conocido)
- **Score en modo All con Always**: cuando `ClipboardSearchVisibility=Always` y query vacía, las entradas tienen score `1000 - index`, lo que las sitúa por encima de prácticamente todo. Puede ser un problema visual
- **"⌘F only" en el radio button**: en Windows/Linux debería decir "Ctrl+F only" — revisar

---

## Lecciones aprendidas en esta sesión

- **`Stop()` debe devolver `Task`** en interfaces de ciclo de vida del proyecto. El revisor lo detectó en `IClipboardMonitor` — el proyecto usa `Task Stop()` consistentemente.
- **Brace style en interfaces**: el proyecto usa `public interface IFoo {` (brace en la misma línea), no Allman style.
- **`Interlocked.Exchange` para CancellationTokenSources**: el patrón `_cts?.Cancel(); _cts = new CTS()` tiene race condition. Siempre usar `Interlocked.Exchange`.
- **Clock injection en stores**: necesaria para testear evicción de entradas antiguas. Sin clock, el test `Add_EntryOlderThanMaxDays_IsDiscarded` era inútil.
- **`ResultChanged` debe suscribirse en MainWindowViewModel**: bug real detectado en revisión final. Si una `IInstantSearchSource` tiene `ResultChanged`, hay que suscribirlo con `Dispatcher.UIThread.Post(RefreshSearch)` en el ViewModel, igual que `UrlSearch.ResultChanged` y `DateSearch.ResultChanged`.
- **Selectores Obj-C deben cachearse como `static readonly`**: llamar `SelRegisterName` en cada tick del poll (500ms) es ineficiente. Cachear al nivel de clase.
- **`Remove` debe usar save inmediato, no debounced**: borrar una entrada del historial debe persistir de inmediato (`FlushAsync`) para no perder la operación en un crash.

---

## Tests actuales

1317 passed, 0 failed, 1 skipped (`Manual_AllAnchorsOpen` — skip manual/visual).

```bash
cd Yottacast.Core.Tests && dotnet test    # suite completa
cd Yottacast.Ipc.Tests && dotnet test     # IPC tests (9 tests)
```

---

## Suggested Skills

```
superpowers:verification-before-completion  ← PRIMERO: prueba manual end-to-end antes de mergear/pushear
superpowers:systematic-debugging            ← si algo falla en la prueba manual
superpowers:finishing-a-development-branch  ← para hacer push/PR cuando esté verificado
```
