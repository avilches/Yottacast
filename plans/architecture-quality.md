# Plan de arquitectura y calidad — Yottacast

Análisis basado en lectura completa del código fuente. Las referencias son fichero:línea tal
como aparecen en la rama `main` en la fecha del análisis.

---

## 1. DI / IoC

### 1.1 `AppHandler` — singleton estático no inyectable

**Problema**
`AppHandler.Instance` es un singleton estático creado mediante `OperatingSystem.IsMacOS()`
inline en `AppHandler.cs:8-11`. Esto implica que cualquier código que lo consuma
(`MainWindow.axaml.cs`, `SettingsWindow.axaml.cs`, `App.axaml.cs`) toma la dependencia sin
declaración, lo que impide sustituirlo en tests y viola la regla arquitectónica de que el
código OS-específico UI debe vivir en `AppHandler` y sus subclases, pero sin acoplar el resto.

**Solución propuesta**
Registrar `AppHandler` en el contenedor DI como singleton concreto e inyectarlo donde se
necesite. El `BuildServices()` ya hace algo análogo con `PlatformProvider`. Tanto
`MainWindow` como `SettingsWindow` son construidas a mano hoy (no con DI), por lo que en
paralelo se puede crear una interfaz `IAppHandler` para poder sustituirlo en tests de
integración futuros.

```
services.AddSingleton<IAppHandler>(_ => AppHandler.Instance);
```

**Riesgo de regresión**: bajo — el cambio es aditivo. La instancia singleton sigue siendo la
misma; solo cambia el punto de acceso.
**Complejidad**: baja.

---

### 1.2 `MainWindow` y `SettingsWindow` construidas fuera del contenedor

**Problema**
`App.axaml.cs:51-52` y `App.axaml.cs:92` instancian las ventanas con `new`, pasando el
`DataContext` a mano. Esto funciona pero fuerza a `App` a conocer la topología interna de
ViewModels y ventanas. Si una ventana necesita una dependencia nueva, hay que editar `App`.

**Solución propuesta**
Registrar `MainWindow` y `SettingsWindow` en DI y resolverlas con
`sp.GetRequiredService<MainWindow>()`. Como Avalonia no tiene ViewLocator con DI por defecto,
el pattern estándar es registrar las vistas y resolver de forma explícita desde `App`. Esto
reduciría la responsabilidad de `App.OnFrameworkInitializationCompleted` y alinearia con el
`ViewLocator.cs` ya existente.

**Riesgo de regresión**: bajo si las vistas no tienen dependencias adicionales hoy. Hay que
verificar que `ViewLocator` no entre en conflicto.
**Complejidad**: baja-media.

---

### 1.3 Log path con `OperatingSystem.IsMacOS()` en `BuildServices()`

**Problema**
`App.axaml.cs:149-155` contiene lógica OS-específica (path de logs). Según la regla
arquitectónica, la lógica OS-específica que no depende de UI debe vivir en
`PlatformProvider`. Actualmente `PlatformProvider` ya expone `DefaultSearchFolders()` y
`DefaultAppDirectories()` — añadir `DefaultLogDirectory()` seguiría el mismo patrón.

**Solución propuesta**
Añadir `public virtual string DefaultLogDirectory()` a `PlatformProvider` con
implementaciones en `MacOsPlatformProvider`, `WindowsPlatformProvider` y
`LinuxPlatformProvider`. `App.BuildServices()` llama `platform.DefaultLogDirectory()` en vez
de hacer el `if (IsMacOS)` inline.

**Riesgo de regresión**: ninguno — es refactor puro sin cambio de comportamiento.
**Complejidad**: baja.

---

### 1.4 `_services` expuesto implícitamente a `OpenSettings()`

**Problema**
`App.axaml.cs:30` declara `_services = null!`. El campo es `null!` hasta que
`OnFrameworkInitializationCompleted` lo asigna. Si `OpenSettings()` (línea 82) se invocara
antes de ese momento —por ejemplo mediante una hotkey global que llegue muy pronto— causaría
un `NullReferenceException` sin mensaje claro.

**Solución propuesta**
Guardar `_services` en una variable local nullable con guard en `OpenSettings`:

