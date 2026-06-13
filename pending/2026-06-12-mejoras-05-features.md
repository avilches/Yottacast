# Plan 5: Features nuevas

Objetivo: ampliar Yottacast con features que encajan en la arquitectura existente, por fases de esfuerzo creciente. Cada feature indica donde encaja (interfaz, instant/deferred) para que un agente pueda implementarla sin redescubrir la arquitectura.

Antes de implementar CUALQUIER feature de este plan: usar la skill de brainstorming si esta disponible (son trabajo creativo) y confirmar con el usuario cual quiere; este documento es el catalogo priorizado, no una orden de implementarlas todas.

## Inventario actual (no reimplementar nada de esto)

- **Sources instant**: ApplicationSearch, CalculatorSearch (math.js/Jint, unidades, divisas, algebra), EmojiSearch (`:`, grid, favoritos, paste automatico), WebSearchSource (22 motores), LocalPathSearch, UrlSearch, DateSearch (11 idiomas), SystemSettingsSearch (macOS), ClipboardHistorySearch (scoring recencia/uso, Paste/Delete, visibilidad Disabled/Always/ModeOnly).
- **Sources deferred**: UserDocumentSearch (Spotlight/Windows Search/plocate, snapshots cada 200 ms), DictionarySource (SQLite local + fallback Wiktionary, 30 idiomas).
- **Infra**: plugins JSON (WebSearch y temas) con hot-reload, daemon IPC gRPC (Unix socket), historial de busquedas (Ctrl+Up/Down), LaunchHistory con decay, temas hot-swap, hotkey global SharpHook, modo sticky/Alfred, drag-and-drop al SO, auto-reparacion de navegador/terminal.

## Fase A: quick wins (horas a 1-2 dias cada uno)

### A1. Accion "abrir carpeta contenedora" en ficheros
- Anadir accion al options menu de resultados de UserDocumentSearch: revelar en Finder/Explorer.
- Encaje: accion en el ResultItemViewModel de ficheros; el lanzamiento OS-especifico va en PlatformProvider (macOS: `open -R`, Windows: `explorer /select,`).
- Leer antes: `docs/search-files.md`, `docs/result-viewmodels.md`.

### A2. Boost de frecuencia para apps lanzadas
- Aplicar el bonus de LaunchHistory (ya existe y ya se computa en el merge) de forma mas agresiva o visible para apps; revisar si hoy ya afecta al orden y ajustar pesos en AppDefaults.
- Encaje: `LaunchHistory.BonusFor` + scoring en el merge. Coordinar con el fix de half-life del Plan 1 (la formula cambia).
- Leer antes: `docs/search-scoring.md`.

### A3. Copiar todas las celdas de una conversion
- Atajo (p.ej. Cmd+Shift+C) que copia "100 F = 37.78 C" completo.
- Encaje: nueva accion en ConversionResultItemViewModel.
- Leer antes: `docs/search-calculator.md`, `docs/result-viewmodels.md`.

### A4. Emojis recientes/favoritos al abrir sin query
- Al teclear solo `:`, ya hay defaults; la propuesta es priorizar favoritos+frecuentes (ya se persisten) de forma mas prominente, o mostrar una fila de recientes en el empty state del Plan 3.
- DECISION DE PRODUCTO: preguntar al usuario que variante quiere.

### A5. Comandos del sistema (sleep, lock, empty trash, restart)
- Nueva source instant `SystemCommandSearch` con catalogo fijo de comandos.
- Encaje: IInstantSearchSource; la ejecucion OS-especifica en PlatformProvider (macOS: osascript/pmset; Windows: rundll32/shutdown). Confirmacion antes de acciones destructivas (restart/shutdown).
- Esfuerzo: 2-3 dias con tests.

## Fase B: features medianas (3-7 dias cada una)

### B1. Snippets de texto
- Source instant con snippets definidos por el usuario (abreviatura + texto); Enter pega en la app anterior (reutilizar el flujo de paste del emoji).
- Encaje: IInstantSearchSource + store JSON (AppPaths) + seccion en Settings + editor simple.
- Valor: pegar respuestas frecuentes sin salir del launcher; paridad con Alfred/Raycast snippets.

