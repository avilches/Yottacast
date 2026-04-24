# Definiciones de diccionario

Yottacast puede buscar definiciones de palabras en un diccionario online multilingue. Los resultados muestran la palabra, su categoría gramatical, opcionalmente el idioma, la definición y un ejemplo de uso.

---

## Invariantes

- Las queries que empiezan por `:` (modo emoji) nunca activan la busqueda de definiciones.
- Es una fuente deferred: las peticiones HTTP se lanzan tras el debounce de 250 ms, no en cada keystroke.
- Si la API no responde o devuelve 404, no se muestran resultados de diccionario (sin error visible para el usuario).
- El timeout HTTP es de 5 segundos (ver `AppDefaults.DictionaryTimeoutSeconds`).
- Si el icono PNG embebido no existe, el hueco del icono queda vacio sin error.

## Modos de activacion

| Modo | Cuando aparece | Score | Ejemplo |
|---|---|---|---|
| `PrefixOnly` (default) | Solo si la query empieza por `"{prefijo} "` (prefijo + espacio) | 3.5 | Escribir "define hello" busca "hello" |
| `ShowAlways` | Siempre (query no vacia, sin modo emoji) | 2.5 | Escribir "hello" busca "hello" |

En modo `PrefixOnly` el score es 3.5 (intent explicito). En modo `ShowAlways` el score es 2.5, inferior al de web search (3.0), para que las definiciones no dominen sobre los resultados de busqueda web.

## Presentacion del resultado

Cada resultado usa `DictionaryResultViewModel` y se renderiza con `DictionaryResultItemView`. El layout muestra:

- **Fila 1**: la palabra buscada en negrita, seguida de una pill con la categoría gramatical (Noun, Verb…). Si el usuario tiene más de un idioma configurado, aparece una segunda pill con el nombre del idioma.
- **Fila 2**: texto de la definición (máx. 2 líneas, con ellipsis).
- **Fila 3** (opcional): ejemplo de uso entre comillas tipográficas, en itálica y color más tenue.

La etiqueta de idioma **no aparece** cuando solo hay un idioma configurado (`DictionaryLanguages.Count == 1`).

Se muestran hasta 3 definiciones por entrada (partOfSpeech), con un máximo global definido por `AppDefaults.SearchSourceLimit`. Las definiciones de inflexión gramatical (`form-of-definition` en el HTML) se descartan. El HTML del API se limpia extrayendo el texto antes del primer `<ol>` (o el contenido del primer `<li>` si no hay texto previo), eliminando las etiquetas restantes y colapsando espacios.

## Accion al activar

Al pulsar Enter sobre un resultado, se abre la pagina de Wiktionary para esa palabra (`https://en.wiktionary.org/wiki/{word}`) en el navegador configurado por el usuario.

## API

Usa la API REST de Wiktionary (`https://en.wiktionary.org/api/rest_v1/page/definition/{word}`). Una unica peticion devuelve definiciones para todos los idiomas disponibles; los idiomas configurados por el usuario filtran que secciones de la respuesta se muestran. Las definiciones llegan en HTML que se limpia antes de mostrar.

30 idiomas disponibles: English, Spanish, French, German, Italian, Portuguese, Russian, Arabic, Hindi, Japanese, Korean, Chinese, Turkish, Dutch, Polish, Swedish, Czech, Danish, Finnish, Greek, Hebrew, Hungarian, Indonesian, Norwegian, Romanian, Thai, Ukrainian, Vietnamese, Catalan, Galician. Ver `AppDefaults.DictionaryAvailableLanguages` para la lista completa con codigos ISO.

## Settings del usuario

Cuatro propiedades en `UserSettings`:

| Propiedad | Tipo | Default | Descripcion |
|---|---|---|---|
| `EnableDictionary` | `bool` | `true` | Activa o desactiva la fuente |
| `DictionaryPrefix` | `string` | `"define"` | Prefijo para modo PrefixOnly |
| `DictionaryShowAlways` | `bool` | `false` | Si true, busca definiciones para cualquier query |
| `DictionaryLanguages` | `List<string>` | `["en"]` | Codigos ISO de los idiomas a mostrar (de los 30 disponibles) |

En la ventana de Settings, la seccion "Dictionary" permite:
- Activar/desactivar con un ToggleSwitch.
- Marcar "Show always" para desactivar el prefijo.
- Editar el prefijo (solo visible cuando ShowAlways esta desactivado).
- Seleccionar los idiomas para los que se muestran definiciones.

## Icono

El icono de los resultados es un PNG embebido (`Search/Dictionary/Icons/wiktionary.png`), cargado una sola vez al inicializar la source.

> **Verificar en:** `DictionarySource.cs` (SearchAsync, LoadIcon), `DictionaryApi.cs` (LookupAsync, StripHtml, IsFormOfDefinition), `DictionaryResultViewModel.cs`, `DictionaryResultItemView.axaml`, `UserSettings.cs` (EnableDictionary, DictionaryPrefix, DictionaryShowAlways, DictionaryLanguages), `AppDefaults.cs` (DictionaryTimeoutSeconds, DictionaryDefaultPrefix, DictionaryAvailableLanguages, DictionaryDefaultLanguages), `SettingsWindowViewModel.cs` (DictionaryLanguageItem, DictionaryLanguages).
