math.createUnit('USD');
var _currencySet = new Set(['USD']);
var _cryptoCurrencySet = new Set();

// Velocidad: mph y kmh como unidades simples
math.createUnit('kmh', { definition: math.unit(1000/3600, 'm/s') });
math.createUnit('mph', { definition: math.unit(1609.344/3600, 'm/s') });

// Frecuencia/rotación: rpm ↔ Hz (dimensión 1/s)
math.createUnit('rpm', { definition: math.unit(1/60, '1/s') });

// Tasas de datos: registradas individualmente para nombres exactos
math.createUnit('bps',  { definition: '1 bit / s' });
math.createUnit('kbps', { definition: '1000 bps' });
math.createUnit('Mbps', { definition: '1000 kbps' });
math.createUnit('Gbps', { definition: '1000 Mbps' });
math.createUnit('Tbps', { definition: '1000 Gbps' });

function registerCurrency(name, rateVsUSD, isCrypto) {
    // rateVsUSD: units of 'name' per 1 USD (e.g. EUR=0.92 means 1 USD = 0.92 EUR)
    _currencySet.add(name);
    if (isCrypto) _cryptoCurrencySet.add(name);
    math.createUnit(name, { definition: (1 / rateVsUSD) + ' USD' }, { override: true });
}

// Pre-computed maps injected by loadPrecomputedData() before any expression is evaluated.
// These are produced by mathjs-precompute.js and embedded as a resource in the assembly.
// See MathJsDataGenerator.GenerateData() in Yottacast.Core.Tests to regenerate them.
var _mathFunctionNames = null;
// lowercase → canonical for non-ambiguous tokens (built at load time from data.symbols)
var _unitSymbols       = null;
// lowercase → [{symbol, longName}], only for tokens with >1 canonical form
var _unitAmbiguousMap  = null;
// longForm → shortForm: maps long-form unit symbols to their short canonical equivalent.
// E.g. "centimeter" → "cm", "kilometer" → "km". Populated by loadPrecomputedData().
var _longToShort       = {};

// Overrides manuales: forzar un mapeo específico independientemente del análisis automático.
// Añade aquí cualquier caso especial que quieras permitir o bloquear.
var _unitOverrides = {
    // 'MG': 'Mg',   // ejemplo: forzar MG → megagramo en vez de dejar ambiguo
};

// Overrides de desambiguación: cuando un token no coincide exactamente con ningún canónico,
// define qué canónico usar en vez del primero de la lista de candidatos.
// Clave: token en minúsculas. Valor: símbolo canónico preferido.
// Cargado desde ambiguityOverrides en unit-config.json vía loadAliasData().
var _ambiguityOverrides = {};

// Overrides forzados para canónicos exactos: cuando el usuario escribe exactamente un símbolo
// canónico válido pero queremos que se resuelva a otro preferido con aviso de ambigüedad.
// A diferencia de _ambiguityOverrides (clave en minúsculas), aquí la clave es case-sensitive
// para poder distinguir "mS" (millisiemens) de "Ms" (megasegundo).
// Cargado desde forceAmbiguous en unit-config.json vía loadAliasData().
var _forceAmbiguous = {};

// Set of blocked unit symbols (lowercase). Populated by loadAliasData().
var _blockedUnits = new Set();

// Unit-specific default conversion targets. Populated by loadAliasData().
// Checked before _defaultUnitPairs to avoid dimensional collisions between same-dimension units.
var _defaultUnitTargets = {};

// Explicit long names for units where getUnitLongName() cannot derive them automatically
// (e.g. time units that use NONE prefix group in math.js). Populated by loadAliasData().
var _longNames = {};

// Maps canonical unit symbols to eval-safe equivalents for use in math.evaluate() expressions.
// Needed when a unit symbol shares its name with a math.js function (e.g. "min" = math.min).
// Applied in the AST traverse in normalizeExpression, after resolveUnitToken.
var _evalSafeAliases = {};

// Default pairs for physical unit auto-conversion.
// Each pair [A, B]: if the user types a value in A, suggest B, and vice-versa.
// Used as fallback when _defaultUnitTargets has no entry for the resolved unit.
// Pairs use the SI base unit ↔ Imperial base unit for each dimension.
// Populated from unit-config.json defaultPairs by loadAliasData().
var _defaultUnitPairs = [];

// Default currency pair: if the user types a single currency, suggest the other.
var _defaultCurrencyPair = ['EUR', 'USD'];

// Set of units that get normalize decomposition behavior. Populated by loadAliasData().
var _normalizeUnits = new Set();

