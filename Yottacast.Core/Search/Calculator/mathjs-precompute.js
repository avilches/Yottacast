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

// Builds a longForm → shortForm map for unit symbols that have a shorter equivalent
// with identical SI value and dimensions. E.g. "centimeter" → "cm", "kilometer" → "km".
// Only runs at build time (inside MathJsDataGenerator), never in the production engine.
function _buildLongToShort(symbols) {
    const longToShort = {};

    // Extract SI value, dimensions, and offset for each symbol
    const unitData = {};
    symbols.forEach(function(sym) {
        try {
            var parsed = math.Unit.parse('1 ' + sym);
            if (!parsed.units || parsed.units.length !== 1) return;
            var part   = parsed.units[0];
            var prefix = part.prefix;
            var base   = part.unit;
            var dims   = base.dimensions ? base.dimensions.slice() : null;
            if (!dims) return;
            // SI value = prefix factor × base unit value
            var prefixFactor = (prefix && prefix.value !== undefined && prefix.value !== 1)
                               ? prefix.value : 1;
            var siValue = prefixFactor * (base.value || 1);
            unitData[sym] = { siValue: siValue, offset: base.offset || 0, dims: dims };
        } catch(e) {}
    });

    // Extract prefix name per symbol (only for non-empty LONG prefixes)
    const prefixName = {};
    symbols.forEach(function(sym) {
        try {
            var parsed = math.Unit.parse('1 ' + sym);
            if (!parsed.units || parsed.units.length !== 1) return;
            var p = parsed.units[0].prefix;
            prefixName[sym] = (p && p.name) ? p.name : '';
        } catch(e) {}
    });

    var allSyms = Object.keys(unitData);
    allSyms.forEach(function(symA) {
        // Only remap symbols that carry a LONG prefix (name length > 2).
        // E.g. "centimeter" has prefix "centi" (5 chars) → candidate for remapping.
        // Unprefixed aliases like "miles", "celsius", "meter" are excluded.
        var pName = prefixName[symA] || '';
        if (pName.length <= 2) return;

        var dA = unitData[symA];
        var bestShorter = null;
        var bestLen = symA.length;
        allSyms.forEach(function(symB) {
            if (symB.length >= bestLen) return;
            var dB = unitData[symB];
            if (dB.dims.length !== dA.dims.length) return;
            if (!dA.dims.every(function(d, i) { return d === dB.dims[i]; })) return;
            var relDiff = Math.abs(dA.siValue - dB.siValue) / (Math.abs(dA.siValue) || 1);
            if (relDiff > 1e-10) return;
            if (Math.abs(dA.offset - dB.offset) > 1e-9) return;
            bestShorter = symB;
            bestLen = symB.length;
        });
        if (bestShorter !== null) longToShort[symA] = bestShorter;
    });

    return longToShort;
}

// Returns the pre-computed runtime maps. Serialized to JSON and embedded as
// Yottacast.Core.Search.Calculator.mathjs-precomputed.json.
function extractPrecomputedData() {
    var maps = _buildMaps();
    return {
        symbols:       maps.symbols,
        ambiguous:     maps.ambiguous,
        functionNames: maps.functionNames,
        longToShort:   _buildLongToShort(maps.symbols)
    };
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