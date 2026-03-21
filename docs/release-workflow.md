# Release workflow: assets embebidos en el ensamblado

Algunos assets pesados se gestionan fuera del control de versiones y se incorporan al binario en tiempo de compilación. El `.csproj` de `Yottacast.Core` tiene targets `BeforeBuild` que los descargan o copian si no están presentes; todos usan `Condition="!Exists(...)"` para ser idempotentes.

## Assets y su origen

| Fichero | Cómo se obtiene | Cuándo regenerar |
|---|---|---|
| `Search/Calculator/math.min.js` | Descarga desde cdnjs en build | Borrar el fichero y recompilar |
| `Search/Emoji/emoji-data.json` | Descarga desde iamcal/emoji-data en build | Borrar el fichero y recompilar |
| `Search/Emoji/emoji-cache.json` | Copiado desde AppData en build (ver abajo) | Borrar el fichero y seguir el flujo de emoji |

## Ciclo de vida del emoji cache

`emoji-cache.json` es una representación compacta de `emoji-data.json` (~100-150 KB vs ~1.25 MB) que permite un arranque instantáneo sin parsear el JSON raw. Se genera en runtime y se promueve al ensamblado mediante el flujo siguiente:

```
1. dotnet run (primera vez)
      └─ no hay embedded cache ni disco → parsea emoji-data.json → escribe AppData/.../emoji-cache.json

2. dotnet build (tras haber ejecutado la app al menos una vez)
      └─ target CopyEmojiCache: copia AppData/.../emoji-cache.json → Search/Emoji/emoji-cache.json
                                 (solo si destino no existe)
      └─ EmbeddedResource condicional: lo embute en el ensamblado

3. git add Search/Emoji/emoji-cache.json && git commit
      └─ el repo queda con el cache; futuros clones lo tienen desde el primer build

4. Arranque en producción
      └─ EmojiDataLoader encuentra el embedded cache → carga directa, sin tocar disco
```

El target `CopyEmojiCache` resuelve la ruta de AppData por plataforma. Ver el `.csproj` para las rutas exactas.

### Regenerar el cache de emojis

Si se actualiza `emoji-data.json` (borrándolo para que el target lo descargue de nuevo):

1. Borrar `Search/Emoji/emoji-cache.json` del repo.
2. Ejecutar la app una vez para que genere el nuevo cache en AppData.
3. Hacer build: el target lo copiará al source tree.
4. Commitear el nuevo `emoji-cache.json`.
