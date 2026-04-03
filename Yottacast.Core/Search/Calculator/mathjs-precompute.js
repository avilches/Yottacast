// mathjs-precompute.js — generation only, not loaded in the production engine.
// Requires math.js to already be loaded.
// Used by MathJsDataGenerator.GenerateData() in Yottacast.Core.Tests to produce:
//   - mathjs-precomputed.json  (runtime maps for mathjs-helpers.js)
//   - mathjs-unit-snapshot.json  (regression baseline for tests)

// Builds all runtime maps from math.js internals.
// Returns:
//   symbols   — sorted array of all canonical unit symbols
//   ambiguous — object mapping lowercase token → [{symbol, longName}], only when >1 canonical form
//   functionNames — object mapping lowercase function name → canonical name
function _buildMaps() {
    const functionNames = {};
    Object.keys(math).forEach(function(k) {
        if (typeof math[k] === 'function') functionNames[k.toLowerCase()] = k;
    });

    // Build intermediate tokenMap: lowercase → [canonical forms]
    const tokenMap = {};
    function addToken(canonical) {
        const lower = canonical.toLowerCase();
        if (!tokenMap[lower]) tokenMap[lower] = [];
        if (tokenMap[lower].indexOf(canonical) < 0) tokenMap[lower].push(canonical);
    }
    Object.keys(math.Unit.UNITS).forEach(function(unitName) {
        const unit = math.Unit.UNITS[unitName];
        addToken(unitName);
        if (unit.prefixes) {
            Object.keys(unit.prefixes).forEach(function(prefix) {
                if (prefix !== '') addToken(prefix + unitName);
            });
        }
    });

    // All canonical symbols (flat sorted array)
    const symbolSet = {};
    Object.keys(tokenMap).forEach(function(lower) {
        tokenMap[lower].forEach(function(sym) { symbolSet[sym] = true; });
    });
    const symbols = Object.keys(symbolSet).sort();

    // Ambiguous entries only — long names computed only for these symbols
    const ambiguous = {};
    Object.keys(tokenMap).forEach(function(lower) {
        const canonicals = tokenMap[lower];
        if (canonicals.length <= 1) return;
        ambiguous[lower] = canonicals.map(function(sym) {
            return { symbol: sym, longName: getUnitLongName(sym) };
        });
    });

    return { symbols: symbols, ambiguous: ambiguous, functionNames: functionNames };
}

// Returns the pre-computed runtime maps. Serialized to JSON and embedded as
// Yottacast.Core.Search.Calculator.mathjs-precomputed.json.
function extractPrecomputedData() {
    return _buildMaps();
}

// Returns a JSON-serializable snapshot of the math.js unit registry.
// Used by tests to detect changes when upgrading math.js.
function extractUnitSnapshot() {
    const maps = _buildMaps();

    const units = Object.keys(math.Unit.UNITS).sort();

    const prefixGroups = {};
    Object.keys(math.Unit.PREFIXES).sort().forEach(function(groupName) {
        prefixGroups[groupName] = Object.keys(math.Unit.PREFIXES[groupName]).sort();
    });

    // ambiguous in snapshot: lowercase → sorted canonical symbols (no longNames needed)
    const sortedAmbiguous = {};
    Object.keys(maps.ambiguous).sort().forEach(function(k) {
        sortedAmbiguous[k] = maps.ambiguous[k].map(function(e) { return e.symbol; }).sort();
    });

    return {
        version:      math.version || 'unknown',
        unitCount:    units.length,
        units:        units,
        prefixGroups: prefixGroups,
        symbols:      maps.symbols,
        ambiguous:    sortedAmbiguous
    };
}