# Mejoras Yottacast: overview de la revision 2026-06-12

Resultado de una revision completa de la aplicacion: bugs, rendimiento, UX, UI y oportunidades de features.
Este fichero es el indice; cada plan tiene su propio documento accionable por un agente.

## Documentos

| Plan | Fichero | Alcance | Esfuerzo estimado | Estado |
|------|---------|---------|-------------------|--------|
| 1. Estabilidad | `2026-06-12-mejoras-01-estabilidad.md` | Fixes de los bugs encontrados (8 criticos, 26 menores) | ~1 semana | DONE (tandas 1-3, review-result borrado por resuelto) |
| 2. Velocidad | `2026-06-12-mejoras-02-velocidad.md` | Optimizaciones del hot path de busqueda | 3-5 dias | CASI DONE (T1/T2/T3/T5 hechos; T6 descartado; T4 y T7 aplazados) |
| 3. UX | `2026-06-12-mejoras-03-ux.md` | Descubribilidad, navegacion, feedback | 1-2 semanas | pendiente |
| 4. UI visual | `2026-06-12-mejoras-04-ui.md` | Temas, coherencia visual, refactor de Settings | ~1 semana | EN CURSO (split de SettingsWindow: General/estilos/iconos extraidos) |
| 5. Features | `2026-06-12-mejoras-05-features.md` | Nuevas funcionalidades por fases | variable | pendiente |

El detalle exhaustivo de los bugs esta en `review-result-20260612193846.md` (en esta misma carpeta); el plan 1 lo usa como backlog.

## Hallazgos clave por area

### Bugs (detalle en review-result-20260612193846.md)

- 8 criticos: crash en Windows por P/Invoke mal declarado, `new Uri("http://")` lanza al teclear URLs, escaneo de apps fallido deja la app sin ventana, `async void` sin catch en PluginService, race en el Dictionary de iconos de plugins, perdida de `ModeOnly` persistida via IPC, campo proto de clipboard huerfano, IndexOutOfRange en Linux.
- Patron transversal: fire-and-forget sin catch en MainWindow, GlobalSearch, UserDocumentSearch, FaviconCache y servicios IPC. Los fallos son invisibles en produccion.
- 4 tests que pasan vaciamente (no testean lo que su nombre promete).
- Violaciones de CLAUDE.md: constantes/scores hardcodeados fuera de AppDefaults, codigo muerto en MacOsPlatformProvider y ThemeService.

### Velocidad

- En cada keystroke se construyen tooltips de score para todos los resultados aunque solo se ven con Alt.
- Triple iteracion para deduplicar apps vs files en RefreshResults.
- NameMatcher re-tokeniza el nombre de cada app (100-500) en cada keystroke.
- UserDocumentSearch reordena el buffer completo cada 200 ms.
- EmojiSearch reconstruye un diccionario de ~2000 entradas al teclear `:`.
- Impacto estimado de los fixes: latencia de actualizacion de UI de ~80-100 ms a ~50-60 ms en escenarios densos.

### UX

- Gap principal frente a Raycast/Alfred: descubribilidad, no funcionalidad.
- Sin empty state al abrir (ventana vacia sin contexto).
- El action panel (Tab · Options) existe pero esta enterrado.
- El historial de busquedas existe pero es invisible (Ctrl+flecha arriba sin ningun hint).
- Footer trunca hints con ellipsis cuando hay muchas acciones.
- Settings no navegable por teclado (botones con Focusable=False).
- Flechas izquierda/derecha en grids mueven tambien el caret del SearchBox (es bug, va en plan 1).

### UI visual

- Colores hardcodeados en SettingsWindow (`#FF3B30` en captura de hotkey) desincronizados de los temas.
- Temas JSON heterogeneos: secciones `editor`/`menu` faltan en algunos y caen a fallbacks.
- Cambio de tema en caliente sin transicion (brusco).
- SettingsWindow.axaml tiene 1683 lineas; candidato a dividir en UserControls.

### Features

- Inventario actual: 9 sources instant + 3 deferred, sistema de plugins (WebSearch y temas JSON), daemon IPC gRPC funcional, historial de busquedas y de clipboard, temas hot-swap, hotkey global.
- Mayor palanca detectada: plugins de busqueda en JavaScript (Jint ya esta integrado, PluginService ya existe; falta el tipo de plugin "search source").
- Quick wins: abrir carpeta contenedora, boost de frecuencia para apps (LaunchHistory ya existe), copiar todas las celdas de una conversion, emojis recientes sin query.

## Secuencia recomendada

1. Plan 1 (estabilidad) primero: el resto construye encima.
2. Plan 2 (velocidad): cambios pequenos y medibles.
3. Plan 3 (UX): mayor impacto percibido por el usuario.
4. Plan 4 (UI) y quick wins del plan 5 en paralelo.
5. Decidir la apuesta grande del plan 5 (plugins JS o UI Swift, no ambas a la vez).

## Reglas comunes a todos los planes

- Antes de tocar codigo de un area, leer los docs/ correspondientes (lista en CLAUDE.md). Es obligatorio.
- Al modificar funcionalidad cubierta por tests, actualizar los tests y ejecutar `cd Yottacast.Core.Tests && dotnet test` (y `Yottacast.Ipc.Tests` si aplica).
- Toda constante nueva va a `AppDefaults.cs`; toda ruta a `AppPaths.cs`.
- Codigo OS-especifico: en `AppHandler` (UI) o `PlatformProvider` (Core), nunca en Views/ViewModels.
- Cambios de color/estilo: solo en el JSON del tema que el usuario indique; si no lo indica, preguntar. Nuevos tokens de tema requieren aprobacion del usuario.
- Al cambiar comportamiento documentado en docs/, actualizar el doc correspondiente (que y por que, no como; con bloque "Verificar en:").