// Normalize chains: define how to decompose a value into human-readable components.
// Each chain entry: { unit, display, longName, factorInBase }
// factorInBase: how many base units equal 1 of this unit.
var _normalizeChains = {
    time: {
        baseUnit: 's',
        chain: [
            { unit: 'year',   display: 'year', longName: 'year',        factorInBase: 31557600 },
            { unit: 'day',    display: 'day',  longName: 'day',         factorInBase: 86400 },
            { unit: 'h',      display: 'h',    longName: 'hour',        factorInBase: 3600 },
            { unit: 'minute', display: 'min',  longName: 'minute',      factorInBase: 60 },
            { unit: 's',      display: 's',    longName: 'second',      factorInBase: 1 },
            { unit: 'ms',     display: 'ms',   longName: 'millisecond', factorInBase: 0.001 },
        ]
    },
    data: {
        baseUnit: 'B',
        mode: 'best_unit',   // single best unit (e.g. 1500 MB → 1.5 GB), not multi-component
        maxDecimals: 3,
        chain: [
            { unit: 'TB', display: 'TB', longName: 'terabyte',  factorInBase: 1e12 },
            { unit: 'GB', display: 'GB', longName: 'gigabyte',  factorInBase: 1e9 },
            { unit: 'MB', display: 'MB', longName: 'megabyte',  factorInBase: 1e6 },
            { unit: 'kB', display: 'kB', longName: 'kilobyte',  factorInBase: 1e3 },
            { unit: 'B',  display: 'B',  longName: 'byte',      factorInBase: 1 },
        ]
    }
};

// Loads alias/blocked configuration from unit-config.json.
// Must be called after loadPrecomputedData().
function loadAliasData(data) {
    // Merge tokenAliases into _unitOverrides
    if (data.tokenAliases) {
        Object.keys(data.tokenAliases).forEach(function(k) {
            _unitOverrides[k] = data.tokenAliases[k];
        });
    }
    // Populate blocked set
    if (data.blocked) {
        data.blocked.forEach(function(sym) { _blockedUnits.add(sym.toLowerCase()); });
    }
    // Populate unit-specific default targets
    if (data.defaultTargets) {
        Object.keys(data.defaultTargets).forEach(function(k) {
            _defaultUnitTargets[k] = data.defaultTargets[k];
        });
    }
    // Populate dimensional fallback pairs (any unit dimensionally compatible with pair[0] → pair[0] or pair[1])
    if (data.defaultPairs) {
        data.defaultPairs.forEach(function(pair) { _defaultUnitPairs.push(pair); });
    }
    // Populate eval-safe aliases (e.g. min → minute to avoid conflict with math.min function)
    if (data.evalSafeAliases) {
        Object.keys(data.evalSafeAliases).forEach(function(k) {
            _evalSafeAliases[k] = data.evalSafeAliases[k];
        });
    }
    // Populate ambiguity overrides: when a token is not an exact canonical, use the configured
    // preferred canonical instead of always defaulting to candidates[0].
    if (data.ambiguityOverrides) {
        Object.keys(data.ambiguityOverrides).forEach(function(k) {
            _ambiguityOverrides[k.toLowerCase()] = data.ambiguityOverrides[k];
        });
    }
    // Populate force-ambiguous overrides: exact-case keys for canonical symbols that should
    // still show ambiguity and resolve to a different preferred canonical.
    if (data.forceAmbiguous) {
        Object.keys(data.forceAmbiguous).forEach(function(k) {
            _forceAmbiguous[k] = data.forceAmbiguous[k];
        });
    }
    // Populate explicit long names for units not derivable via LONG prefix group (e.g. time units)
    if (data.normalizeUnits) {
        data.normalizeUnits.forEach(function(u) { _normalizeUnits.add(u); });
    }
    if (data.longNames) {
        Object.keys(data.longNames).forEach(function(k) {
            _longNames[k] = data.longNames[k];
        });
        // Auto-build reverse mappings: long name → canonical short symbol.
        // E.g. longNames["h"]="hour" → _unitOverrides["hour"]="h", so "10 hour" normalizes to "10 h".
        // Skips entries where the key and long name are equal (canonical already is the long form, e.g. "minute":"minute").
        // Does not overwrite explicit tokenAliases (those take precedence).
        Object.keys(data.longNames).forEach(function(k) {
            var longName = data.longNames[k];
            if (longName && longName !== k && _unitOverrides[longName] === undefined) {
                _unitOverrides[longName] = k;
            }
        });
    }
}

// Injects pre-computed maps produced by mathjs-precompute.js.
// Must be called before any expression evaluation.
function loadPrecomputedData(data) {
    _unitAmbiguousMap  = data.ambiguous;
    _mathFunctionNames = data.functionNames;
    _longToShort       = data.longToShort || {};
    // Build lowercase → canonical lookup for non-ambiguous tokens
    _unitSymbols = {};
    data.symbols.forEach(function(sym) {
        var lower = sym.toLowerCase();
        if (!_unitAmbiguousMap[lower]) _unitSymbols[lower] = sym;
    });
}

