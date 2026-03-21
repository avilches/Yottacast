Lanza varios agentes en paralelo para hacer una auditoría bidireccional entre docs/ y el código fuente.

## Instrucciones

Lanza **en paralelo** los dos agentes siguientes y espera a que ambos terminen antes de sintetizar el resultado final.

---

### Agente 1 — Spec → Código (¿está implementado?)

Tarea: Lee todos los ficheros `docs/*.md` del proyecto. Para cada claim concreto que hagas (comportamiento descrito, clase mencionada, método, flujo, campo de configuración, regla de negocio…), busca en el código fuente (`Yottacast/`, `Yottacast.Core/`, `Yottacast.Cli/`, `Yottacast.Core.Tests/`) la evidencia que lo respalda.

Marca como **[HUÉRFANO]** cualquier claim de la spec que no tenga implementación real o que la implementación contradiga lo descrito.

Para cada huérfano incluye:
- El fichero doc y la cita exacta del texto
- Por qué no encuentras código que lo respalde (¿clase inexistente? ¿método diferente? ¿comportamiento distinto?)

No marques como huérfano algo por una diferencia de nomenclatura menor; busca con criterio.

---

### Agente 2 — Código → Spec (¿está documentado?)

Tarea: Explora el código fuente (`Yottacast/`, `Yottacast.Core/`, `Yottacast.Cli/`, `Yottacast.Core.Tests/`) buscando comportamientos, módulos, clases, métodos relevantes o decisiones de diseño que **no están mencionados en ningún fichero `docs/*.md`**.

Marca como **[NO DOCUMENTADO]** todo lo que encuentres que falte en la spec.

Para cada entrada incluye:
- Archivo y función/clase (con línea si es posible)
- Qué hace y por qué es relevante documentarlo

Ignora detalles triviales (getters simples, constructores sin lógica). Céntrate en comportamientos no obvios, flujos de control importantes, estrategias de caché, manejo de errores relevantes, decisiones de arquitectura.

---

## Output final (sintetizado por ti, no por los agentes)

Cuando ambos agentes hayan terminado, obtén el timestamp actual ejecutando `date +%Y%m%d%H%M%S` y escribe el resultado en un fichero llamado `audit-result-<TIMESTAMP>.md` en la raíz del proyecto.

El fichero debe tener este formato:

```
## DIRECCIÓN 1 — Huérfanos (spec sin código)

[HUÉRFANO] <doc/fichero.md> — "<cita exacta>"
→ <razón por la que no hay implementación>

...

## DIRECCIÓN 2 — No documentado (código sin spec)

[NO DOCUMENTADO] <Archivo.cs:línea> — <Clase/Método>
→ <qué hace y por qué importa>

...

## Resumen de confianza

- Huérfanos encontrados: N
- No documentados encontrados: M
- Alineación estimada: X% (0 = caos total, 100 = perfectamente alineado)
- Veredicto: <una frase directa sobre el estado de salud de la documentación>
```

Una vez escrito el fichero, indica al usuario la ruta del fichero generado.

Sé implacable. El objetivo es encontrar gaps reales, no validar que todo está bien.