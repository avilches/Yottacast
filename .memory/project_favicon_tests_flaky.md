---
name: project_favicon_tests_flaky
description: Algunos FaviconCacheTests son flaky en la suite completa; pasan en aislamiento. No perseguirlos.
metadata: 
  node_type: memory
  type: project
  originSessionId: 18113fc9-8230-443a-b9ab-7fc2ba2332ba
---

En `Yottacast.Core.Tests`, varios tests de `FaviconCacheTests` (p. ej. `Stop_ClearsMemory`,
`GetOrLoad_SameHostTwice_OnlyOneFetch`, `FaviconLoaded_FiredAfterLoad`, `GetOrLoad_DiskWriteFails_StillServesFromMemory`)
son **flaky** al correr la suite completa con `dotnet test`: fallan de forma intermitente (1-5 por corrida, varían) por
timing de red/disco bajo ejecución paralela (~3 s, timeouts). En aislamiento
(`dotnet test --filter "FullyQualifiedName~FaviconCacheTests"`) pasan los 7 de forma consistente.

Mismo patrón observado en `MacOsPlatformProviderTests.CreateCommandScript_LeavesNoOrphanTmpFile`: falla
intermitente en la suite completa, pasa aislado. Tratarlo como el mismo flaky de concurrencia.

**Why:** no es una regresión. Confirmado repetidamente al verificar cambios de Core/IPC no relacionados (T1/T2/T3/T5 del
plan de velocidad). Confunde porque el contador de fallos cambia entre corridas.

**How to apply:** si la suite completa marca fallos solo en `FaviconCacheTests`, reconfirmar en aislamiento antes de
sospechar de tus cambios; si pasan aislados, son el flaky conocido. No invertir tiempo en buscar la causa en cambios
ajenos a FaviconCache. (Estabilizarlos sería un item aparte.)