// Devuelve un objeto {resolved, ambiguous, candidates?} para el token de unidad dado,
// o null si el token no se reconoce como unidad ni como override.
//   resolved   — forma canónica a usar
//   ambiguous  — true si hay múltiples candidatos y el input no es un match exacto
//   candidates — lista de {symbol, longName} (solo presente cuando ambiguous=true)
function resolveUnitToken(name) {
    if (_blockedUnits.has(name.toLowerCase())) return null;
    const override = _unitOverrides[name];
    if (override !== undefined) return { resolved: override, ambiguous: false };
    // Multi-char tokens: also try the lowercase key so that "Hour", "HOUR" etc. resolve the same as "hour".
    // Single-char tokens are intentionally case-sensitive: "c"→degC but "C"→Coulomb, "f"→degF but "F"→Farad.
    if (name.length > 1) {
        const lowerOverride = _unitOverrides[name.toLowerCase()];
        if (lowerOverride !== undefined) return { resolved: lowerOverride, ambiguous: false };
    }
    const lower = name.toLowerCase();

    const candidates = _unitAmbiguousMap[lower]
        ? _unitAmbiguousMap[lower].filter(function(c) { return !_blockedUnits.has(c.symbol.toLowerCase()); })
        : null;
    if (candidates && candidates.length > 0) {
        // Todos los candidatos comparten el mismo long name (sinónimos) → normalizar al primero
        // Este check va primero para que "l" y "L" (ambos litro) se normalicen siempre a "L"
        if (candidates.every(function(c) { return c.longName === candidates[0].longName; }))
            return { resolved: candidates[0].symbol, ambiguous: false };
        // Input ya es exactamente uno de los canónicos.
        // Caso normal → sin ambigüedad. Caso forceAmbiguous → se resuelve al preferred con aviso.
        // forceAmbiguous es case-sensitive para distinguir "mS" (millisiemens) de "Ms" (megasegundo).
        if (candidates.some(function(c) { return c.symbol === name; })) {
            var forcePreferred = _forceAmbiguous[name];
            if (forcePreferred !== undefined) {
                var forceIdx = -1;
                for (var fi = 0; fi < candidates.length; fi++) {
                    if (candidates[fi].symbol === forcePreferred) { forceIdx = fi; break; }
                }
                if (forceIdx >= 0) {
                    var forceReordered = [candidates[forceIdx]];
                    for (var fj = 0; fj < candidates.length; fj++) {
                        if (fj !== forceIdx) forceReordered.push(candidates[fj]);
                    }
                    return { resolved: forcePreferred, ambiguous: true, candidates: forceReordered };
                }
            }
            return { resolved: name, ambiguous: false };
        }
        // Override configurado: resolver al símbolo preferido y marcar como ambiguo con candidatos
        // reordenados (preferred primero) para que BuildHints muestre las alternativas al usuario.
        var preferred = _ambiguityOverrides[lower];
        if (preferred !== undefined) {
            var preferredIdx = -1;
            for (var i = 0; i < candidates.length; i++) {
                if (candidates[i].symbol === preferred) { preferredIdx = i; break; }
            }
            if (preferredIdx >= 0) {
                var reordered = [candidates[preferredIdx]];
                for (var j = 0; j < candidates.length; j++) {
                    if (j !== preferredIdx) reordered.push(candidates[j]);
                }
                return { resolved: preferred, ambiguous: true, candidates: reordered };
            }
        }
        return { resolved: candidates[0].symbol, ambiguous: true, candidates: candidates };
    }

    // Token no ambiguo: el mapa _unitSymbols da la forma canónica directamente.
    // Si hay un equivalente corto en _longToShort, se usa como canónico único.
    const canonical = _unitSymbols[lower];
    if (canonical !== undefined) {
        var short = _longToShort[canonical];
        return { resolved: short !== undefined ? short : canonical, ambiguous: false };
    }
    return null;
}

// Detecta el patrón AST para unidades compuestas: número × unidadNum / unidadDen
// Ejemplo: "10 km/h" → OperatorNode('/') con OperatorNode('*', implicit) como numerador
function _isCompoundUnitEntry(node) {
    return node.type === 'OperatorNode' && node.op === '/' && !node.implicit &&
           node.args.length === 2 &&
           node.args[0].type === 'OperatorNode' && node.args[0].implicit === true &&
           node.args[0].args.length === 2 &&
           node.args[0].args[0].type === 'ConstantNode' &&
           node.args[0].args[1].type === 'SymbolNode' &&
           node.args[1].type === 'SymbolNode';
}

