# Definiciones de diccionario

Yottacast puede buscar definiciones de palabras en un diccionario online. Los resultados muestran la palabra, su categoria gramatical (noun, verb, adjective...), la definicion y opcionalmente un ejemplo de uso.

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

## Titulo y subtitulo del resultado

El titulo tiene el formato `"{word} ({partOfSpeech}): {definition}"`, p. ej. `"hello (noun): an utterance of 'hello'; a greeting"`.

El subtitulo muestra el ejemplo de uso si existe (entre comillas), o la transcripcion fonetica si la hay.

Se muestran hasta 3 definiciones por categoria gramatical, con un maximo global definido por `AppDefaults.SearchSourceLimit`.

## Accion al activar

Al pulsar Enter sobre un resultado, se abre la URL fuente de la definicion en el navegador configurado por el usuario.

## API

Usa la API publica de Free Dictionary API (`https://api.dictionaryapi.dev/api/v2/entries/{lang}/{word}`). Soporta 15 idiomas: English, Spanish, French, German, Italian, Portuguese (Brazil), Russian, Arabic, Hindi, Japanese, Korean, Turkish, Czech, Dutch y Slovak. El usuario puede seleccionar uno o varios idiomas en Settings; las peticiones se lanzan en paralelo (una por idioma) y los resultados se combinan. El titulo de cada resultado incluye el codigo de idioma entre corchetes (p. ej. `[en]`, `[es]`).

## Settings del usuario

Cuatro propiedades en `UserSettings`:

| Propiedad | Tipo | Default | Descripcion |
|---|---|---|---|
| `EnableDictionary` | `bool` | `true` | Activa o desactiva la fuente |
| `DictionaryPrefix` | `string` | `"define"` | Prefijo para modo PrefixOnly |
| `DictionaryShowAlways` | `bool` | `false` | Si true, busca definiciones para cualquier query |
| `DictionaryLanguages` | `List<string>` | `["en"]` | Idiomas en los que buscar definiciones (ver `AppDefaults.DictionaryAvailableLanguages` para la lista completa) |

En la ventana de Settings, la seccion "Dictionary" permite:
- Activar/desactivar con un ToggleSwitch.
- Marcar "Show always" para desactivar el prefijo.
- Editar el prefijo (solo visible cuando ShowAlways esta desactivado).
- Seleccionar idiomas con checkboxes (siempre debe haber al menos uno seleccionado).

## Icono

El icono de los resultados es un PNG embebido (`Search/Dictionary/Icons/wiktionary.png`), cargado una sola vez al inicializar la source.

> **Verificar en:** `DictionarySource.cs` (SearchAsync, LoadIcon), `DictionaryApi.cs` (LookupAsync, DTOs), `UserSettings.cs` (EnableDictionary, DictionaryPrefix, DictionaryShowAlways, DictionaryLanguages), `AppDefaults.cs` (DictionaryTimeoutSeconds, DictionaryDefaultPrefix, DictionaryDefaultLanguages, DictionaryAvailableLanguages), `SettingsWindowViewModel.cs` (DictionaryLanguageItem, DictionaryLanguages).
