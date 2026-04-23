# Logging

## Objetivo

Yottacast registra un diario de actividad que permite diagnosticar problemas sin necesidad de reproducirlos. El sistema
de logging cumple estos contratos:

1. **Todo error recuperable queda registrado.** Si la aplicacion continua funcionando tras un fallo (red, disco,
   parseo), el fichero de log debe contener al menos un `Warning` con el mensaje de la excepcion.
2. **Los eventos relevantes del ciclo de vida quedan registrados.** Arranque, migraciones de version, escaneo de
   aplicaciones, carga de datos y aplicacion de temas emiten `Information`.
3. **Las operaciones frecuentes de busqueda se registran como `Debug`**, para no saturar el log en uso normal pero estar
   disponibles cuando se sube el nivel.
4. **El usuario nunca ve los logs.** Los logs van a fichero; no se muestran mensajes de log en la interfaz grafica.

> **Verificar en:** `App.axaml.cs` (metodo `BuildServices`).

---

## Destinos y formato

| Destino          | Rotacion                    | Plantilla                                                                                  |
|------------------|-----------------------------|--------------------------------------------------------------------------------------------|
| Fichero en disco | Diaria, 7 dias de retencion | `{Timestamp:HH:mm:ss.fff} [{Level:u5}] [{SourceContext}] {Message:lj}{NewLine}{Exception}` |

**Rutas del fichero de log (GUI):**

| Plataforma      | Ruta                                                           |
|-----------------|----------------------------------------------------------------|
| macOS           | `~/Library/Logs/Yottacast/yottacast-YYYYMMDD.log`              |
| Windows / Linux | `{LocalApplicationData}/Yottacast/Logs/yottacast-YYYYMMDD.log` |

El campo `SourceContext` se rellena automaticamente con el nombre de la clase generica (`ILogger<T>`), identificando el
origen de cada linea sin esfuerzo manual.

> **Verificar en:** `AppPaths.cs` (propiedades `LogDir`, `LogFilePattern`), `App.axaml.cs` (metodo `BuildServices` --
> configuracion de Serilog).