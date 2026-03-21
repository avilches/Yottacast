Audita la documentación (`docs/`) contra el código fuente y la corrige.

## Objetivo

La documentación existe para dos cosas:
1. Ayudar a Claude a saber dónde tocar sin leer todo el código.
2. Servir de referencia al desarrollador sobre cómo funciona el proyecto.

No debe duplicar lo que ya es obvio leyendo el código (constantes, scores, regex, rutas concretas). Esto ya está descrito en la regla de documentación de `CLAUDE.md` — aplícala también aquí.

## Instrucciones

Lanza **un agente** que haga lo siguiente, en este orden:

### Paso 1 — Corregir lo existente en docs

Lee todos los ficheros `docs/*.md`. Para cada claim concreto (comportamiento, clase, método, flujo, campo de configuración, regla de negocio…), busca en el código fuente (`Yottacast/`, `Yottacast.Core/`, `Yottacast.Cli/`, `Yottacast.Core.Tests/`) la evidencia que lo respalda.

Corrige directamente en el fichero `.md` correspondiente:
- **Claims incorrectos**: la doc dice X pero el código hace Y → actualiza la doc.
- **Claims obsoletos**: la doc menciona algo que ya no existe → elimínalo.
- **Nombres desactualizados**: clases, métodos o ficheros renombrados → actualiza las referencias.

No corrijas por diferencia de nomenclatura menor; usa criterio.

### Paso 2 — Documentar lo que falta

Explora el código fuente buscando comportamientos, módulos, clases, métodos relevantes o decisiones de diseño que **no están mencionados en ningún fichero `docs/*.md`**.

Añádelos directamente en el doc que corresponda por tema. Si no encaja en ninguno existente, anótalo para el fichero de salida como propuesta estructural.

Ignora lo trivial (getters simples, constructores sin lógica, cosas obvias leyendo el código). Céntrate en: flujos de control importantes, estrategias de caché, manejo de errores relevante, decisiones de arquitectura, contratos de interfaz.

### Paso 3 — Comentarios inline en código

Si un comentario en `.cs`/`.axaml` contradice la implementación real, corrígelo directamente.

### Paso 4 — Verificar intención de CLAUDE.md contra código

Lee `CLAUDE.md` (la fuente de intención del proyecto). Si describe comportamientos, flujos o decisiones de diseño que el código no implementa o contradice, anótalos para el fichero de salida. **No modifiques `CLAUDE.md`** — solo el desarrollador lo hace.

### Paso 5 — Anotar bugs obvios

Si durante los pasos anteriores encuentras bugs evidentes (lógica claramente incorrecta, null references obvios, condiciones invertidas, recursos no liberados), anótalos para el fichero de salida. No los corrijas — solo regístralos con contexto suficiente para que sean accionables.

### Paso 6 — Revisar estructura de los docs

Evalúa la organización de `docs/`:
- ¿Hay documentos que abarcan demasiado y deberían dividirse?
- ¿Hay secciones duplicadas entre documentos?
- ¿Hay contenido que estaría mejor en otro fichero?
- ¿Hay documentos que podrían fusionarse?

Estas propuestas van al fichero de salida (no las apliques directamente).

---

## Fichero de salida

Tras aplicar las correcciones, obtén el timestamp ejecutando `date +%Y%m%d%H%M%S` y escribe `audit-result-<TIMESTAMP>.md` en la raíz del proyecto.

**Este fichero debe ser un plan autocontenido**: cualquier persona (o Claude en una sesión nueva) debe poder leerlo y ejecutar los cambios pendientes sin contexto adicional. Incluye ficheros afectados, qué cambiar, y por qué.

El fichero solo contiene lo que **no se pudo corregir automáticamente**:

```markdown
# Plan de cambios pendientes — Audit <TIMESTAMP>

Lee este fichero como un plan de trabajo. Cada sección contiene tareas accionables.
Donde hay opciones, pregunta al usuario cuál prefiere antes de actuar.

## Gaps con CLAUDE.md

Intenciones descritas en `CLAUDE.md` que el código no implementa o contradice.
No modificar `CLAUDE.md` — preguntar al usuario si debe implementarse o si la intención ha cambiado.

### [GAP] <descripción corta>

- **CLAUDE.md dice**: <cita o paráfrasis>
- **El código hace**: <qué pasa realmente>
- **Opciones**:
  - A) Implementar lo que dice CLAUDE.md
  - B) El desarrollador actualiza CLAUDE.md porque la intención cambió

...

## Propuestas estructurales de docs

Cambios en la organización de `docs/` que requieren decisión del usuario.

### [PROPUESTA] <descripción corta>

- **Ficheros afectados**: `docs/X.md`, `docs/Y.md`
- **Situación actual**: <qué pasa hoy>
- **Opciones**:
  - A) <opción y qué implica>
  - B) <opción y qué implica>
- **Recomendación**: <cuál y por qué>

...

## Bugs detectados

Problemas encontrados de pasada durante la auditoría de docs. No son problemas de documentación sino de código.

### [BUG] <descripción corta>

- **Fichero**: `<Archivo.cs:línea>`
- **Qué ocurre**: <descripción del problema>
- **Impacto**: <qué puede fallar>
- **Fix sugerido**: <qué cambiar>

...

## Resumen de lo ya aplicado

(Solo informativo — estos cambios ya están hechos, no requieren acción.)

- Claims corregidos en docs: N
- Comportamientos documentados: M
- Comentarios de código corregidos: P
```

Si una sección no tiene items, indicar "Ninguno".

Una vez escrito el fichero, indica al usuario la ruta.

Sé riguroso. El objetivo es que la documentación refleje fielmente el código actual.