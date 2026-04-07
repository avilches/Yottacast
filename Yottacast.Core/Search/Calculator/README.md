# Calculator & Converter

Yottacast evalúa expresiones matemáticas y convierte unidades mientras escribes. El resultado se copia al portapapeles al pulsar Enter.

---

## Aritmética

| Escribes | Resultado |
|---|---|
| `2 + 2` | `4` |
| `15% * 200` | `30` |
| `sqrt(144)` | `12` |
| `(12 + 5) * 3` | `51` |
| `2^10` | `1024` |
| `log(1000)` | `3` |

---

## Conversiones de unidades

### Conversión automática (sin escribir destino)

Escribe `valor unidad` y el conversor elige el destino más útil — generalmente el par métrico↔imperial:

| Escribes | Resultado |
|---|---|
| `10 km` | `6.21 mi` |
| `5 miles` | `8.05 km` |
| `1.8 m` | `5.91 ft` |
| `6 ft` | `1.83 m` |
| `70 kg` | `154.32 lb` |
| `100 lb` | `45.36 kg` |
| `100 C` | `212 °F` |
| `72 F` | `22.22 °C` |
| `1 L` | `0.26 gallon` |
| `1 gallon` | `3.79 L` |
| `1 atm` | `1.01 bar` |
| `1 kW` | `1.34 hp` |

### Conversión explícita con `to` o `in`

Añade `to unidad` (o `in unidad`) para especificar el destino:

```
10 km to miles
100 F to C
50 lb in kg
90 deg to rad
1000 ml to cups
10 m2 to sqft
```

### Expresiones compuestas

Se puede combinar aritmética y conversión:

```
2 hours + 30 minutes to seconds
(5 km + 800 m) to miles
100 kg - 15 lb to kg
```

---

## Tiempo

Las unidades de tiempo largas se descomponen en componentes legibles:

| Escribes | Resultado |
|---|---|
| `38000 s` | `10 h 33 min 20 s` |
| `49 hours` | `2 day 1 h` |
| `2500 ms` | `2 s 500 ms` |
| `10 h` | `600 min` |
| `0.01 day` | `14 min 24 s` |
| `1000000 s` | `11 day 13 h 46 min 40 s` |

---

## Datos

| Escribes | Resultado |
|---|---|
| `1500 MB` | `1.5 GB` |
| `0.01 TB` | `10 GB` |
| `10 GB` | `0.01 TB` |
| `2048 kB` | `2.048 MB` |

---

## Divisas

Escribe una cantidad con código de moneda:

```
100 USD
50 EUR to GBP
200 USD to JPY
```

Los tipos de cambio se actualizan periódicamente.

---

## Categorías soportadas

| Categoría | Ejemplos de unidades |
|---|---|
| Longitud | `m`, `km`, `cm`, `mm`, `ft`, `in`, `yard`, `mi` |
| Masa | `kg`, `g`, `lb`, `oz`, `t` |
| Temperatura | `C`, `F`, `K` |
| Tiempo | `ms`, `s`, `min`, `h`, `day`, `week`, `year`, `decade` |
| Volumen | `L`, `mL`, `gallon`, `pint`, `cup`, `floz`, `tablespoon` |
| Área | `m2`, `sqft`, `hectare`, `acre`, `km2` |
| Presión | `Pa`, `psi`, `bar`, `atm`, `mmHg` |
| Fuerza | `N`, `lbf`, `kgf` |
| Energía | `J`, `kJ`, `Wh` |
| Potencia | `W`, `kW`, `hp` |
| Electricidad | `V`, `A`, `ohm`, `W`, `F`, `H`, `T` |
| Ángulo | `deg`, `rad`, `grad`, `arcmin`, `arcsec` |
| Datos | `B`, `kB`, `MB`, `GB`, `TB` |

---

## Escritura flexible

El conversor acepta múltiples formas para la misma unidad:

- **Mayúsculas/minúsculas**: `km`, `KM`, `Km` → lo mismo
- **Formas plurales**: `10 miles`, `5 pounds`, `100 degrees`
- **Nombres completos**: `10 kilometers`, `100 fahrenheit`, `5 liters`, `3 tablespoons`
- **Símbolos de temperatura**: `100°C`, `212°F`

---

## Avisos de ambigüedad

Algunos tokens tienen varios significados posibles (por ejemplo, `mg` puede ser miligramo o megagramo). En ese caso el conversor muestra una advertencia con los candidatos junto al resultado para que puedas confirmarlo.
