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

Se muestran hasta 5 definiciones por entrada (ver `AppDefaults.DictionaryMaxDefinitionsPerItem`). Las definiciones de inflexión gramatical se descartan (kaikki: senses con campo `form_of`; API: HTML con `form-of-definition`).

## Accion al activar

Al pulsar Enter sobre un resultado, se abre la página de Wiktionary del idioma correspondiente (`https://{langCode}.wiktionary.org/wiki/{word}`) en el navegador configurado por el usuario.

## Fuentes de datos: local vs API

La fuente tiene dos modos de obtener definiciones, por idioma configurado:

**Local (SQLite)**: si existe `~/.cache/yottacast/dictionary/{lang}.db`, se usa para ese idioma. La búsqueda es instantánea y offline. Proviene de datos de kaikki.org, que extrae definiciones ricas del Wiktionary en el idioma nativo (ej: `es.db` tiene las 15+ definiciones de "casa" en español). 16 idiomas disponibles: ver `AppDefaults.KaikkiLanguages`.

**API (fallback)**: si no existe DB local para un idioma, la app llama a `https://en.wiktionary.org/api/rest_v1/page/definition/{word}`. Una sola petición devuelve definiciones para todos los idiomas disponibles; los idiomas configurados sin DB local filtran qué secciones se muestran. Las definiciones llegan en HTML que se limpia antes de mostrar. La cobertura del Wiktionary inglés para palabras no inglesas es más escasa que la de las ediciones nativas.

30 idiomas disponibles en total; 16 tienen soporte local (kaikki), los 14 restantes solo via API. Ver `AppDefaults.DictionaryAvailableLanguages` y `AppDefaults.KaikkiLanguages`.

## Diccionario local (kaikki)

Los ficheros de diccionario local se generan con los scripts en `tools/kaikki/` y se colocan manualmente en `~/.cache/yottacast/dictionary/`.

**Formato de los ficheros básicos (JSONL)**:
```
{"w":"casa","p":"Noun","d":["edificación destinada a vivienda","domicilio"],"e":"Vivo en una casa grande."}
```

**Rutas** (ver `AppPaths`):
- JSONL: `~/.cache/yottacast/dictionary/{lang}.jsonl`
- SQLite: `~/.cache/yottacast/dictionary/{lang}.db`

**Conversión automática**: si al arrancar existe un `.jsonl` pero no el `.db` correspondiente, `DictionarySource.Start()` lanza la conversión en background (`LocalDictionaryConverter`). Mientras convierte, las búsquedas de ese idioma usan la API. Tras completar, las búsquedas siguientes usan el DB local.

**Generación de los ficheros**:
```bash
cd tools/kaikki
python step1_kaikki_to_json.py --lang es   # descarga kaikki y produce es.jsonl
python step2_json_to_sqlite.py --lang es   # convierte a es.db
cp output/es.db ~/.cache/yottacast/dictionary/
```

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

> **Verificar en:** `DictionarySource.cs` (Start, ConvertInBackground, SearchAsync, BuildDefsFromLocal), `LocalDictionaryDb.cs` (Lookup, Exists), `LocalDictionaryConverter.cs` (ConvertAsync), `DictionaryApi.cs` — clase `DictionaryApiClient` (LookupAsync, StripHtml, IsFormOfDefinition), `DictionaryResultViewModel.cs`, `DictionaryResultItemView.axaml`, `UserSettings.cs` (EnableDictionary, DictionaryPrefix, DictionaryShowAlways, DictionaryLanguages), `AppDefaults.cs` (DictionaryTimeoutSeconds, DictionaryDefaultPrefix, DictionaryAvailableLanguages, DictionaryDefaultLanguages, KaikkiLanguages), `AppPaths.cs` (DictionaryDir, DictionaryDb, DictionaryJsonl), `SettingsWindowViewModel.cs` (DictionaryLanguageItem, DictionaryLanguages). Tests: `LocalDictionaryTests.cs`.