```csharp
private IServiceProvider? _services;

public async void OpenSettings() {
    if (_services is null) return;
    ...
}
```

**Riesgo de regresión**: ninguno — es una guarda defensiva.
**Complejidad**: baja.

---

## 2. Testabilidad

### 2.1 `MainWindowViewModel` no es testeable

**Problema**
`MainWindowViewModel` (`Yottacast/ViewModels/MainWindowViewModel.cs`) depende de
`GlobalSearch`, `UserSettings` y `BrowserDiscovery`. El constructor ya permite inyección, pero
no existe ningún test de él. El método `SearchAsync` contiene lógica no trivial: dos fases de
búsqueda con debounce de 250 ms, fusión de snapshots, selección automática de la calculadora,
conservación de `SelectedResult` cuando el usuario ha navegado. Todo eso permanece sin cobertura.

**Solución propuesta**
Crear `Yottacast.Core.Tests` (o un nuevo proyecto `Yottacast.Tests`) con pruebas del
ViewModel usando dobles de `GlobalSearch`. El obstáculo principal es que `GlobalSearch` no
implementa una interfaz. Extraer `IGlobalSearch` con `SearchInstantAsync` /
`SearchDeferredAsync` / `Start` / `Stop` / `WhenReady` permite inyectar un fake.

Tests mínimos a añadir:
- Texto vacío → `Results` vacío, `IsSearching = false`.
- Resultado calculadora → `SelectedResult` es el ítem calculadora aunque no sea el primero.
- Navegación manual (`NotifyUserNavigated`) → la selección no se mueve al calcular.
- Cancelación al cambiar texto antes del debounce → no aparece el resultado de la búsqueda antigua.
- `MakeGoogleItem` → la URL generada es correcta para queries con caracteres especiales.

**Riesgo de regresión**: ninguno; es añadir tests, no cambiar código.
**Complejidad**: media (requiere interfaz `IGlobalSearch`).

---

### 2.2 `SettingsWindowViewModel` no tiene tests

**Problema**
`SettingsWindowViewModel` contiene lógica relevante: `ProcessKeyCapture` traduce teclas
Avalonia a `HotkeyConfig`, valida modificadores solos, persiste mediante `settings.Save()`.
Hay riesgo de regresión silenciosa porque ningún test lo cubre.

**Solución propuesta**
El ViewModel ya es testeable (sus dependencias son inyectadas). Añadir tests para:
- `ProcessKeyCapture` con una tecla + modificador → `settings.Hotkey` actualizado.
- `ProcessKeyCapture` con solo `Key.LeftAlt` → nada cambia.
- `CancelHotkeyCapture` → `HotkeyText` revierte al valor guardado.
- `OnSelectedBrowserChanged` → `settings.Browser` actualizado y `Save()` llamado.
- `OnSelectedThemeChanged` → `themeService.Apply()` llamado con el ID correcto.

El obstáculo es `ThemeService`, que depende de Avalonia para leer ficheros de tema (lectura de
`Themes/*.json`). Hace falta extraer una interfaz `IThemeService` con solo `Apply(string id)`
y `AvailableThemes()`.

**Riesgo de regresión**: ninguno.
**Complejidad**: baja-media.

---

### 2.3 `EmojiSearch` no tiene tests

**Problema**
`EmojiSearch` carga un recurso embebido (`emojis.json`) mediante un `Lazy<T>` estático. La
lógica de scoring (`MatchScore`) y los defaults están sin cobertura.

**Solución propuesta**
`EmojiSearch` es instantáneo e in-memory, por lo que es directamente testeable sin dobles.
Tests a añadir:
- Query sin `:` → sin resultados.
- Query solo `:` → 6 emojis por defecto.
- Query `:smile` → resultados ordenados por score, el exacto primero.
- `PasteAfterActivate = true` en todos los resultados.
- `OnActivate` llama a `clipboard.CopyText`.

**Riesgo de regresión**: ninguno.
**Complejidad**: baja.

---

### 2.4 `MathJsEngine.Evaluate` descarta todas las excepciones

**Problema**
`MathJsEngine.cs:49` tiene `catch { return null; }`. Esto oculta cualquier excepción de Jint,
incluyendo errores de inicialización tardía o corrupción del estado del motor, haciendo que el
síntoma observable sea "la calculadora no funciona" sin ningún rastro en el log.

