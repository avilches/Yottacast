Audita la documentación (`docs/`) contra el código fuente y la corrige.

## Objetivo

La documentación existe para dos cosas:
1. Ayudar a Claude a saber dónde tocar sin leer todo el código.
2. Servir de referencia al desarrollador sobre cómo funciona el proyecto.

No debe duplicar lo que ya es obvio leyendo el código (constantes, scores, regex, rutas concretas). Esto ya está descrito en la regla de documentación de `CLAUDE.md` — aplícala también aquí.

## Instrucciones

El proceso se ejecuta en dos fases. Cada fase lanza agentes en paralelo, uno por fichero `docs/*.md`.

### Fase 1 — Verificar y corregir lo que ya dice la doc (doc → código)

Lanza un agente por cada fichero `docs/*.md`, todos en paralelo. Espera a que todos terminen antes de continuar con la Fase 2.

**Tarea de cada agente:**

> Eres el verificador del fichero `docs/X.md`.
>
> Lee el fichero. Para cada claim concreto (comportamiento, clase, método, flujo, campo de configuración, regla de negocio…), busca **activamente** en el código fuente (`Yottacast/`, `Yottacast.Core/`, `Yottacast.Cli/`, `Yottacast.Core.Tests/`) la evidencia que lo respalda. No asumas que algo es correcto porque suena razonable — verifícalo en el código.
>
> Corrige directamente en el fichero:
> - **Claims incorrectos**: la doc dice X pero el código hace Y → actualiza la doc.
> - **Claims obsoletos**: la doc menciona algo que ya no existe → elimínalo.
> - **Nombres desactualizados**: clases, métodos o ficheros renombrados → actualiza las referencias.
>
> **Solo toca lo que ya está escrito.** No añadas contenido nuevo. No corrijas por diferencia de nomenclatura menor; usa criterio.
>
> Devuelve un resumen de cuántos claims verificaste y cuántos corregiste.

### Fase 2 — Documentar lo que falta (código → doc)

Una vez terminada la Fase 1, lanza un agente por cada fichero `docs/*.md`, todos en paralelo. Espera a que todos terminen antes de continuar.

**Tarea de cada agente:**

> Eres el explorador del fichero `docs/X.md`.
>
> Este doc cubre el tema `<topic>`. Lee el fichero para entender qué ya está documentado sobre ese tema.
>
> Explora el código fuente (`Yottacast/`, `Yottacast.Core/`, `Yottacast.Cli/`, `Yottacast.Core.Tests/`) buscando comportamientos, flujos, clases, métodos o decisiones de diseño **relacionados con el tema de este doc** que no estén mencionados en él.
>
> **Solo añade cosas nuevas.** No modifiques ni corrijas lo que ya existe — eso ya lo hizo la Fase 1.
>
> Ignora lo trivial (getters simples, constructores sin lógica, cosas obvias leyendo el código). Céntrate en: flujos de control importantes, estrategias de caché, manejo de errores relevante, decisiones de arquitectura, contratos de interfaz.
>
> Si encuentras algo relevante que claramente no encaja en este doc sino en otro, anótalo (fichero destino sugerido + qué añadir) pero no lo escribas — se incluirá en el fichero de salida como propuesta estructural.
>
> Devuelve un resumen de cuántos comportamientos añadiste y cuántas propuestas estructurales encontraste.

### Fase 3 — Comentarios inline en código

Si un comentario en `.cs`/`.axaml` contradice la implementación real, corrígelo directamente.

### Fase 4 — Verificar intención de CLAUDE.md contra código

Lee `CLAUDE.md` (la fuente de intención del proyecto). Si describe comportamientos, flujos o decisiones de diseño que el código no implementa o contradice, anótalos para el fichero de salida. **No modifiques `CLAUDE.md`** — solo el desarrollador lo hace.

### Fase 5 — Anotar bugs obvios

Si durante los pasos anteriores encuentras bugs evidentes (lógica claramente incorrecta, null references obvios, condiciones invertidas, recursos no liberados), anótalos para el fichero de salida. No los corrijas — solo regístralos con contexto suficiente para que sean accionables.

### Fase 5b — Anotar trabajo incompleto

Busca en el código fuente señales de trabajo intencionalmente incompleto:
- Comentarios `TODO`, `FIXME`, `HACK`, `XXX`
- Métodos que lanzan `NotImplementedException` o `throw new Exception("not implemented")`
- Stubs vacíos o con `// placeholder` / `// TODO: implement`
- Funcionalidad parcialmente cableada: UI que expone una opción pero el backend no la conecta, interfaces registradas en DI sin implementación real, eventos que se disparan pero nadie escucha

Anótalos para el fichero de salida. No los corrijas — solo regístralos con suficiente contexto para que sean accionables.

### Fase 6 — Revisar estructura de los docs

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

## Trabajo incompleto

Implementaciones parciales, TODOs y funcionalidad a medias encontrada en el código. No son bugs — son tareas pendientes de terminar.

### [WIP] <descripción corta>

- **Fichero**: `<Archivo.cs:línea>`
- **Qué hay**: <qué está implementado actualmente>
- **Qué falta**: <qué parece incompleto y por qué>
- **Señal**: <TODO / NotImplementedException / stub / no conectado en DI / etc.>

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