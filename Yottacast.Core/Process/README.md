# Yottacast

## Por qué se usa PTY para lanzar procesos

Los servicios que buscan ficheros (`FileSearch`) o ejecutan binarios (`PtyRunner`) necesitan
recibir las líneas de salida **en tiempo real** para poder cancelar el proceso en cuanto se
alcanza el número de resultados deseado.

### El problema con stdout redirigido

Cuando un proceso detecta que su stdout **no** es un terminal (tty) — lo que ocurre siempre
que se usa `Process.RedirectStandardOutput = true` — el runtime de C (y la mayoría de
herramientas Unix) activa el **block-buffering**: acumula la salida en un buffer interno de
~4 KB y solo la entrega al lector cuando ese buffer se llena o el proceso termina.

Consecuencia directa: el callback `Func<string, bool> onLine` recibe las líneas tarde
(cuando el buffer se vacía) o directamente después de que el proceso ya ha terminado.
Devolver `false` para "parar" el proceso en ese momento no tiene ningún efecto útil —
el trabajo ya está hecho.

### La solución: PTY (pseudo-terminal)

Un PTY crea un par maestro/esclavo que emula un terminal real. El proceso hijo escribe
en el esclavo creyendo que hay un usuario al otro lado, lo que fuerza el **line-buffering**:
cada línea se entrega inmediatamente al maestro en cuanto se escribe el `\n`.

Esto permite:

- Recibir cada resultado en cuanto el proceso lo produce.
- Llamar a `onLine` con cada línea de forma incremental.
- Matar el proceso (`pty.Kill()`) inmediatamente cuando `onLine` devuelve `false`,
  antes de que el proceso siga ejecutándose y consumiendo recursos.

### La librería PTY elegida: `vs-pty.net`

Se usa la librería oficial de Microsoft
[`microsoft/vs-pty.net`](https://github.com/microsoft/vs-pty.net) en lugar del paquete
NuGet [`PTY` v1.0.3](https://www.nuget.org/packages/PTY) (autor: gsw945, basado en el
mismo código).

**Motivo:** El paquete NuGet `PTY 1.0.3` contiene debug logs olvidados en el código
fuente que se imprimen directamente en `Console.Out` desde un hilo de fondo:

```csharp
// Pty.Net/Unix/PtyConnection.cs — PtyTerminal (gsw945)
private void ChildWatcherThreadProc()
{
    Console.WriteLine($"Waiting on {this.pid}");   // ← debug olvidado
    // ...
    Console.WriteLine($"Wait succeeded");           // ← debug olvidado
}
```

Estos mensajes aparecen mezclados con la salida normal de la aplicación de forma
asíncrona (el segundo incluso puede aparecer después del siguiente prompt interactivo)
porque el hilo que llama a `waitpid` sigue vivo tras retornar `RunAsync`. No hay ninguna
API pública del paquete que permita suprimirlos.

`vs-pty.net` usa `System.Diagnostics.TraceSource` para todo su logging interno, por lo
que no imprime nada en consola salvo que el consumidor configure explícitamente un
`TraceListener`.
