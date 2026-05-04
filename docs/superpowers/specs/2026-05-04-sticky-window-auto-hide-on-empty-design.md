# Spec: sticky window auto-hide when empty on focus loss

**Fecha:** 2026-05-04

---

## Comportamiento actual

En modo sticky (`StickyWindow = true`), la ventana permanece visible aunque pierda el foco. Al perder el foco, solo se inicia el decay timer para limpiar el texto transcurrido cierto tiempo.

En modo no-sticky, al perder el foco la ventana se oculta siempre (salvo si Settings está abierta).

## Comportamiento deseado

En modo sticky, al perder el foco:
- Si el campo de búsqueda está **vacío**: ocultar la ventana (igual que non-sticky).
- Si el campo de búsqueda **tiene texto**: comportamiento actual — iniciar el decay timer.
- Si la ventana de Settings está visible: no ocultar en ningún caso (guard existente).

---

## Diseño

### Punto de cambio: `App.axaml.cs` — handler `Deactivated`

El handler `Deactivated` en `App.axaml.cs` gestiona actualmente solo el caso non-sticky. Se unifica la lógica de ambos modos en un único handler:

```
Deactivated → si Settings visible: ignorar
             → si non-sticky OR (sticky AND SearchText vacío): Hide + OnHide
             → si sticky AND SearchText no vacío: StartDecayTimer
```

### Punto de cambio: `MainWindow.axaml.cs` — handler `Deactivated`

El handler `Deactivated` actual en `MainWindow.axaml.cs` llama a `StartDecayTimer()` en modo sticky. Esta responsabilidad se mueve a `App.axaml.cs`, por lo que el handler de `MainWindow.axaml.cs` queda vacío y se elimina.

---

## Invariantes

| Condición | Resultado |
|---|---|
| Sticky + campo vacío + pierde foco | Ventana oculta |
| Sticky + campo con texto + pierde foco | Decay timer iniciado, ventana visible |
| Sticky + Settings abierta + pierde foco | Sin cambio |
| Non-sticky + pierde foco | Ventana oculta (comportamiento sin cambios) |
| Ventana se oculta por hotkey toggle | Sin cambio (no pasa por Deactivated) |

---

## Archivos afectados

- `Yottacast/App.axaml.cs` — refactorizar handler `Deactivated`
- `Yottacast/Views/MainWindow.axaml.cs` — eliminar handler `Deactivated` (lógica movida)
- `docs/ui-main-window.md` — actualizar sección 1 (ciclo de vida) y sección 15 (decay timer)

> **Verificar en:** `App.axaml.cs` handler `mainWindow.Deactivated`. `MainWindow.axaml.cs` constructor.