**Solución propuesta**
Loguear la excepción con nivel `Debug` antes de retornar null, e inyectar `ILogger<MathJsEngine>`
(actualmente el constructor no acepta logger). Esto permite diagnosticar sin cambiar el
comportamiento observable.

**Riesgo de regresión**: ninguno — es añadir logging.
**Complejidad**: baja.

---

### 2.5 `FakePlatformProvider` requiere implementar ~14 métodos abstractos

**Problema**
Cualquier test que necesite un `PlatformProvider` debe implementar todos los métodos
abstractos, incluyendo browser, terminal, app scan, file search e iconos. `FakePlatformProvider`
en `Yottacast.Core.Tests/Fakes/` alivia esto, pero `UserSettingsTests.cs` y `ApplicationSearchTests.cs`
definen cada uno su propia clase `MinimalPlatform` / `PlatformWithApps` con los mismos
boilerplate, lo que provoca duplicación.

**Solución propuesta**
Consolidar en `FakePlatformProvider` usando propiedades virtuales configurables o el patrón
builder. `FakePlatformProvider` ya existe como clase base — extenderla con setters de `Func`
delegates para los callbacks más usados (`ScanAppsAsync`, `SearchFilesAsync`, browser paths)
elimina la necesidad de subclases anónimas en cada test file.

**Riesgo de regresión**: bajo; afecta solo a los tests.
**Complejidad**: baja.

---

## 3. Separación de concerns

### 3.1 `MainWindowViewModel` mezcla lógica de dominio con lógica de presentación

**Problema**
`MainWindowViewModel.cs:131-146` construye inline el `ResultItemViewModel` para Google Search,
incluyendo la URL de búsqueda hardcoded (`https://www.google.com/search?q=`), el icono emoji
y el score. La lógica "abrir URL en navegador" y la cadena de Google son detalles de dominio
que quedarían mejor en una clase `WebSearchSource : ISearchSource`.

Ventajas de separar:
- La URL de búsqueda de Google se puede testear independientemente.
- Sustituir Google por DuckDuckGo no toca el ViewModel.
- El ViewModel deja de conocer `BrowserDiscovery` directamente.

**Solución propuesta**
Crear `WebSearchSource : ISearchSource` (IsInstant = true) que reciba `UserSettings` y
`BrowserDiscovery`, y genere el ítem de búsqueda web. Registrar como `ISearchSource` en DI.
`MainWindowViewModel` elimina `BrowserDiscovery` de sus dependencias.

**Riesgo de regresión**: medio — hay que asegurarse de que el ítem de Google sigue apareciendo
inmediatamente sin debounce, lo que requiere que `WebSearchSource.IsInstant = true`.
**Complejidad**: baja.

---

### 3.2 `ClipboardService` es un singleton mutable con estado tardío

**Problema**
`ClipboardService.cs` expone `Initialize(Action<string> copy)` que debe ser llamado una vez
en arranque (`App.axaml.cs:58-62`). Si `CopyText` se llama antes de `Initialize` (por
ejemplo durante los tests de `CalculatorSearch` o `EmojiSearch`), silencia el error (el
delegate es null y simplemente no hace nada). No hay warning ni excepción.

**Solución propuesta**
Dos opciones según la gravedad que se quiera:
a) Loguear o lanzar `InvalidOperationException` si `_copy` es null cuando se llama `CopyText`.
b) Definir una interfaz `IClipboardService` con `CopyText(string)` e inyectar una
   `NullClipboardService` en tests, evitando el problema de raíz.

La opción (b) también permite tests de `EmojiSearch` y `CalculatorSearch` que aserten que
`CopyText` fue llamado con el valor correcto.

**Riesgo de regresión**: ninguno si se elige la opción (a); bajo si se elige (b).
**Complejidad**: baja.

---

### 3.3 `App.RegisterGlobalHotKey` tiene lógica de negocio incrustada

**Problema**
`App.axaml.cs:186-230` contiene toda la lógica de parseo de la hotkey, comprobación de
modificadores y decisión show/hide. Esta lógica es difícil de testear porque requiere un
`IGlobalHook` de SharpHook real.