// Devuelve la unidad de destino por defecto para una unidad dada, o null si no hay par.
// Para monedas usa _defaultCurrencyPair; para unidades físicas primero _defaultUnitTargets
// (mapa concreto, mayor prioridad) y luego _defaultUnitPairs (matching dimensional, fallback).
function findDefaultTarget(resolvedUnit, knownCurrencies) {
    // Currency
    if (knownCurrencies.has(resolvedUnit)) {
        for (var i = 0; i < _defaultCurrencyPair.length; i++) {
            var c = _defaultCurrencyPair[i];
            if (c !== resolvedUnit) return c;
        }
        return null;
    }
    // Unit-specific target (highest priority — avoids dimensional collision)
    if (_defaultUnitTargets[resolvedUnit] !== undefined)
        return _defaultUnitTargets[resolvedUnit];
    // Physical unit: check dimensional compatibility
    for (var j = 0; j < _defaultUnitPairs.length; j++) {
        var pair = _defaultUnitPairs[j];
        try {
            var base = math.Unit.parse('1 ' + resolvedUnit);
            if (base.equalBase(math.Unit.parse('1 ' + pair[0]))) {
                return pair[0] === resolvedUnit ? pair[1] : pair[0];
            }
        } catch(e) {}
    }
    return null;
}

// Parses the expression into an AST, cleans it (block/assignment nodes), normalizes
// currency and unit casing, fixes function casing, detects ambiguous unit tokens, and
// determines the expression kind (calculation, unit_entry, simple_conversion, complex_conversion).
// knownCurrenciesCsv is a comma-separated list of uppercase ISO codes (e.g. "USD,EUR,GBP").
// Returns { expr, kind, fromUnit, toUnit, leftExpr, currencies, ambiguities } or null for
// FunctionAssignmentNode (those produce no result).
// Throws if the expression is syntactically invalid — the caller should treat that as no result.
function normalizeExpression(expression, knownCurrenciesCsv) {
    // Normalize 'to'/'in' keywords to lowercase before parsing — the math.js parser is
    // case-sensitive and rejects TO/IN as unknown identifiers.
    expression = expression.replace(/\bto\b/gi, 'to');
    expression = expression.replace(/\bin\b/gi, 'in');

    const knownCurrencies = new Set(knownCurrenciesCsv.split(','));
    const currencies = [];
    const ambiguities = [];
    const seenAmbiguous = {};

    // ── AST cleanup ──────────────────────────────────────────────────────────────
    let root = math.parse(expression);
    // Multi-statement block: keep only the first statement
    if (root.type === 'BlockNode') root = root.blocks[0].node;
    // Function definitions produce no value
    if (root.type === 'FunctionAssignmentNode') return null;
    // Strip assignment nodes (e.g. "10 + (a = 2)") — keep the value side
    root = root.transform(function(n) {
        return n.type === 'AssignmentNode' ? n.value : n;
    });

    // ── Traverse: normalize currencies, unit casing, function names ───────────
    root.traverse(function(n) {
        if (n.type === 'SymbolNode') {
            var originalName = n.name;
            var upper = originalName.toUpperCase();
            var lower = originalName.toLowerCase();

            // Currencies → uppercase
            if (knownCurrencies.has(upper)) {
                n.name = upper;
                if (currencies.indexOf(upper) < 0) currencies.push(upper);
                return;
            }

            // Unit token resolution (with ambiguity detection)
            var resolution = resolveUnitToken(originalName);
            if (resolution !== null) {
                var resolved = resolution.resolved;
                n.name = (_evalSafeAliases[resolved] !== undefined) ? _evalSafeAliases[resolved] : resolved;
                if (resolution.ambiguous && !seenAmbiguous[lower]) {
                    seenAmbiguous[lower] = true;
                    // resolution.candidates already contains [{symbol, longName}] from precomputed data
                    ambiguities.push({ input: originalName, candidates: resolution.candidates });
                }
                return;
            }

            // Function names → canonical casing.
            // In math.js, the fn of a FunctionNode is itself a SymbolNode, so the traverse visits it here.
            if (_mathFunctionNames[lower]) {
                n.name = _mathFunctionNames[lower];
            }
        }
    });

    // ── Determine expression kind ─────────────────────────────────────────────
    let kind;
    let fromUnit = null, toUnit = null, leftExpr = null;

    if (root.type === 'OperatorNode' && root.op === 'to') {
        var left = root.args[0];
        var right = root.args[1];
        toUnit = right.type === 'SymbolNode' ? right.name : null;
        if (left.type === 'OperatorNode' && left.implicit === true &&
            left.args.length === 2 && left.args[1].type === 'SymbolNode') {
            kind = 'simple_conversion';
            fromUnit = left.args[1].name;
        } else {
            kind = 'complex_conversion';
            leftExpr = left.toString();
            // Extract compound fromUnit when LHS is "number × unit / unit" (e.g. "10 mi/s")
            if (_isCompoundUnitEntry(left)) {
                fromUnit = left.args[0].args[1].name + ' / ' + left.args[1].name;
            }
        }
    } else if (root.type === 'OperatorNode' && root.implicit === true &&
               root.args.length === 2 && root.args[1].type === 'SymbolNode') {
        // Reject when LHS is a bare unit symbol (e.g. "day km" from "d km") — not a valid entry
        if (root.args[0].type === 'SymbolNode' && resolveUnitToken(root.args[0].name) !== null) {
            return null;
        }
        var unitName = root.args[1].name;
        var defaultTarget = findDefaultTarget(unitName, knownCurrencies);
        if (defaultTarget !== null) {
            kind = 'unit_entry';
            fromUnit = unitName;
            toUnit = defaultTarget;
        } else {
            kind = 'calculation';
        }
    } else if (_isCompoundUnitEntry(root)) {
        var numUnit = root.args[0].args[1].name;
        var denUnit = root.args[1].name;
        var compoundUnit = numUnit + ' / ' + denUnit;
        var defaultTarget = findDefaultTarget(compoundUnit, knownCurrencies);
        if (defaultTarget !== null) {
            kind = 'unit_entry';
            fromUnit = compoundUnit;
            toUnit = defaultTarget;
        } else {
            kind = 'calculation';
        }
    } else if (root.type === 'ConstantNode') {
        // Bare number literal with no operator or unit (e.g. "2", "2.0") — no calculation performed
        return null;
    } else if (root.type === 'SymbolNode' && resolveUnitToken(root.name) !== null) {
        // Bare unit symbol with no value (e.g. "j", "m", "s") — not a meaningful expression
        return null;
    } else if (root.type === 'SymbolNode' && _mathFunctionNames[root.name.toLowerCase()]) {
        // Bare function reference with no arguments (e.g. "sin", "sqrt") — evaluates to "function", not useful
        return null;
    } else {
        kind = 'calculation';
    }

    // ── Build final expression ────────────────────────────────────────────────
    let normalizedExpr = root.toString();
    if (kind === 'unit_entry') {
        normalizedExpr = normalizedExpr + ' to ' + toUnit;
        if (knownCurrencies.has(toUnit) && currencies.indexOf(toUnit) < 0) currencies.push(toUnit);
    }

    return {
        expr: normalizedExpr,
        kind: kind,
        fromUnit: fromUnit,
        toUnit: toUnit,
        leftExpr: leftExpr,
        currencies: currencies,
        ambiguities: ambiguities
    };
}

