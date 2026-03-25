math.createUnit('USD');

function registerCurrency(name, rateVsUSD) {
    // rateVsUSD: units of 'name' per 1 USD (e.g. EUR=0.92 means 1 USD = 0.92 EUR)
    math.createUnit(name, { definition: (1 / rateVsUSD) + ' USD' }, { override: true });
}

// Map of lowercase function name → canonical math.js function name (built once at load time).
var _mathFunctionNames = (function() {
    var m = {};
    Object.keys(math).forEach(function(k) {
        if (typeof math[k] === 'function') m[k.toLowerCase()] = k;
    });
    return m;
})();

// ── Unit case-normalization map ───────────────────────────────────────────────
// Built at startup from math.js's own UNITS + PREFIXES tables.
// Maps each lowercase token form to ALL canonical math.js tokens with that lowercase.
//
// Safe to normalize (1 canonical form): "KG"→"kg", "RADIAN"→"radian", "MILES"→"miles",
//   "FAHRENHEIT"→"fahrenheit", "KM"→"km", etc.
//
// Ambiguous (>1 canonical form — case is preserved):
//   "mg"/"Mg" coexisten (milli-gram vs mega-gram). Lo mismo con cualquier combinación de
//   los pares de prefijos M/m, P/p, Z/z, Y/y aplicados a unidades de grupo SHORT.
//   Ejemplos: mV/MV, mW/MW, mJ/MJ, mL/ML, ms/Ms (second vs siemens), pF/PF, etc.
//
// Para añadir excepciones manuales, editar _unitOverrides más abajo.
var _unitTokenMap = (function() {
    var map = {};
    function addToken(canonical) {
        var lower = canonical.toLowerCase();
        if (!map[lower]) map[lower] = [];
        if (map[lower].indexOf(canonical) < 0) map[lower].push(canonical);
    }
    Object.keys(math.Unit.UNITS).forEach(function(unitName) {
        var unit = math.Unit.UNITS[unitName];
        addToken(unitName);
        if (unit.prefixes) {
            Object.keys(unit.prefixes).forEach(function(prefix) {
                if (prefix !== '') addToken(prefix + unitName);
            });
        }
    });
    return map;
})();

// Overrides manuales: forzar un mapeo específico independientemente del análisis automático.
// Añade aquí cualquier caso especial que quieras permitir o bloquear.
var _unitOverrides = {
    // 'MG': 'Mg',   // ejemplo: forzar MG → megagramo en vez de dejar ambiguo
};

// Devuelve la forma canónica math.js del token de unidad, o null si es ambiguo/desconocido.
function _resolveUnitToken(name) {
    var override = _unitOverrides[name];
    if (override !== undefined) return override;
    var lower = name.toLowerCase();
    var candidates = _unitTokenMap[lower];
    if (!candidates) return null;
    if (candidates.length === 1) return candidates[0];
    // Ambiguo: múltiples formas canónicas comparten este lowercase.
    // Si el input ya ES uno de los canónicos, lo dejamos intacto.
    return candidates.indexOf(name) >= 0 ? name : null;
}

// Parses the expression into an AST, normalizes currency and unit casing, fixes function casing,
// and returns { expr, hasConversion, currencies }.
// knownCurrenciesCsv is a comma-separated list of uppercase ISO codes (e.g. "USD,EUR,GBP").
// defaultCurrency: if currencies are found but no 'to' conversion exists, appends "to <defaultCurrency>".
// hasConversion is true if the expression contains an explicit 'to' unit-conversion operator.
// Throws if the expression is syntactically invalid — the caller should treat that as no result.
function normalizeExpression(expression, knownCurrenciesCsv, defaultCurrency) {
    var known = {};
    knownCurrenciesCsv.split(',').forEach(function(c) { known[c] = true; });

    // Normalize 'to'/'in' keywords to lowercase before parsing — the math.js parser is
    // case-sensitive and rejects TO/IN as unknown identifiers.
    expression = expression.replace(/\bto\b/gi, 'to');
    expression = expression.replace(/\bin\b/gi, 'in');

    var node = math.parse(expression);
    var currencies = [];
    var hasConversion = false;
    node.traverse(function(n) {
        if (n.type === 'SymbolNode') {
            // Currencies → uppercase
            var upper = n.name.toUpperCase();
            if (known[upper]) {
                n.name = upper;
                if (currencies.indexOf(upper) < 0) currencies.push(upper);
                return;
            }
            var resolvedUnit = _resolveUnitToken(n.name);
            if (resolvedUnit !== null) {
                n.name = resolvedUnit;
                return;
            }
            var lower = n.name.toLowerCase();
            // Function names → canonical casing.
            // In math.js, the fn of a FunctionNode is itself a SymbolNode, so the traverse visits it here.
            // Modifying n.name directly updates the SymbolNode that FunctionNode.toString() uses.
            if (_mathFunctionNames[lower]) {
                n.name = _mathFunctionNames[lower];
            }
        }
        if (n.type === 'OperatorNode' && n.op === 'to') {
            hasConversion = true;
        }
    });
    var normalizedExpr = node.toString();
    if (currencies.length > 0 && !hasConversion && defaultCurrency) {
        normalizedExpr = normalizedExpr + ' to ' + defaultCurrency;
        if (currencies.indexOf(defaultCurrency) < 0) currencies.push(defaultCurrency);
    }
    return { expr: normalizedExpr, hasConversion: hasConversion, currencies: currencies };
}

// Returns a JSON-serializable snapshot of the math.js unit registry and the
// derived _unitTokenMap. Used by tests to detect changes when upgrading math.js.
function extractUnitSnapshot() {
    var units = Object.keys(math.Unit.UNITS).sort();

    var prefixGroups = {};
    Object.keys(math.Unit.PREFIXES).sort().forEach(function(groupName) {
        prefixGroups[groupName] = Object.keys(math.Unit.PREFIXES[groupName]).sort();
    });

    var sortedTokenMap = {};
    Object.keys(_unitTokenMap).sort().forEach(function(k) {
        sortedTokenMap[k] = _unitTokenMap[k].slice().sort();
    });

    var ambiguous = Object.keys(_unitTokenMap).filter(function(k) {
        return _unitTokenMap[k].length > 1;
    }).sort();

    return {
        version:      math.version || 'unknown',
        unitCount:    units.length,
        units:        units,
        prefixGroups: prefixGroups,
        tokenMap:     sortedTokenMap,
        ambiguous:    ambiguous
    };
}