**Solución propuesta**
Extraer a `HotkeyService` (o similar) que reciba `UserSettings`, el `IGlobalHook` de SharpHook
y un callback `Action<bool> onToggle`. `App` solo lo construye y llama `Start()`. La lógica
de comparación de modificadores queda en `HotkeyService` y es testeable sin Avalonia.

**Riesgo de regresión**: bajo — la lógica no cambia, solo se mueve.
**Complejidad**: media.

---

## 4. Error handling

### 4.1 `ApplicationSearch.ScanAndWatchAsync` no captura excepciones

**Problema**
`ApplicationSearch.cs:92-99` lanza `ScanAndWatchAsync()` como fire-and-forget (`_ = ScanAndWatchAsync()`).
Si `platform.ScanAppsAsync` lanza una excepción no controlada (por ejemplo, un directorio no
accesible, o un error en el P/Invoke de Spotlight), la excepción silencia la task. La app
arrancaría sin apps en el caché y sin ningún mensaje de error.

**Solución propuesta**
Envolver `ScanAndWatchAsync` en try/catch con log de error y, si falla, llamar igualmente a
`_readyTcs.TrySetResult()` para que `WhenReady()` no bloquee indefinidamente.

```csharp
private async Task ScanAndWatchAsync() {
    try {
        await platform.ScanAppsAsync(AddApp, ...);
        ...
    } catch (Exception ex) when (ex is not OperationCanceledException) {
        logger.LogError(ex, "App scan failed");
    } finally {
        _readyTcs.TrySetResult();
    }
}
```

**Riesgo de regresión**: ninguno — el comportamiento observable en el caso feliz no cambia.
**Complejidad**: baja.

---

### 4.2 `UserDocumentSearch` puede dejar `channel.Writer` sin completar

**Problema**
`UserDocumentSearch.cs:41-102` lanza un `Task.Run` que finalmente escribe
`channel.Writer.TryComplete()` en la línea 101. Si ocurre cualquier excepción no capturada
dentro del `Task.Run` antes de ese punto (por ejemplo, en el callback de resultado), el canal
nunca se completa y `ReadAllAsync` bloquea indefinidamente.

El `catch (OperationCanceledException)` de la línea 94 captura solo cancelación. Otras
excepciones (`IOException`, `NullReferenceException` en el callback) escapan del try/catch
y hacen que `channel.Writer.TryComplete()` nunca se ejecute.

**Solución propuesta**
Convertir el try/catch en try/catch/finally con `channel.Writer.TryComplete(ex)` en el finally:

```csharp
} catch (Exception ex) when (ex is not OperationCanceledException) {
    logger.LogError(ex, "DocSearch error query={Query}", query);
} finally {
    cts.Dispose();
    channel.Writer.TryWrite(buffer.OrderByDescending(x => x.Score).Take(limit).ToList());
    channel.Writer.TryComplete();
}
```

**Riesgo de regresión**: ninguno — el finally garantiza lo mismo que el código actual en el
caso feliz, y evita el deadlock en el caso de error.
**Complejidad**: baja.

---

### 4.3 `MacAppHandler.OnHide` puede causar memory leak si `_previousApp` no se libera

**Problema**
`MacAppHandler.cs:35-41`: si `OnShow()` se llama varias veces seguidas sin `OnHide()` de por
medio (por ejemplo, si el hotkey se activa dos veces), la línea 28 libera el puntero anterior
antes de sobrescribirlo — eso es correcto. Sin embargo, si `OnHide()` nunca llega a llamarse
(por ejemplo si la app termina mientras está visible), `_previousApp` queda retenido. No hay
`IDisposable` en `MacAppHandler`.

**Solución propuesta**
Hacer `MacAppHandler : IDisposable` y liberar `_previousApp` en `Dispose()`. Registrar
`AppHandler.Instance` en el ciclo de vida de la app para que `Dispose()` se llame en `Exit`.

**Riesgo de regresión**: ninguno — es añadir cleanup.
**Complejidad**: baja.

---

### 4.4 `MathJsEngine`: fallo de inicialización no propagado correctamente