// Computes a human-readable long name for a canonical math.js unit symbol.
// E.g. "km" → "kilometer", "mg" → "milligram", "Mg" → "megagram".
// Checks _longNames first (explicit overrides from unit-config.json), then searches
// math.Unit.UNITS for a matching entry with LONG prefix group.
// Falls back to the symbol itself if decomposition fails or no LONG prefix group is found.
function getUnitLongName(symbol) {
    if (_longNames[symbol] !== undefined) return _longNames[symbol];
    try {
        const parsed = math.Unit.parse('1 ' + symbol);
        if (!parsed.units || parsed.units.length !== 1) return symbol;
        const unitPart = parsed.units[0];
        const prefix   = unitPart.prefix;
        const baseUnit = unitPart.unit;

        // Find the long prefix name by matching the prefix value in PREFIXES.LONG
        let longPrefixName = null;
        if (prefix && prefix.value !== 1) {
            const longPrefixes = math.Unit.PREFIXES.LONG || {};
            Object.keys(longPrefixes).forEach(function(pk) {
                if (longPrefixes[pk].value === prefix.value && longPrefixName === null) {
                    longPrefixName = longPrefixes[pk].name;
                }
            });
            if (longPrefixName === null) longPrefixName = prefix.name;
        }
        if (longPrefixName === null) longPrefixName = '';

        // Find the long unit name: a UNITS entry with LONG prefixes and same value/dimensions/offset
        let longUnitName = null;
        const longPrefixGroup = math.Unit.PREFIXES.LONG;
        const baseOffset = baseUnit.offset || 0;
        Object.keys(math.Unit.UNITS).forEach(function(name) {
            if (longUnitName) return;
            const u = math.Unit.UNITS[name];
            if (u.prefixes !== longPrefixGroup) return;
            const relDiff = Math.abs(u.value - baseUnit.value) / (Math.abs(baseUnit.value) || 1);
            if (relDiff > 1e-12) return;
            const dimA = u.dimensions;
            const dimB = baseUnit.dimensions;
            if (!dimA || !dimB || dimA.length !== dimB.length) return;
            if (!dimA.every(function(d, i) { return d === dimB[i]; })) return;
            if (Math.abs((u.offset || 0) - baseOffset) > 1e-9) return;
            longUnitName = name;
        });

        if (!longUnitName) return symbol;
        return longPrefixName + longUnitName;
    } catch (e) {
        return symbol;
    }
}

