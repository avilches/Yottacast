# Búsqueda de fechas (DateSearch)

**Fecha**: 2026-05-09

## Objetivo

Añadir un `IInstantSearchSource` que detecta fechas y rangos de fechas expresados en lenguaje natural dentro de la query, y los presenta como un resultado navegable con múltiples formatos copiables.

El usuario puede escribir "3 de mayo", "next Monday" o "del 1 al 5 de junio" y obtener inmediatamente el valor formateado en ISO y en texto largo, sin necesidad de abrir ninguna aplicación externa.

---

## 1. Activación

DateSearch se comporta como la calculadora: no requiere prefijo. Se activa con cualquier texto que el reconocedor de fechas interprete como una fecha o rango de fechas válido. Si la query no contiene ninguna fecha reconocible, la fuente devuelve `[]` sin efecto visible.

La detección usa **Microsoft.Recognizers.Text.DateTime**, que admite expresiones absolutas ("3 de mayo de 2025"), relativas ("mañana", "next week") y rangos ("del lunes al viernes"). El reconocedor devuelve tipos `datetimeV2.date`, `datetimeV2.datetime`, `datetimeV2.daterange` y `datetimeV2.datetimerange`; cualquier otro tipo se ignora.

Para evitar falsos positivos, la fuente filtra dos clases de entrada antes y después del reconocimiento:

- **Entradas puramente numéricas**: una query sin ninguna letra (p. ej. "134.2", "12.5", "2025") se descarta sin lanzar el reconocedor, porque es entrada de calculadora/número, no una fecha. La única forma todo-dígitos que sí se acepta es una fecha ISO completa `yyyy-MM-dd`.
- **Nombre de mes o día de la semana suelto**: un mes solo ("dec", "diciembre") o un día de la semana solo ("monday", "lunes") produce un rango/fecha indefinido que el usuario no llegó a especificar; se suprime. Las formas cualificadas ("3 de mayo", "next monday", "diciembre 2025") conservan día/año concretos en su `timex` y sí se muestran. La detección se hace sobre el `timex` del reconocedor: se suprime cuando es `XXXX-MM` (mes indefinido) o `XXXX-WXX-N` (día de semana indefinido).

---

## 2. Resultado: fecha simple

Cuando se reconoce una fecha puntual, la fuente produce **un único resultado** con dos celdas navegables:

| Celda | Contenido |
|---|---|
| 0 (ISO) | `yyyy-MM-dd` — formato estándar, apto para bases de datos y APIs |
| 1 (Largo) | `d de MMMM de yyyy (dddd)` en español — texto completo con día de la semana |

El subtítulo expresa la distancia temporal respecto a hoy:

| Distancia | Subtítulo |
|---|---|
| 0 días | `hoy` |
| +1 día | `mañana` |
| -1 día | `ayer` |
| N > 1 días | `dentro de N días` |
| N < -1 días | `hace N días` |

El icono es 📅 y la categoría `Date`.

---

## 3. Resultado: rango de fechas

Cuando se reconoce un rango (inicio + fin), la fuente produce **un único resultado** con cuatro celdas navegables:

| Celda | Contenido |
|---|---|
| 0 (ISO inicio) | `yyyy-MM-dd` de la fecha de inicio |
| 1 (ISO fin) | `yyyy-MM-dd` de la fecha de fin |
| 2 (ISO rango) | `yyyy-MM-dd/yyyy-MM-dd` — formato de intervalo ISO 8601 |
| 3 (Original) | El texto reconocido tal como lo escribió el usuario |

El subtítulo muestra la duración del rango en días. El reconocedor reporta el día final de forma **inclusiva** para rangos explícitos "de X a Y" (`timex` en forma de tupla, p. ej. `(...,...,P4D)`) pero **exclusiva** —primer día del periodo siguiente— para rangos de periodo completo (mes/año, p. ej. `2025-12`). Por eso la duración suma el día final solo en el primer caso: "del 1 al 5 de junio" son 5 días, y "diciembre 2025" son 31 días (no 32).

El icono es 📅 y la categoría `Date Range`.

---

## 4. Navegación y copia

El resultado usa el mismo mecanismo de celdas navegables que el conversor de unidades:

- **←/→**: desplazan la celda seleccionada circularmente (la tecla es consumida por el item y no mueve el cursor de texto).
- **Enter** o **Cmd+C / Ctrl+C**: copia al portapapeles el contenido de la celda actualmente seleccionada.
- Al activar, `PasteAfterActivate = false`: la ventana no se oculta automáticamente ni simula un paste.
- El mensaje de feedback tras copiar es `"Copiado"`.

La celda inicial al aparecer el resultado es siempre la celda 0 (ISO simple o ISO inicio del rango).

---

## 5. Multi-idioma

El reconocedor se ejecuta para cada idioma habilitado en `DateSearchLanguages`. Los resultados de todos los idiomas se deduplicen por el texto reconocido (`r.Text`); a continuación se toma el primer resultado con tipo de fecha válido.

Esto permite detectar "3 de mayo" (español) y "next Monday" (inglés) con la misma configuración por defecto, sin duplicar el resultado cuando ambos reconocedores coinciden sobre el mismo texto.

---

## 6. Settings

| Propiedad | Tipo | Valor por defecto | Descripción |
|---|---|---|---|
| `DateSearchEnabled` | `bool` | `true` | Toggle global de la fuente |
| `DateSearchLanguages` | `List<string>` | `["es-es", "en-us"]` | Idiomas activos para el reconocedor (default en `AppDefaults.DateSearchDefaultLanguages`) |

La detección se ejecuta **solo** contra los idiomas de `DateSearchLanguages`, no contra los 11 disponibles. Mantener la lista corta es deliberado: cada idioma extra amplía los falsos positivos (p. ej. el japonés interpreta "134.2" como el año 0134). La ventana de Settings aún no expone un selector de idiomas; el valor por defecto (es/en) cubre el caso común y la lista se persiste para cuando se añada el selector.

Los 11 idiomas disponibles son (código → nombre en UI): `es-es` Español, `en-us` English, `fr-fr` Français, `de-de` Deutsch, `it-it` Italiano, `nl-nl` Nederlands, `pt-br` Português, `zh-cn` 中文, `ja-jp` 日本語, `ko-kr` 한국어, `tr-tr` Türkçe.

El mínimo enforced es 1 idioma: si `DateSearchLanguages` se guarda vacío en el JSON (por ejemplo por un bug de UI), la carga de settings lo reemplaza por los valores por defecto, garantizando que la fuente siempre tiene al menos un idioma operativo.

---

## 7. Invariantes

- Si `DateSearchEnabled = false` → la fuente devuelve `[]` siempre.
- Si `DateSearchLanguages` está vacío en runtime → la fuente devuelve `[]` (no lanza excepción).
- Si la query no contiene ninguna fecha reconocible en ninguno de los idiomas activos → la fuente devuelve `[]`.
- Siempre se produce como máximo un único resultado (fecha simple o rango), nunca múltiples.
- La selección de celda inicial es siempre 0.
- La navegación de celdas es circular: avanzar desde la última celda vuelve a la primera, y retroceder desde la primera va a la última.
- Los errores del reconocedor (excepciones de la biblioteca externa) se capturan y loguean; la fuente devuelve `[]` sin propagar el error.
- `DateSearchScore` (ver `AppDefaults`) es superior al de apps y web search, de modo que el resultado de fecha aparece al principio de la lista cuando la query es claramente una fecha.

---

## 8. Footer hints

El footer muestra los hints contextuales para el resultado de fecha:

| Tipo | Hints |
|---|---|
| Fecha simple | `↵ Copy` · `⌘C Copy` · `←→ Switch cell` |
| Rango de fechas | `↵ Copy` · `⌘C Copy` · `←→ Switch cell` |

> **Verificar en:** `Yottacast.Core/Search/Date/DateSearch.cs` (Search, BuildDateViewModel, BuildDateRangeViewModel), `Yottacast.Core/ViewModels/DateSearchResultViewModel.cs` (Cells, SelectedCell, MoveCellLeft, MoveCellRight), `Yottacast.Core/Services/UserSettings.cs` (DateSearchEnabled, DateSearchLanguages), `Yottacast.Core/AppDefaults.cs` (DateSearchScore, DateSearchDefaultLanguages, DateSearchAvailableLanguages), `Yottacast.Core.Tests/Search/Date/DateSearchTests.cs`.
