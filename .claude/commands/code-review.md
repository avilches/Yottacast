Revisa el código fuente en profundidad buscando bugs, inconsistencias y problemas de calidad.

## Objetivo

Revisión profunda del código, no de la documentación. Busca problemas reales que afecten al funcionamiento, mantenibilidad o seguridad.

## Instrucciones

Lanza **un agente** que explore el código fuente (`Yottacast/`, `Yottacast.Core/`, `Yottacast.Cli/`, `Yottacast.Core.Tests/`) buscando:

### Bugs y errores de lógica
- Condiciones invertidas o incorrectas
- Null references no protegidos en rutas alcanzables
- Recursos no liberados (IDisposable sin dispose, streams abiertos)
- Race conditions en código async
- Off-by-one, divisiones por cero, overflows

### Inconsistencias
- Métodos que prometen algo en su nombre pero hacen otra cosa
- Contratos de interfaz que no se respetan en las implementaciones
- Comportamiento diferente entre plataformas cuando debería ser igual
- Tests que no testean lo que dicen testear

### Gaps con la intención de CLAUDE.md
- `CLAUDE.md` es la fuente de intención del proyecto. Si el código contradice una intención descrita allí (comportamiento, flujo, decisión de diseño), reportarlo como gap.
- No modificar `CLAUDE.md` — solo el desarrollador lo hace.

### Problemas de arquitectura
- Violaciones de las reglas de `CLAUDE.md` (clases static con lógica de negocio, código OS-específico fuera de AppHandler/PlatformProvider)
- Dependencias circulares o acoplamiento excesivo
- Código muerto alcanzable (no trivial)

### Qué ignorar
- Estilo, formateo, naming conventions menores
- Falta de tests (eso es otra auditoría)
- Optimizaciones de rendimiento teóricas sin evidencia de problema
- Cualquier cosa relacionada con documentación (eso lo hace `/audit`)

---

## Fichero de salida

Obtén el timestamp ejecutando `date +%Y%m%d%H%M%S` y escribe `review-result-<TIMESTAMP>.md` en la raíz del proyecto.

**Este fichero debe ser un plan autocontenido**: cualquier persona (o Claude en una sesión nueva) debe poder leerlo y ejecutar los fixes sin contexto adicional.

```markdown
# Plan de fixes — Code Review <TIMESTAMP>

Lee este fichero como un plan de trabajo. Cada item es accionable.
Donde hay opciones o trade-offs, pregunta al usuario cuál prefiere antes de actuar.

## Gaps con CLAUDE.md

Intenciones de `CLAUDE.md` que el código no implementa o contradice.
Preguntar al usuario si debe implementarse o si la intención ha cambiado.

### [GAP] <descripción corta>

- **CLAUDE.md dice**: <cita o paráfrasis>
- **El código hace**: <qué pasa realmente>
- **Opciones**:
  - A) Implementar lo que dice CLAUDE.md
  - B) El desarrollador actualiza CLAUDE.md

...

## Bugs

### [BUG] <descripción corta>

- **Fichero**: `<Archivo.cs:línea>`
- **Qué ocurre**: <descripción precisa del problema>
- **Cómo reproducirlo**: <si es posible describir el escenario>
- **Impacto**: <qué puede fallar en producción>
- **Fix sugerido**: <qué cambiar, con suficiente detalle para implementarlo>

...

## Inconsistencias

### [INCONSISTENCIA] <descripción corta>

- **Ficheros**: `<Archivo1.cs:línea>`, `<Archivo2.cs:línea>`
- **Qué ocurre**: <descripción de la contradicción>
- **Opciones**:
  - A) <opción y qué implica>
  - B) <opción y qué implica>
- **Recomendación**: <cuál y por qué>

...

## Problemas de arquitectura

### [ARQUITECTURA] <descripción corta>

- **Ficheros afectados**: <lista>
- **Situación actual**: <qué pasa hoy>
- **Problema**: <por qué es un problema>
- **Refactor sugerido**: <qué hacer, con pasos concretos>

...

## Resumen

- Bugs encontrados: N (críticos: X, menores: Y)
- Inconsistencias: M
- Problemas de arquitectura: P
```

Si una sección no tiene items, indicar "Ninguno".

Una vez escrito el fichero, indica al usuario la ruta.

Sé implacable. El objetivo es encontrar problemas reales, no validar que todo está bien.