// Returns the explicit long name from _longNames for the given symbol, or empty string if not set.
// Unlike getUnitLongName, this never falls back to the symbol itself.
function getExplicitLongName(symbol) {
    var v = _longNames[symbol];
    return (v !== undefined) ? v : '';
}

// Formats a math.js result value as a string with smart decimal precision:
// - Numbers with an integer part (|x| >= 1): rounded to _FMT_LARGE_DECIMALS decimal places.
// - Numbers < 1: limited to _FMT_SMALL_SIG_FIGS significant figures.
// - Integers (no decimal point): unchanged.
// Examples: 6.213711922 mi → "6.21 mi", 0.001450377377 psi → "0.00145 psi", 600 min → "600 min"
function smartFormat(r) {
    var s = math.format(r, {precision: _FMT_BASE_PRECISION});
    // Convert scientific notation to fixed when the result is readable (up to 10 digits).
    // e.g. "3.2808399e+7 ft" → "32808399 ft", but "1e+18 N" stays as-is.
    var sci = /^(-?\d+\.?\d*)[eE]\+(\d+)(\s.*)?$/.exec(s);
    if (sci) {
        var exp = parseInt(sci[2], 10);
        var mantissa = sci[1].replace('-', '').replace('.', '');
        var totalDigits = mantissa.length + exp - (sci[1].indexOf('.') >= 0 ? mantissa.length - 1 : 0);
        // Only for integer mantissa: totalDigits = intDigits + exp
        var dotPos = sci[1].indexOf('.');
        var intDigits = dotPos >= 0 ? dotPos - (sci[1][0] === '-' ? 1 : 0) : sci[1].replace('-','').length;
        var mantissaDecimals = dotPos >= 0 ? sci[1].length - dotPos - 1 : 0;
        var resultDigits = intDigits + exp;
        if (resultDigits <= 10) {
            var num = Number(sci[1] + 'e+' + sci[2]);
            var suffix = sci[3] || '';
            var decimals = Math.max(0, mantissaDecimals - exp);
            s = num.toFixed(decimals) + suffix;
        }
    }
    // Convert negative-exponent scientific notation to fixed decimal for currency amounts.
    // Physical units like "1e-9 W" or "1e-6 kg" keep scientific notation; only currencies
    // (e.g. "8.1e-8 USD", "1.23e-7 SHIB") are converted to fixed point.
    // After conversion, _FMT_SMALL_SIG_FIGS trimming applies in the decimal branch below.
    var sciNeg = /^(-?\d+\.?\d*)[eE]-(\d+)(\s.*)?$/.exec(s);
    if (sciNeg && _currencySet.has((sciNeg[3] || '').trim())) {
        var expN = parseInt(sciNeg[2], 10);
        var dotN  = sciNeg[1].indexOf('.');
        var mDecN = dotN >= 0 ? sciNeg[1].length - dotN - 1 : 0;
        var numN  = parseFloat(sciNeg[1] + 'e-' + sciNeg[2]);
        s = numN.toFixed(Math.min(expN + mDecN, 20)) + (sciNeg[3] || '');
    }
    // Only post-process strings with a plain decimal number (no scientific notation).
    // The pattern requires digits.digits followed immediately by whitespace or end of string.
    var m = /^(-?\d+\.\d+)(\s|$)/.exec(s);
    if (!m) return s;
    var n = parseFloat(m[1]);
    var suffix = s.slice(m[1].length).trim();
    var numStr;
    if (Math.abs(n) < 1) {
        if (_cryptoCurrencySet.has(suffix)) {
            // Crypto < 1: always _FMT_CRYPTO_DECIMALS decimal places (e.g. 0.00000001 BTC)
            numStr = n.toFixed(_FMT_CRYPTO_DECIMALS);
        } else {
            // FIAT and non-currency small values: significant figures (e.g. 0.000042 USD)
            var magnitude = Math.floor(Math.log10(Math.abs(n)));
            var decimalPlaces = _FMT_SMALL_SIG_FIGS - 1 - magnitude;
            var factor = Math.pow(10, decimalPlaces);
            var rounded = Math.round(n * factor) / factor;
            numStr = rounded.toFixed(decimalPlaces).replace(/0+$/, '').replace(/\.$/, '');
        }
    } else {
        var effDecimals = _cryptoCurrencySet.has(suffix) ? _FMT_CRYPTO_DECIMALS
                        : _currencySet.has(suffix) ? _FMT_FIAT_DECIMALS
                        : _FMT_LARGE_DECIMALS;
        var factor = Math.pow(10, effDecimals);
        var rounded = Math.round(n * factor) / factor;
        if (_currencySet.has(suffix)) {
            // Currencies always show fixed decimal places (no trailing-zero stripping)
            numStr = rounded.toFixed(effDecimals);
        } else {
            numStr = (rounded === Math.floor(rounded))
                ? rounded.toString()
                : rounded.toFixed(effDecimals).replace(/0+$/, '');
        }
    }
    return numStr + s.slice(m[1].length);
}