**Problema**
`MathJsEngine.cs:55-57` en `Dispose()` hace `_initTask.Wait()` dentro de try/catch vacío.
Si `Initialize()` lanzó una excepción (por ejemplo el recurso embebido no existe), `_initTask`
está en estado `Faulted`. `CalculatorSearch.WhenReady()` devuelve ese task faulted, lo que
puede causar que `GlobalSearch.SearchSourcesAsync` no emita la excepción (ya que la captura
con `catch (OperationCanceledException)` en `GlobalSearch.cs:45` no la atraparía — bien —
pero el task de init faulted sí podría propagarse en contextos de `await` externo de forma
inesperada).

**Solución propuesta**
En `Initialize()`, si el recurso embebido no existe, loguear el error y completar `_initTask`
como cancelado o como un resultado que indique "no disponible", en vez de lanzar la excepción
sin atrapar. Alternativamente, almacenar el error y que `Evaluate()` lo logúe en la primera
llamada.

**Riesgo de regresión**: bajo.
**Complejidad**: baja.

---

## 5. Configurabilidad

### 5.1 Constantes hardcoded en el código de búsqueda

Las siguientes constantes están inline en el código sin posibilidad de configuración:

| Constante | Fichero | Valor | Descripción |
|-----------|---------|-------|-------------|
| `SearchSourceLimit` | `MainWindowViewModel.cs:47` | 10 | Resultados por fuente |
| `SnapshotIntervalMs` | `UserDocumentSearch.cs:35` | 200 ms | Frecuencia de snapshots |
| `timeoutMs` (param) | `UserDocumentSearch.cs:20` | 20 000 ms | Timeout de búsqueda de ficheros |
| Debounce | `MainWindowViewModel.cs:83` | 250 ms | Espera antes de búsqueda deferred |
| `Task.Delay(150)` | `MacAppHandler.cs:48`, `WindowsAppHandler.cs:14` | 150 ms | Espera antes del paste simulado |
| Defaults emojis | `EmojiSearch.cs:20` | 6 emojis | Emojis mostrados sin query |

Ninguna de estas constantes es urgente de externalizar, pero el debounce (250 ms) es
perceptible en hardware lento y podría merecer estar en `UserSettings` (o al menos ser
configurable por la plataforma).

**Solución propuesta** (priorizando el debounce):
Añadir `public int SearchDebounceMs { get; set; } = 250;` en `UserSettings`.
`MainWindowViewModel` lo lee de `settings.SearchDebounceMs`. Las demás constantes pueden
permanecer hardcoded.

**Riesgo de regresión**: bajo.
**Complejidad**: baja.

---

### 5.2 `EmojiSearch` usa una lista fija de emojis por defecto

**Problema**
`EmojiSearch.cs:20` hardcodea 6 emojis de uso frecuente. Un usuario podría querer sus propios
favoritos. Actualmente no hay forma de configurarlo.

**Solución propuesta**
Añadir `List<string> FavoriteEmojis` a `UserSettings` (con los 6 actuales como default). No
es urgente pero completaría la personalización de la sesión de usuario.

**Riesgo de regresión**: ninguno.
**Complejidad**: baja.

---

### 5.3 URL de búsqueda web hardcoded en `MainWindowViewModel`

**Problema**
`MainWindowViewModel.cs:142`: `https://www.google.com/search?q=` no es configurable. Usuarios
que prefieran DuckDuckGo o Brave Search no tienen opción.

**Solución propuesta**
Extraer a `WebSearchSource : ISearchSource` (ver §3.1). La URL y el score del ítem de Google
quedarían en ese servicio y en `UserSettings`, no en el ViewModel. Ver `plans/scoring.md §1`
para los rangos de score recomendados al normalizar.

**Riesgo de regresión**: ninguno.
**Complejidad**: baja.

---

## 6. Extensibilidad

### 6.1 No hay mecanismo de plugins externos

**Problema**
Toda la lista de `ISearchSource` está hardcoded en `App.BuildServices()`. Añadir una nueva
fuente de búsqueda requiere modificar `Yottacast/App.axaml.cs` y hacer rebuild.