### B2. Quicklinks
- Bookmarks definidos por el usuario (titulo + URL o ruta), buscables como cualquier resultado; Enter abre con navegador configurado o app por defecto.
- Encaje: IInstantSearchSource + store JSON + seccion en Settings. Reutiliza FaviconCache para iconos.

### B3. Switcher de ventanas
- Source instant que lista ventanas abiertas y las activa con Enter.
- Encaje: IInstantSearchSource; enumeracion de ventanas en PlatformProvider (macOS: CGWindowList + AX para activar; Windows: EnumWindows/SetForegroundWindow). En macOS requiere permisos de accesibilidad: detectar y guiar al usuario.
- Riesgo: permisos y edge cases de espacios/pantallas; prototipar primero en macOS.

### B4. Busqueda de contactos
- Source instant sobre Contacts (macOS) con acciones copiar email/telefono.
- Encaje: IInstantSearchSource + P/Invoke en PlatformProvider. Requiere permiso de contactos.

### B5. Historial de tasas de divisas offline
- Mostrar fecha de ultima actualizacion de tasas cuando no hay red (ya se cachean en exchange-rates.json) y avisar de tasas obsoletas.
- Encaje: ExchangeRateService + ConversionResultItemViewModel (ya existe un indicador "rates may be outdated"; ampliarlo con la fecha).
- Esfuerzo: 1-2 dias; es el mas barato de la fase B.

## Fase C: apuestas grandes (semanas; elegir UNA, no en paralelo)

### C1. Plugins de busqueda en JavaScript (recomendada: mayor palanca)
- Permitir que un plugin aporte una search source escrita en JS, ejecutada en Jint (ya integrado para math.js).
- Encaje: PluginService carga `*.js` ademas de JSON; definir API minima del plugin (funcion search(query) que devuelve items con title/subtitle/score/action), sandbox (sin acceso a fs salvo API explicita), timeout por busqueda, y mapeo a un ResultItemViewModel generico.
- Por que primero: convierte cada feature de la Fase B en algo que la comunidad (o el propio usuario) puede hacer sin recompilar; multiplica el valor del resto del roadmap.
- Pasos sugeridos: (1) disenar la API del plugin y revisarla con el usuario; (2) runtime Jint con limites; (3) tipo de plugin en PluginService + hot-reload; (4) plugin de ejemplo; (5) docs en `docs/plugin-system.md`.

### C2. UI Swift nativa sobre el daemon IPC
- El daemon ya existe (`Yottacast.Ipc`); falta cliente SwiftUI, empaquetado y codesigning.
- REQUISITO PREVIO: Fase B del Plan 1 (bugs de IPC: race de streams, perdida ModeOnly, proto huerfano) y decidir el proto definitivo de settings.
- Pasos: completar el proto (settings nuevos de clipboard, enum de visibilidad), generacion Swift, app SwiftUI minima (busqueda + resultados + activate), paridad de hotkey global, empaquetado.

### C3. Sincronizacion de settings
- Export/import primero (barato, util); sync automatico despues si hay demanda. Backend a decidir con el usuario (iCloud Drive es el natural en macOS: copiar settings.json + plugins a un directorio observado).

## Descartes razonados (no implementar sin nueva discusion)

- Terminal integrada (`$` ejecuta shell): riesgo de seguridad y de alcance; el usuario ya tiene terminal configurado y ExecuteCommand.
- Estadisticas/analytics en Settings: valor bajo frente al coste; requiere recoleccion adicional.
- Control de musica via APIs externas (Spotify): requiere OAuth/tokens; reconsiderar como plugin JS cuando exista C1.

## Reglas para cualquier feature

- Source nueva: implementar IInstantSearchSource o IDeferredSearchSource con ciclo Start/WhenReady/Stop completo; registrarla en `App.axaml.cs` y valorar si tambien en `Yottacast.Ipc/Program.cs` (y entonces anadir el tipo a ResultMapper, sin caer en el fallback "app").
- Score por defecto en AppDefaults.cs; rutas de stores en AppPaths.cs.
- Toggle en UserSettings + Settings UI + evento SearchSettingsChanged, como las sources existentes (ver `docs/user-settings.md`).
- Tests en Yottacast.Core.Tests para la logica de la source; doc nuevo en docs/ si la feature introduce comportamiento con contrato propio, y entrada en el indice de CLAUDE.md la anade el desarrollador.