// Formats a number with at most maxDec decimal places, stripping trailing zeros.
// Examples: formatMaxDec(1.5, 3) → "1.5", formatMaxDec(1.024, 3) → "1.024"
function formatMaxDec(value, maxDec) {
    var factor = Math.pow(10, maxDec);
    var rounded = Math.round(value * factor) / factor;
    if (rounded === Math.floor(rounded)) return rounded.toString();
    return rounded.toFixed(maxDec).replace(/0+$/, '');
}

// Decomposes a value in the given unit into human-readable components.
// Returns an array of { value, unit, display, longName } or null if not supported.
// Two modes (set per chain via chainConfig.mode):
//   'best_unit'  — single component: largest unit where value >= 1 (e.g. 1500 MB → 1.5 GB)
//   default      — decompose into up to 3 components (e.g. 38000 s → 10 h 33 min 20 s)
// Only produces an interesting result when the result unit differs from the input unit.
// Returns true if the given unit is handled by any normalize chain — either because it is
// listed as a step in the chain (fast path) or because it is dimensionally compatible with
// the chain's base unit (covers SI-prefixed variants like Ms, ks, Ts, PB, EB…).
// Units with an explicit defaultTarget (e.g. decade→year, week→day) are excluded from the
// dimensional fallback: they already have a meaningful conversion that should take precedence.
function isNormalizableUnit(unit) {
    var keys = Object.keys(_normalizeChains);
    for (var i = 0; i < keys.length; i++) {
        var cfg = _normalizeChains[keys[i]];
        for (var j = 0; j < cfg.chain.length; j++) {
            if (cfg.chain[j].unit === unit) return true;
        }
    }
    // Dimensional fallback: only for units without an explicit defaultTarget
    if (_defaultUnitTargets[unit] !== undefined) return false;
    for (var di = 0; di < keys.length; di++) {
        try { math.evaluate('1 ' + unit + ' to ' + _normalizeChains[keys[di]].baseUnit); return true; } catch(e) {}
    }
    return false;
}

function computeNormalization(valueStr, unit) {
    // Find the chain for this unit: first check if it is a listed step, then fall back to
    // dimensional matching so that SI-prefixed variants (Ms, ks, Ts, PB, EB…) are covered.
    var chainKey = null;
    var chainKeys = Object.keys(_normalizeChains);
    for (var ci = 0; ci < chainKeys.length && !chainKey; ci++) {
        _normalizeChains[chainKeys[ci]].chain.forEach(function(step) {
            if (step.unit === unit) chainKey = chainKeys[ci];
        });
    }
    if (!chainKey) {
        for (var di = 0; di < chainKeys.length && !chainKey; di++) {
            try {
                math.evaluate('1 ' + unit + ' to ' + _normalizeChains[chainKeys[di]].baseUnit);
                chainKey = chainKeys[di];
            } catch(e) {}
        }
    }
    if (!chainKey) return null;

    var chainConfig = _normalizeChains[chainKey];
    var baseUnit = chainConfig.baseUnit;
    var baseVal;
    try {
        var r = math.evaluate(valueStr + ' ' + unit + ' to ' + baseUnit);
        baseVal = parseFloat(math.format(r, { precision: 14 }));
    } catch(e) { return null; }
    if (isNaN(baseVal) || !isFinite(baseVal) || baseVal < 0) return null;

    // Special case: 0 — return input unit to keep trivial detection working
    if (baseVal === 0) {
        var inputStep = null;
        chainConfig.chain.forEach(function(s) { if (s.unit === unit) inputStep = s; });
        var z = inputStep || chainConfig.chain[chainConfig.chain.length - 1];
        return [{ value: '0', unit: z.unit, display: z.display, longName: z.longName }];
    }

    var chain = chainConfig.chain;

    // best_unit mode: find the largest unit where baseVal / factorInBase >= 1
    if (chainConfig.mode === 'best_unit') {
        var maxDec = chainConfig.maxDecimals || 2;
        for (var i = 0; i < chain.length; i++) {
            var amount = baseVal / chain[i].factorInBase;
            if (amount >= 1 - 1e-9) {
                return [{ value: formatMaxDec(amount, maxDec), unit: chain[i].unit,
                          display: chain[i].display, longName: chain[i].longName }];
            }
        }
        // Fallback: smallest unit (B)
        var last = chain[chain.length - 1];
        return [{ value: formatMaxDec(baseVal, maxDec), unit: last.unit,
                  display: last.display, longName: last.longName }];
    }

    // Default mode: decompose into up to 3 components
    var components = [];
    var remaining = baseVal;
    var epsilon = 1e-9 * baseVal;

    for (var j = 0; j < chain.length; j++) {
        if (remaining < epsilon) break;
        var step = chain[j];
        var isLastInChain = (j === chain.length - 1);
        var willHitCap = (components.length >= 3);

        if (isLastInChain || willHitCap) {
            var fracAmt = remaining / step.factorInBase;
            if (fracAmt > epsilon / step.factorInBase) {
                // isLastInChain: unidad más pequeña de la cadena (ej. ms) → decimales válidos.
                // willHitCap && !isLastInChain: unidad intermedia forzada a ser última por el cap
                // (ej. min cuando ya hay year+day+h) → redondear a entero para evitar "46.67 min".
                var valStr = isLastInChain ? smartFormat(fracAmt) : Math.round(fracAmt).toString();
                components.push({ value: valStr, unit: step.unit,
                                  display: step.display, longName: step.longName });
            }
            break;
        }
        var whole = Math.floor(remaining / step.factorInBase + 1e-9);
        if (whole > 0) {
            components.push({ value: whole.toString(), unit: step.unit,
                              display: step.display, longName: step.longName });
            remaining -= whole * step.factorInBase;
        }
    }

    if (components.length === 0) {
        var lastStep = chain[chain.length - 1];
        components.push({ value: '0', unit: lastStep.unit, display: lastStep.display, longName: lastStep.longName });
    }
    return components;
}

