// mathjs-precompute.js — generation only, not loaded in the production engine.
// Requires math.js to already be loaded.
// Used by MathJsDataGenerator.GenerateData() in Yottacast.Core.Tests to produce:
//   - mathjs-precomputed.json  (runtime maps for mathjs-helpers.js)
//   - mathjs-unit-snapshot.json  (regression baseline for tests)

// Computes a human-readable long name for a canonical math.js unit symbol.
// E.g. "mg" → "milligram", "Mg" → "megagram", "mS" → "millisiemens".
// Falls back to the canonical symbol itself if decomposition fails.
function _computeUnitLongName(symbol) {
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
            if (longPrefixName === null) longPrefixName = prefix.name; // fallback to short prefix name
        }
        if (longPrefixName === null) longPrefixName = '';

        // Find the long unit name: a UNITS entry with LONG prefixes and same value + dimensions.
        // Uses a relative tolerance for floating-point comparison of unit values.
        let longUnitName = null;
        const longPrefixGroup = math.Unit.PREFIXES.LONG;
        Object.keys(math.Unit.UNITS).forEach(function(name) {
            if (longUnitName) return;
            const u = math.Unit.UNITS[name];
            if (u.prefixes !== longPrefixGroup) return;
            const relDiff = Math.abs(u.value - baseUnit.value) / (Math.abs(baseUnit.value) || 1);
            if (relDiff > 1e-12) return;
            const dimA = u.dimensions;
            const dimB = baseUnit.dimensions;
            if (!dimA || !dimB || dimA.length !== dimB.length) return;
            const dimsMatch = dimA.every(function(d, i) { return d === dimB[i]; });
            if (!dimsMatch) return;
            longUnitName = name;
        });

        if (!longUnitName) longUnitName = baseUnit.name || symbol;
        return longPrefixName + longUnitName;
    } catch (e) {
        return symbol;
    }
}

// Builds all three runtime maps from math.js internals.
function _buildMaps() {
    const functionNames = {};
    Object.keys(math).forEach(function(k) {
        if (typeof math[k] === 'function') functionNames[k.toLowerCase()] = k;
    });

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

    const longNameCache = {};
    Object.keys(tokenMap).forEach(function(lower) {
        tokenMap[lower].forEach(function(sym) {
            if (!(sym in longNameCache)) longNameCache[sym] = _computeUnitLongName(sym);
        });
    });

    return { tokenMap: tokenMap, longNameCache: longNameCache, functionNames: functionNames };
}

// Returns the pre-computed runtime maps. Serialized to JSON and embedded as
// Yottacast.Core.Search.Calculator.mathjs-precomputed.json.
function extractPrecomputedData() {
    return _buildMaps();
}

// Returns a JSON-serializable snapshot of the math.js unit registry and the
// derived tokenMap. Used by tests to detect changes when upgrading math.js.
function extractUnitSnapshot() {
    const maps = _buildMaps();

    const units = Object.keys(math.Unit.UNITS).sort();

    const prefixGroups = {};
    Object.keys(math.Unit.PREFIXES).sort().forEach(function(groupName) {
        prefixGroups[groupName] = Object.keys(math.Unit.PREFIXES[groupName]).sort();
    });

    const sortedTokenMap = {};
    Object.keys(maps.tokenMap).sort().forEach(function(k) {
        sortedTokenMap[k] = maps.tokenMap[k].slice().sort();
    });

    const ambiguous = Object.keys(maps.tokenMap).filter(function(k) {
        return maps.tokenMap[k].length > 1;
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