**Solución propuesta** (largo plazo)
Definir un directorio de plugins (p.ej. `~/.config/yottacast/plugins/`). Cada plugin es un
assembly .NET que exporta implementaciones de `ISearchSource` mediante
`IServiceCollection.AddYottacastPlugin(IServiceCollection)`. El host carga los assemblies con
`AssemblyLoadContext` aislado. Esto es arquitecturalmente significativo — conviene decidir si
es un objetivo del producto antes de invertir tiempo.

Una versión más ligera (y mucho más simple de implementar) es permitir plugins mediante scripts
JavaScript ejecutados en Jint, ya que el motor JS ya está presente en la solución.

**Riesgo de regresión**: alto si se hace de forma apresurada (carga de assemblies externos
puede desestabilizar el proceso).
**Complejidad**: alta (assemblies) / media (scripts JS).

---

### 6.2 `ISearchSource` no tiene metadatos de capacidades

**Problema**
La única capacidad que declara `ISearchSource` es `IsInstant`. Si en el futuro se quieren
fuentes que se activen solo para ciertos prefijos (como EmojiSearch con `:`, pero de forma
declarativa), el filtrado tendría que hacerse en `GlobalSearch` o en el ViewModel con lógica
especial.

**Solución propuesta**
Ampliar la interfaz con una propiedad opcional de activación:

```csharp
/// <summary>Si no es null, la fuente solo recibe queries que empiecen por este prefijo.</summary>
string? QueryPrefix { get; }
```

`GlobalSearch` filtra antes de invocar. `EmojiSearch` declara `QueryPrefix = ":"` y elimina
el `if (!query.StartsWith(':'))` de su `SearchAsync`. Esto hace el sistema más declarativo
y facilita que fuentes externas (plugins) declaren sus prefijos.

**Riesgo de regresión**: bajo — es añadir un miembro con valor default nulo en una interfaz.
**Complejidad**: baja.

---

### 6.3 `ResultItemViewModel` usa `Action?` para `OnActivate`

**Problema**
`ResultItemViewModel.cs:11`: `OnActivate` es `Action?`. Esto es simple y funciona, pero tiene
dos limitaciones:
- No hay forma de que la acción devuelva un resultado (por ejemplo, un `Task` si la acción es
  asíncrona — actualmente todas son síncronas, pero abrir una URL podría no serlo en el futuro).
- `PasteAfterActivate` es un flag booleano que implica una acción post-activación específica;
  si hubiera más comportamientos post-activación (p.ej. abrir SettingsWindow después de
  activar), habría que añadir más flags.

**Solución propuesta** (conservadora)
Cambiar `Action?` a `Func<Task>?` y ajustar las llamadas en `MainWindow.axaml.cs:75-83`.
Esto permite acciones asíncronas sin cambiar la interfaz pública de `ResultItemViewModel`.

**Riesgo de regresión**: medio — hay varios sitios que crean `ResultItemViewModel` con lambdas
síncronas; habría que envolverlas. La búsqueda de todos los usos es sencilla con grep.
**Complejidad**: baja.

---

## 7. Cobertura de tests

### Cobertura actual (observada)

| Componente | Tests |
|------------|-------|
| `NameMatcher` | Sí, completo (`NameMatcherTests.cs`) |
| `ApplicationSearch` | Sí, completo (`ApplicationSearchTests.cs`) |
| `UserDocumentSearch` | Sí, cobertura funcional básica |
| `GlobalSearch` | Sí, muy completo (`GlobalSearchTests.cs`) |
| `UserSettings` (Load/Save) | Sí, muy completo |
| `BrowserDiscovery` / `TerminalDiscovery` | Sí (`BrowserTerminalDiscoveryTests.cs`) |
| `CalculatorSearch` | Sí (`CalculatorSearchTests.cs`) |
| `HotkeyConfig.Parse` | Sí (`HotkeyConfigTests.cs`) |
| `StandardCommandRunner` | Sí |
| `EmojiSearch` | **No** |
| `MathJsEngine` | Solo via `CalculatorSearch` |
| `MainWindowViewModel` | **No** |
| `SettingsWindowViewModel` | **No** |
| `ClipboardService` | **No** |
| `ThemeService` | **No** |
| `AppHandler` (cualquier subclase) | **No** (no testeable sin P/Invoke) |

