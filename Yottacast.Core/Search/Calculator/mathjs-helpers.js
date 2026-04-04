math.createUnit('USD');

function registerCurrency(name, rateVsUSD) {
    // rateVsUSD: units of 'name' per 1 USD (e.g. EUR=0.92 means 1 USD = 0.92 EUR)
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
var _defaultUnitPairs = [
    ['m', 'ft'],      // longitud: métrico↔imperial
    ['kg', 'lb'],     // masa: métrico↔imperial
    ['degC', 'degF'], // temperatura: métrico↔imperial
    ['L', 'gallon'],  // volumen: métrico↔imperial
    ['J', 'BTU'],     // energía: métrico↔imperial
    ['Pa', 'psi'],    // presión: métrico↔imperial
    ['N', 'lbf'],     // fuerza: métrico↔imperial
    ['h', 'minute'],  // tiempo: fallback ('minute' es eval-safe)
    ['A', 'mA'],      ['V', 'mV'],   ['W', 'kW'],
    ['B', 'kB'],      ['F', 'uF'],   ['H', 'mH'],
    ['T', 'mT'],      ['rad', 'deg'],
];

// Default currency pair: if the user types a single currency, suggest the other.
var _defaultCurrencyPair = ['EUR', 'USD'];

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
    // Populate eval-safe aliases (e.g. min → minute to avoid conflict with math.min function)
    if (data.evalSafeAliases) {
        Object.keys(data.evalSafeAliases).forEach(function(k) {
            _evalSafeAliases[k] = data.evalSafeAliases[k];
        });
    }
    // Populate explicit long names for units not derivable via LONG prefix group (e.g. time units)
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

    const candidates = _unitAmbiguousMap[lower];
    if (candidates) {
        // Todos los candidatos comparten el mismo long name (sinónimos) → normalizar al primero
        // Este check va primero para que "l" y "L" (ambos litro) se normalicen siempre a "L"
        if (candidates.every(function(c) { return c.longName === candidates[0].longName; }))
            return { resolved: candidates[0].symbol, ambiguous: false };
        // Input ya es exactamente uno de los canónicos → sin ambigüedad
        if (candidates.some(function(c) { return c.symbol === name; }))
            return { resolved: name, ambiguous: false };
        // Verdaderamente ambiguo: múltiples formas canónicas con distinto significado
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
        }
    } else if (root.type === 'OperatorNode' && root.implicit === true &&
               root.args.length === 2 && root.args[1].type === 'SymbolNode') {
        var unitName = root.args[1].name;
        var defaultTarget = findDefaultTarget(unitName, knownCurrencies);
        if (defaultTarget !== null) {
            kind = 'unit_entry';
            fromUnit = unitName;
            toUnit = defaultTarget;
        } else {
            kind = 'calculation';
        }
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

// Classifies a math.js error message into a structured object.
// Returns { type, token, suggestions } where:
//   type: 'unknown_symbol' | 'incompatible_units' | 'syntax' | 'other'
//   token: the problematic identifier (for symbol errors), or null
//   suggestions: always null (casing issues are resolved by normalizeExpression before evaluation)
function classifyError(errorMessage) {
    // "Undefined symbol XYZ" or "Unit 'XYZ' not found"
    var tokenMatch =
        /undefined symbol\s+'?(\w+)'?/i.exec(errorMessage) ||
        /unit\s+'?(\w+)'?\s+(?:not found|undefined)/i.exec(errorMessage);
    if (tokenMatch) {
        return { type: 'unknown_symbol', token: tokenMatch[1], suggestions: null };
    }

    // Incompatible unit conversion (e.g. kg to meter)
    if (/units do not match|cannot convert|unit mismatch/i.test(errorMessage)) {
        return { type: 'incompatible_units', token: null, suggestions: null };
    }

    // Syntax / parse errors
    if (/syntaxerror|unexpected token|unexpected end|parse error/i.test(errorMessage) ||
        errorMessage.toLowerCase().startsWith('syntaxerror')) {
        return { type: 'syntax', token: null, suggestions: null };
    }

    return { type: 'other', token: null, suggestions: null };
}
