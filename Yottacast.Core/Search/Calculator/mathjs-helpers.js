math.createUnit('USD');

function registerCurrency(name, rateVsUSD) {
    // rateVsUSD: units of 'name' per 1 USD (e.g. EUR=0.92 means 1 USD = 0.92 EUR)
    math.createUnit(name, { definition: (1 / rateVsUSD) + ' USD' }, { override: true });
}

// Pre-computed maps injected by loadPrecomputedData() before any expression is evaluated.
// These are produced by mathjs-precompute.js and embedded as a resource in the assembly.
// See MathJsDataGenerator.GenerateData() in Yottacast.Core.Tests to regenerate them.
var _mathFunctionNames = null;
var _unitTokenMap      = null;
var _unitLongNameCache = null;

// Overrides manuales: forzar un mapeo específico independientemente del análisis automático.
// Añade aquí cualquier caso especial que quieras permitir o bloquear.
var _unitOverrides = {
    // 'MG': 'Mg',   // ejemplo: forzar MG → megagramo en vez de dejar ambiguo
};

// Injects pre-computed maps produced by mathjs-precompute.js.
// Must be called before any expression evaluation.
function loadPrecomputedData(data) {
    _unitTokenMap      = data.tokenMap;
    _unitLongNameCache = data.longNameCache;
    _mathFunctionNames = data.functionNames;
}

// Devuelve la forma canónica math.js del token de unidad, o null si es ambiguo/desconocido.
function resolveUnitToken(name) {
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
// detects ambiguous unit tokens, and returns { expr, isConversion, currencies, ambiguities }.
// knownCurrenciesCsv is a comma-separated list of uppercase ISO codes (e.g. "USD,EUR,GBP").
// defaultCurrency: if currencies are found but no 'to' conversion exists, appends "to <defaultCurrency>".
// isConversion is true if the expression is an explicit 'to' unit-conversion operator.
// ambiguities: [{input, candidates:[{symbol, longName}]}] for tokens that map to multiple canonical forms.
// Throws if the expression is syntactically invalid — the caller should treat that as no result.
function normalizeExpression(expression, knownCurrenciesCsv, defaultCurrency) {
    // Normalize 'to'/'in' keywords to lowercase before parsing — the math.js parser is
    // case-sensitive and rejects TO/IN as unknown identifiers.
    expression = expression.replace(/\bto\b/gi, 'to');
    expression = expression.replace(/\bin\b/gi, 'in');

    var knownCurrencies = {};
    knownCurrenciesCsv.split(',').forEach(function(c) { knownCurrencies[c] = true; });
    var currencies = [];
    var ambiguities = [];
    var seenAmbiguous = {};

    var root = math.parse(expression);
    root.traverse(function(n) {
        if (n.type === 'SymbolNode') {
            // Currencies → uppercase
            var upper = n.name.toUpperCase();
            var lower = n.name.toLowerCase();
            if (knownCurrencies[upper]) {
                n.name = upper;
                if (currencies.indexOf(upper) < 0) currencies.push(upper);
                return;
            }
            var candidates = _unitTokenMap[lower];
            // Detect ambiguity before mutating n.name (preserves the original user token).
            if (candidates && candidates.length > 1 && !_mathFunctionNames[lower] && !seenAmbiguous[lower]) {
                seenAmbiguous[lower] = true;
                ambiguities.push({
                    input: n.name,
                    candidates: candidates.map(function(sym) {
                        return { symbol: sym, longName: _unitLongNameCache[sym] };
                    })
                });
            }
            var resolvedUnit = resolveUnitToken(n.name);
            if (resolvedUnit !== null) {
                n.name = resolvedUnit;
                return;
            }
            // Function names → canonical casing.
            // In math.js, the fn of a FunctionNode is itself a SymbolNode, so the traverse visits it here.
            // Modifying n.name directly updates the SymbolNode that FunctionNode.toString() uses.
            if (_mathFunctionNames[lower]) {
                n.name = _mathFunctionNames[lower];
            }
        }
    });
    var isConversion = root.type === 'OperatorNode' && root.op === 'to';
    // Only auto-append "to defaultCurrency" when the root is exactly "N CURRENCY" (implicit multiply).
    var isSingleCurrencyUnit =
        root.type === 'OperatorNode' && root.implicit === true &&
        root.args.length === 2 &&
        root.args[1].type === 'SymbolNode' &&
        knownCurrencies[root.args[1].name.toUpperCase()];
    var normalizedExpr = root.toString();
    if (isSingleCurrencyUnit && defaultCurrency) {
        normalizedExpr = normalizedExpr + ' to ' + defaultCurrency;
        if (currencies.indexOf(defaultCurrency) < 0) currencies.push(defaultCurrency);
    }
    return { expr: normalizedExpr, isConversion: isConversion, currencies: currencies, ambiguities: ambiguities };
}

// Classifies a math.js error message into a structured object.
// Returns { type, token, suggestions } where:
//   type: 'wrong_unit_casing' | 'unknown_symbol' | 'incompatible_units' | 'syntax' | 'other'
//   token: the problematic identifier (for symbol errors), or null
//   suggestions: [{symbol, longName}] when the token maps to known unit variants, or null
function classifyError(errorMessage) {
    // "Undefined symbol XYZ" or "Unit 'XYZ' not found"
    var tokenMatch =
        /undefined symbol\s+'?(\w+)'?/i.exec(errorMessage) ||
        /unit\s+'?(\w+)'?\s+(?:not found|undefined)/i.exec(errorMessage);
    if (tokenMatch) {
        var token = tokenMatch[1];
        var lower = token.toLowerCase();
        var candidates = _unitTokenMap[lower];
        if (candidates && candidates.length > 0) {
            // Token's lowercase maps to known unit(s) — wrong casing
            return {
                type: 'wrong_unit_casing',
                token: token,
                suggestions: candidates.map(function(sym) {
                    return { symbol: sym, longName: _unitLongNameCache[sym] };
                })
            };
        }
        return { type: 'unknown_symbol', token: token, suggestions: null };
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