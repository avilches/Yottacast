# Navegador del usuario

Este documento describe el comportamiento esperado de la seleccion, resolucion y apertura del navegador web configurado por el usuario en Yottacast.

---

## 1. Proposito

El usuario puede elegir un navegador web preferido en la ventana de Settings. Ese navegador se utiliza para abrir URLs generadas por los resultados de tipo "Web Search" (busquedas en Google, DuckDuckGo, etc.). El sistema debe garantizar que siempre se use un navegador valido o, en su defecto, que la accion de abrir URL no produzca errores visibles.

---

## 2. Seleccion del navegador en Settings

Cuando el usuario abre la ventana de Settings, la aplicacion muestra un picker con los navegadores disponibles.

**Comportamiento esperado:**

- La lista solo contiene navegadores realmente instalados en el sistema.
- Para determinar si un navegador esta instalado, se busca en las carpetas de apps del usuario (`ExpandedAppDirectories`), luego en las carpetas por defecto de la plataforma (`DefaultAppDirectories`), y por ultimo en rutas conocidas de la plataforma (`BrowserKnownPaths`, solo relevante en Windows). Las carpetas duplicadas se saltan automaticamente. La verificacion usa `Directory.Exists` / `File.Exists` y no depende de la cache de `ApplicationSearch`.
- Los resultados del discovery se cachean en memoria. La cache se invalida al cambiar las carpetas de apps en Settings (diferido al salir de la seccion AppSearch o cerrar Settings).
- La lista se carga de forma lazy al abrir el dropdown del combo en Settings.

> **Verificar en:**
> - `Yottacast/ViewModels/SettingsWindowViewModel.cs` -- constructor, llamada a `browserDiscovery.Discover()`
> - `Yottacast.Core/Services/BrowserDiscovery.cs` -- metodo `Discover()`

---

## 3. Resolucion del navegador activo (auto-reparacion)

El navegador configurado puede dejar de existir (por ejemplo, si el usuario lo desinstala). El sistema se auto-repara de forma transparente.

**Invariantes:**

- Cada vez que se accede a `ActiveBrowser`, se verifica que el navegador guardado existe en disco.
- Si el navegador guardado ya no existe, se itera una lista ordenada de navegadores conocidos y se selecciona el primero que exista en disco.
- Cuando se produce un cambio automatico de navegador, el nuevo valor se persiste inmediatamente (llamada a `Save()`).
- Si ningun navegador conocido existe en disco, `ActiveBrowser` devuelve `null`.
- La resolucion no depende de la cache de `ApplicationSearch` ni de la cache de discovery: busca directamente en disco (carpetas del usuario, carpetas por defecto, rutas conocidas).
- `EnsureIntegrity()` fuerza la resolucion de navegador (y terminal) como efecto secundario; esta pensado para llamarse en puntos naturales como al abrir Settings.

**Logica de fallback de `Resolve`:**

| Situacion | Comportamiento |
|---|---|
| Nombre preferido vacio o nulo | Salta directamente al fallback: itera `KnownBrowserNames` en orden |
| Nombre preferido existe en disco | Devuelve ese navegador |
| Nombre preferido no existe en disco | Itera `KnownBrowserNames` completo (puede incluir de nuevo el nombre preferido si aparece en la lista) y devuelve el primero encontrado |
| Ningun navegador conocido existe | Devuelve `null` |

> **Verificar en:**
> - `Yottacast.Core/Services/UserSettings.cs` -- propiedad `ActiveBrowser`, metodo `EnsureIntegrity()`
> - `Yottacast.Core/Services/BrowserDiscovery.cs` -- metodo estatico `Resolve()`

---

## 4. Apertura de URL en el navegador

Cuando el usuario activa un resultado de tipo Web Search, se abre la URL construida en el navegador activo.

**Invariantes:**

- Si `ActiveBrowser` devuelve `null`, la accion retorna sin hacer nada. El usuario no ve un error ni una excepcion.
- Las excepciones durante el lanzamiento del proceso se capturan silenciosamente (`catch { }`). El usuario nunca ve un dialogo de error del sistema.
- El metodo `OpenUrl` de `BrowserDiscovery` delega en la implementacion de plataforma, pasando el nombre del navegador (no la ruta del ejecutable).

> **Verificar en:**
> - `Yottacast.Core/Search/WebSearch/WebSearchSource.cs` -- callback `OnActivate`
> - `Yottacast.Core/Services/BrowserDiscovery.cs` -- metodo `OpenUrl()`

---

## 5. Comportamiento por plataforma

### 5.1 macOS

| Aspecto | Comportamiento |
|---|---|
| Lista de navegadores conocidos | Safari, Google Chrome, Firefox, Brave Browser, Microsoft Edge, Opera, Arc, Vivaldi, Chromium, Tor Browser, DuckDuckGo, Orion |
| Busqueda | Via `AppPathInDirectory` en las carpetas del usuario y las por defecto (`/Applications`, `~/Applications`, `/System/Applications`, `/System/Applications/Utilities`) |
| Apertura de URL | Ejecuta `open -a <browserName> <url>`, delegando la resolucion del bundle al SO |

### 5.2 Windows

| Aspecto | Comportamiento |
|---|---|
| Lista de navegadores conocidos | Google Chrome, Mozilla Firefox, Microsoft Edge, Brave Browser, Opera, Vivaldi |
| Busqueda | Primero carpetas del usuario, luego `BrowserKnownPaths` con rutas absolutas a ejecutables conocidos |
| Apertura de URL | Resuelve la ruta del `.exe` desde `BrowserKnownPaths` en el momento de la llamada (`File.Exists`). Si no se encuentra, retorna silenciosamente. Lanza el exe con la URL como argumento |

**Diferencia de nombres:** En Windows, Firefox se llama `"Mozilla Firefox"` (nombre completo); en macOS es `"Firefox"`.

### 5.3 Linux

| Aspecto | Comportamiento |
|---|---|
| Lista de navegadores conocidos | Vacia |
| Busqueda | No hay navegadores conocidos |
| Apertura de URL | No-op (metodo vacio) |

> **Estado: incompleto (Linux)** - `LinuxPlatformProvider.KnownBrowserNames` esta vacio y `OpenUrl()` tiene cuerpo vacio. El descubrimiento devuelve siempre lista vacia y la auto-reparacion no puede operar: `ActiveBrowser` resuelve siempre a `null`, asi que activar un resultado Web Search no abre nada en Linux. Ver `Yottacast.Core/Platform/LinuxPlatformProvider.cs`.

> **Verificar en:**
> - `Yottacast.Core/Platform/MacOsPlatformProvider.cs` -- seccion Browser
> - `Yottacast.Core/Platform/WindowsPlatformProvider.cs` -- seccion Browser
> - `Yottacast.Core/Platform/LinuxPlatformProvider.cs` -- seccion Browser

---

## 6. Modelo de datos

`BrowserInfo` es un `record` con dos campos:

| Campo | Tipo | Descripcion |
|---|---|---|
| `Name` | `string` | Nombre de visualizacion (ej. `"Google Chrome"`) |
| `ExecutablePath` | `string` | Ruta completa en disco |

Este tipo es el valor de retorno de `Discover()`, `Resolve()` y la propiedad `ActiveBrowser`. La propiedad `UserSettings.Browser` solo almacena el `Name` (string), no el objeto completo.

> **Verificar en:**
> - `Yottacast.Core/Services/BrowserDiscovery.cs` -- declaracion del record `BrowserInfo`
> - `Yottacast.Core/Services/UserSettings.cs` -- propiedad `Browser` (string) y formato JSON (`SettingsData`)
