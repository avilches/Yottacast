# Emoji Grid - Viewport Scrolling: Gotchas y Solución

Este documento recoge los invariantes, trampas y decisiones de diseño del scroll del grid de emojis
por secciones. El scroll funciona correctamente; este doc existe para no olvidar por qué está
implementado así y evitar regresiones al tocar el área.

---

## El problema estructural de fondo

Las categorías de emoji NO están alineadas a filas de `Columns` celdas.

Ejemplo real con `Columns = 10`:

```
Smileys & Emotion: 162 celdas → índices Default 0-161
People & Body:     empieza en índice Default 162 (162 % 10 = 2, NO es múltiplo de 10)
```

El modelo AXAML renderiza cada sección como su propio `UniformGrid` que siempre empieza en col 0:

```xml
<UniformGrid Columns="10"/>   <!-- siempre col 0 para la primera celda del slice -->
```

Esto tiene dos consecuencias que hay que gestionar activamente:

1. **Visual**: la cola de una sección (sus últimas N < Columns celdas) aparece como fila incompleta.
   Si esa fila incompleta queda en el borde del viewport, se ve rota o huérfana.
2. **Navegación**: `SelectDown/Up` calculan posición relativa a `sectionStart`. Si `_viewportStartCell`
   no está alineado a una fila de la sección, la col visual y la col de navegación divergen.

---

## Invariante crítico: section-row-aligned

**`_viewportStartCell % Columns == 0` (flat-row-aligned) NO es suficiente.**

El invariante correcto es:

```
(viewportStartCell - sectionStart) % Columns == 0
```

donde `sectionStart` es el inicio en Default de la sección que contiene `viewportStartCell`.

**Por qué importa**: el `UniformGrid` de cada sección renderiza la primera celda visible en col 0.
`SelectDown/SelectUp/GetPosition` calculan la col como `(cellIndex - sectionStart) % Columns`.
Si estas dos cosas no coinciden, el cursor y la navegación apuntan a columnas distintas.

**Ejemplo concreto**: People & Body empieza en índice 162 (162 % 10 = 2).

Con `_viewportStartCell = 170` (flat-aligned, 170 % 10 = 0):

```
People[8] = índice Default 170
  → Visual col en UniformGrid: 0   (primera celda del slice)
  → Col de navegación: (170 - 162) % 10 = 8
```

People[8] aparece en col 0 visualmente, pero DOWN lo trata como col 8.

Con `_viewportStartCell = 172` (section-aligned: 162 + 10):

```
People[10] = índice Default 172
  → Visual col: 0
  → Col de navegación: (172 - 162) % 10 = 0   ✓
```

---

## Solución implementada

### Padding de secciones (`GroupIntoSections`)

Cada sección (excepto la última del slice) se rellena con celdas `IsPlaceholder=true` hasta
completar la fila. Así cada `UniformGrid` recibe un número exacto de filas completas.

- Celdas placeholder: `Opacity=0` + `IsHitTestVisible=false` (no `IsVisible=false`, que colapsa
  el elemento en Avalonia eliminando el espacio que ocupa en el grid).
- La última sección del slice NO se rellena: una cola parcial al final del viewport es válida.

### `ComputeVisibleDefaultCount`

Calcula cuántas celdas reales caben en `maxRows` filas renderizadas a partir de `start`.
El cálculo naive `defaultVisibleRows × Columns` es incorrecto: el padding de cada sección
consume filas renderizadas adicionales, reduciendo las celdas reales que caben en el viewport.

### `AlignToSectionRow` / `SectionStartOf` / `SectionEndOf`

Helpers para calcular posiciones section-row-aligned:

- `SectionStartOf(pinnedCount, defaultIndex)`: inicio de la sección que contiene `defaultIndex`.
- `SectionEndOf(pinnedCount, defaultIndex)`: fin exclusivo de esa sección.
- `AlignToSectionRow(pinnedCount, pos)`:
  `sectionStart + floor((pos - sectionStart) / Columns) * Columns`

### `EnsureVisible` - reglas de scroll

**UP scroll** (celda seleccionada por encima del viewport):
```
newStart = AlignToSectionRow(pinnedCount, defaultIndex)
```
Nunca usar `(defaultIndex / Columns) * Columns` - eso es flat-aligned y rompe secciones
que no empiezan en múltiplo de `Columns`.

**DOWN scroll** (celda seleccionada por debajo del viewport):
1. Calcular `rawStart` (estimación de dónde debe empezar el viewport para que la celda quede
   cerca del final).
2. Ceiling-align dentro de la sección de `rawStart`:
   `sectionStart + ceil((rawStart - sectionStart) / Columns) * Columns`
3. Si el resultado aterriza en la cola parcial de esa sección (y no es la primera fila),
   saltar a `sectionEnd` para que el top del viewport nunca sea una fila huérfana paddeada.
4. Avanzar row a row (section-row-aligned) con `ComputeVisibleDefaultCount` hasta que la
   celda seleccionada quede dentro del viewport visible.

---

## Gotchas técnicos

- **`IsVisible=false` en Avalonia colapsa el elemento** (equivale a `display:none`). Para celdas
  invisibles que mantengan espacio en `UniformGrid` usar `Opacity=0` + `IsHitTestVisible=false`.

- **El problema no es visual sino de coherencia**: el `UniformGrid` renderiza la celda en la
  posición dentro del slice; `SelectDown/Up/GetPosition` usan `(cellIndex - sectionStart) % Columns`.
  Si estas dos posiciones no coinciden, el cursor y la lógica de navegación divergen silenciosamente.

- **Flat-row-aligned ≠ section-row-aligned**: un viewport start que es múltiplo de `Columns` solo
  garantiza coherencia si `sectionStart` también lo es. En emojis reales, prácticamente todas las
  secciones salvo la primera rompen esta condición.

- **`VisibleSections` y `EnsureVisible` deben usar exactamente el mismo modelo de conteo**.
  Si el getter toma N celdas y `EnsureVisible` cree que caben M, la celda seleccionada puede
  parecer visible para `EnsureVisible` y no aparecer en `VisibleSections`.

---

## Estado del código

- `EmojiCellViewModel.IsPlaceholder` + `static Placeholder` ✓
- `GroupIntoSections` con padding a múltiplo de Columns ✓
- `ComputeVisibleDefaultCount` ✓
- `SectionStartOf` / `SectionEndOf` / `AlignToSectionRow` ✓
- `EnsureVisible` usa section-row-aligned (UP y DOWN) ✓

> **Verificar en:**
> - `Yottacast.Core/ViewModels/EmojiGridResultViewModel.cs` - `EnsureVisible`,
>   `AlignToSectionRow`, `SectionStartOf`, `SectionEndOf`, `ComputeVisibleDefaultCount`,
>   `GroupIntoSections`
> - `Yottacast.Core/ViewModels/EmojiCellViewModel.cs` - `IsPlaceholder`, `Placeholder`
> - `Yottacast/Views/MainWindow.axaml` - estilo `Border.emoji-placeholder`
> - `Yottacast/Views/Results/EmojiGridResultView.axaml` - binding `Classes.emoji-placeholder`
> - `Yottacast.Core.Tests/Search/EmojiSearchTests.cs` - tests de viewport y padding