### 7.1 Tests a añadir con alta prioridad

**`EmojiSearch`** — baja complejidad, cero dependencias externas:
- `SearchAsync_NoColon_ReturnsEmpty`
- `SearchAsync_OnlyColon_ReturnsDefaultEmojis`
- `SearchAsync_WithTerm_FiltersByNameAndKeyword`
- `SearchAsync_Results_HavePasteAfterActivateTrue`
- `SearchAsync_OnActivate_CallsClipboard`

**`MainWindowViewModel`** — media complejidad, requiere `IGlobalSearch`:
- `OnSearchTextChanged_EmptyText_ClearsResults`
- `OnSearchTextChanged_ValidQuery_ShowsGoogleItem`
- `SearchAsync_InstantResults_AppearsBeforeDeferred`
- `SearchAsync_CalculatorResult_SelectedAutomatically`
- `SearchAsync_UserNavigated_SelectionPreserved`
- `SearchAsync_TextChangedBeforeDebounce_PreviousResultsNotShown`

**`SettingsWindowViewModel`** — baja complejidad, todas las dependencias son inyectables:
- `ProcessKeyCapture_ValidKey_UpdatesSettingsAndHotkeyText`
- `ProcessKeyCapture_ModifierOnly_NoChange`
- `ProcessKeyCapture_Escape_CancelsCapture`
- `OnSelectedBrowserChanged_SavesSettings`
- `OnSelectedThemeChanged_AppliesTheme`

**`ClipboardService`** — trivial:
- `CopyText_BeforeInitialize_DoesNotThrow`
- `CopyText_AfterInitialize_CallsDelegate`

### 7.2 Tests de integración recomendados

- `GlobalSearch` + `ApplicationSearch` + `UserDocumentSearch` juntos, con `FakePlatformProvider`,
  verificando que la pipeline completa (Start → debounce → resultados mezclados) funciona.
- `UserSettings.Load` → `ApplicationSearch.Start` → `BrowserDiscovery.Discover` integrado,
  verificando que el flow de arranque es correcto.

---

## Tabla resumen de prioridades

| # | Propuesta | Sección | Complejidad | Riesgo | Prioridad |
|---|-----------|---------|-------------|--------|-----------|
| 1 | `try/catch/finally` en `UserDocumentSearch` para no dejar canal colgado | 4.2 | Baja | Ninguno | Alta |
| 2 | `try/catch` en `ApplicationSearch.ScanAndWatchAsync` + `_readyTcs` en finally | 4.1 | Baja | Ninguno | Alta |
| 3 | Tests para `EmojiSearch` | 7.1 | Baja | Ninguno | Alta |
| 4 | Tests para `SettingsWindowViewModel` | 7.1 | Baja | Ninguno | Alta |
| 5 | Extraer `IClipboardService` + tests | 3.2 / 7.1 | Baja | Ninguno | Media |
| 6 | `DefaultLogDirectory()` en `PlatformProvider` | 1.3 | Baja | Ninguno | Media |
| 7 | Logger en `MathJsEngine.Evaluate` | 2.4 | Baja | Ninguno | Media |
| 8 | Guard nula `_services` en `OpenSettings` | 1.4 | Baja | Ninguno | Media |
| 9 | Extraer `WebSearchSource` desde `MainWindowViewModel` | 3.1 | Baja | Baja | Media |
| 10 | Tests para `MainWindowViewModel` + `IGlobalSearch` | 2.1 | Media | Ninguno | Media |
| 11 | `ISearchSource.QueryPrefix` declarativo | 6.2 | Baja | Baja | Baja |
| 12 | `IAppHandler` en DI | 1.1 | Baja | Baja | Baja |
| 13 | `Func<Task>?` en `ResultItemViewModel.OnActivate` | 6.3 | Baja | Media | Baja |
| 14 | `SearchDebounceMs` en `UserSettings` | 5.1 | Baja | Baja | Baja |
| 15 | `MacAppHandler : IDisposable` | 4.3 | Baja | Ninguno | Baja |
| 16 | Extracción de `HotkeyService` | 3.3 | Media | Baja | Baja |
| 17 | Mecanismo de plugins externos | 6.1 | Alta | Alta | Largo plazo |
