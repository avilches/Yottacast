# Rutas runtime y constantes centralizadas

## AppPaths (rutas de disco)

`Yottacast.Core/AppPaths.cs` es la fuente única de todas las rutas de fichero y directorio que la app lee o escribe en runtime. Cualquier componente que necesite una ruta de disco la obtiene de `AppPaths` en lugar de construirla localmente.

Define tres directorios base (`ConfigDir`, `LogDir`, `CacheDir`) y las rutas concretas derivadas de ellos (`SettingsFile`, `EmojiCacheFile`, `LogFilePattern`, `AppIconCacheDir`). Las rutas base usan `Environment.SpecialFolder` y siguen las convenciones de cada plataforma (macOS: `~/Library/...`, Windows: `%APPDATA%/...`).

## AppDefaults (constantes numéricas)

`Yottacast.Core/AppDefaults.cs` centraliza todos los valores por defecto y parámetros tunables: timeouts, límites de resultados, tamaños de grid, delays, etc. Cualquier constante numérica o string que controle comportamiento debe vivir aquí.

## Convención

Al añadir una nueva ruta de disco, definirla en `AppPaths`. Al añadir una constante o valor por defecto, definirla en `AppDefaults`. Los consumidores referencian estas clases — nunca hardcodean valores.

## user-data/ (acceso rápido para desarrollo)

El directorio `user-data/` en la raíz del proyecto contiene symlinks a los directorios runtime de la máquina local. Está gitignored y sirve para inspeccionar rápidamente los ficheros que la app escribe durante la ejecución. Ver `user-data/README.md` para detalles. Si los links se pierden, ejecutar `user-data/create-links.sh`.
