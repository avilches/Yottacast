# tools/kaikki — Generación de diccionarios locales

Scripts para producir los ficheros de diccionario local que Yottacast usa en lugar de la API de Wiktionary.

## Contexto

La API de `en.wiktionary.org` devuelve definiciones muy escasas para palabras en idiomas no ingleses
(p.ej. "casa" en español → 1 definición en inglés). [kaikki.org](https://kaikki.org) extrae los dumps
de cada edición nativa del Wiktionary (eswiktionary para español, dewiktionary para alemán…) y ofrece
JSONL estructurado con definiciones ricas **en el idioma nativo**.

Los archivos se descargan desde `https://kaikki.org/downloads/{lang_code}/{lang_code}-extract.jsonl.gz`.

El flujo completo:

```
kaikki.org (JSONL ~1 GB)
    → step1: filtrado + extracción → output/{lang}.jsonl (~50 MB)
        → la app lo convierte a SQLite en background al arrancar
            → búsquedas locales instantáneas y offline
```

## Idiomas soportados (16)

`en es fr de it pt ru tr nl pl th ko ja zh el id`

Los 14 idiomas restantes de Yottacast (he, hi, ar, cs, sv, da, fi, hu, no, ro, uk, vi, ca, gl)
no tienen datos en kaikki y siguen usando la API de Wiktionary como fallback.

## Uso

### Paso 1 — kaikki JSONL → basic JSONL

```bash
python step1_kaikki_to_json.py --lang es        # solo español
python step1_kaikki_to_json.py --lang es fr de  # varios idiomas
python step1_kaikki_to_json.py --lang all       # todos los idiomas
python step1_kaikki_to_json.py                  # equivalente a --lang all
```

- Descarga el JSONL crudo de kaikki.org a `cache/{lang_code}.jsonl.gz` (se reutiliza en ejecuciones siguientes).
- Filtra entradas `form-of` (inflexiones gramaticales sin definición propia).
- Extrae: palabra, parte del discurso, hasta 5 definiciones, primer ejemplo.
- Escribe `output/{lang}.jsonl` (una línea JSON por entrada).

### Paso 2 (opcional) — basic JSONL → SQLite

```bash
python step2_json_to_sqlite.py --lang es        # solo español
python step2_json_to_sqlite.py --lang all       # todos los JSONL en output/
python step2_json_to_sqlite.py                  # equivalente a --lang all
```

**Nota**: este paso lo hace la app automáticamente al arrancar si detecta un `.jsonl` sin su `.db`.
Solo es necesario correrlo manualmente si quieres pre-generar el SQLite (p.ej. para distribuirlo
directamente o para verificar el output antes de que llegue a un usuario).

## Instalar el diccionario en la app

```bash
# Tras ejecutar step1:
mkdir -p ~/.cache/yottacast/dictionary
cp output/es.jsonl ~/.cache/yottacast/dictionary/
# Arrancar Yottacast → convierte es.jsonl a es.db en background al primer inicio
```

O con el SQLite ya generado:

```bash
cp output/es.db ~/.cache/yottacast/dictionary/
# La app usa el .db directamente, sin conversión
```

## Formato del basic JSONL

Una línea por entrada (word + part-of-speech). Campos:

| Campo | Tipo | Descripcion |
|---|---|---|
| `w` | string | Palabra |
| `p` | string | Parte del discurso (Noun, Verb, Adjective…) |
| `d` | string[] | Definiciones (máx. 5) |
| `e` | string? | Primer ejemplo de uso (opcional) |

```json
{"w":"casa","p":"Noun","d":["edificación destinada a vivienda","domicilio"],"e":"Vivo en una casa grande."}
```

## Esquema SQLite

```sql
CREATE TABLE entries (
    word TEXT NOT NULL COLLATE NOCASE,
    pos  TEXT NOT NULL,
    definitions TEXT NOT NULL,  -- JSON array de strings
    example TEXT                -- nullable
);
CREATE INDEX idx_word ON entries(word COLLATE NOCASE);
```

## Estructura de directorios

```
tools/kaikki/
├── step1_kaikki_to_json.py   ← descarga + extracción
├── step2_json_to_sqlite.py   ← conversión a SQLite (opcional)
├── README.md
├── cache/                    ← JSONL.gz crudos de kaikki (no subir al repo, son grandes)
│   └── es.jsonl.gz
└── output/                   ← basic JSONL y SQLite generados
    ├── es.jsonl
    └── es.db
```

Los directorios `cache/` y `output/` están en `.gitignore`.