// Strips a leading numeric value (e.g. "10 km" → "km", "20degC" → "degC", "km" → "km").
function _extractUnit(s) {
    return s.replace(/^[\d.,e+\-]+\s*/, '').trim();
}

// Returns the long display name for a unit symbol (e.g. "km" → "kilometer", "degC" → "celsius").
// Falls back to the symbol itself when no long name is found.
function _unitLongDisplayName(unit) {
    var explicit = getExplicitLongName(unit);
    if (explicit) return explicit;
    var derived = getUnitLongName(unit);
    return (derived && derived !== unit) ? derived : unit;
}

// Classifies a math.js error message into a structured object.
// Returns { type, token, suggestions } where:
//   type: 'unknown_symbol' | 'incompatible_units_convert' | 'incompatible_units_op' | 'syntax' | 'other'
//   token: for 'incompatible_units_convert', "fromLongName|toLongName"; for symbol errors, the token; otherwise null
//   suggestions: always null (casing issues are resolved by normalizeExpression before evaluation)
function classifyError(errorMessage) {
    // "Undefined symbol XYZ" or "Unit 'XYZ' not found"
    var tokenMatch =
        /undefined symbol\s+'?(\w+)'?/i.exec(errorMessage) ||
        /unit\s+'?(\w+)'?\s+(?:not found|undefined)/i.exec(errorMessage);
    if (tokenMatch) {
        return { type: 'unknown_symbol', token: tokenMatch[1], suggestions: null };
    }

    // Explicit conversion between incompatible units: "units do not match ('L' != 'km')"
    // The captured groups may include a numeric prefix (e.g. "10 km") when math.js includes the quantity.
    var unitsMismatch = /units do not match\s*\(\s*'([^']+)'\s*!=\s*'([^']+)'\s*\)/i.exec(errorMessage);
    if (unitsMismatch) {
        // [1] is the target side, [2] is the source side — strip any leading value, then resolve long name
        var fromLong = _unitLongDisplayName(_extractUnit(unitsMismatch[2]));
        var toLong   = _unitLongDisplayName(_extractUnit(unitsMismatch[1]));
        return { type: 'incompatible_units_convert', token: fromLong + '|' + toLong, suggestions: null };
    }

    // "cannot convert VALUE? UNIT to UNIT"
    var cannotConvert = /cannot convert\s+(.+?)\s+to\s+(\S+)/i.exec(errorMessage);
    if (cannotConvert) {
        var fromLong = _unitLongDisplayName(_extractUnit(cannotConvert[1]));
        var toLong   = _unitLongDisplayName(_extractUnit(cannotConvert[2]));
        return { type: 'incompatible_units_convert', token: fromLong + '|' + toLong, suggestions: null };
    }

    // Arithmetic between incompatible units: "units do not match" (no unit detail)
    if (/units do not match|cannot convert/i.test(errorMessage)) {
        return { type: 'incompatible_units_op', token: null, suggestions: null };
    }

    // Syntax / parse errors
    if (/syntaxerror|unexpected token|unexpected end|parse error/i.test(errorMessage) ||
        errorMessage.toLowerCase().startsWith('syntaxerror')) {
        return { type: 'syntax', token: null, suggestions: null };
    }

    return { type: 'other', token: null, suggestions: null };
}
