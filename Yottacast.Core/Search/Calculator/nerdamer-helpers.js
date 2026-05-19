// nerdamer-helpers.js
// Loaded in a dedicated Jint engine (separate from mathjs).
// Requires: nerdamer.core.min.js + Algebra.min.js + Calculus.min.js + Solve.min.js loaded before this file.
//
// Exposes: solveEquation(query, decimalPlaces) → JSON string | null
//          getAlgebraResults(expr, decimalPlaces) → JSON string | null
//
// Returns null when:
//   - No '=' in query
//   - No variables found
//   - All solutions are trivial (solution === variable name)
//   - nerdamer throws (syntax error, unsupported expression)

function roundLongDecimals(text, decimalPlaces) {
    return text.replace(/-?\d+\.\d+/g, function(match) {
        var dot = match.indexOf('.');
        var decimPart = match.substring(dot + 1);
        if (decimPart.length > decimalPlaces) {
            var n = parseFloat(match);
            var rounded = parseFloat(n.toFixed(decimalPlaces)).toString();
            return rounded;
        }
        return match;
    });
}

function solveEquation(query, decimalPlaces) {
    if (decimalPlaces === undefined) decimalPlaces = 2;
    try {
        var eqIdx = query.indexOf('=');
        if (eqIdx < 0) return null;

        // Guard: reject >= and <= operators (they contain '=' but are not equations).
        if (query.indexOf('>=') >= 0 || query.indexOf('<=') >= 0) return null;

        var lhs = query.substring(0, eqIdx).trim();
        var rhs = query.substring(eqIdx + 1).trim();
        if (!lhs || !rhs) return null;

        // Guard: reject queries where rhs itself contains '=' (e.g., x^2=4=invalid).
        if (rhs.indexOf('=') >= 0) return null;

        // Extract all variables from both sides of the equation.
        // nerdamer(expr).variables() returns an array of variable name strings.
        var allVars;
        try {
            allVars = nerdamer('(' + lhs + ')+(' + rhs + ')').variables();
        } catch (e) {
            return null;
        }
        if (!allVars || allVars.length === 0) return null;

        // Guard: identity equations (lhs === rhs symbolically) have infinite solutions.
        // Detected when lhs - rhs simplifies to 0.
        try {
            if (nerdamer('(' + lhs + ')-(' + rhs + ')').text() === '0') return null;
        } catch (e) { /* proceed */ }

        var results = [];
        for (var i = 0; i < allVars.length; i++) {
            var v = allVars[i];
            try {
                // nerdamer supports the "lhs=rhs" equation format in solveFor.
                var solObj = nerdamer(lhs + '=' + rhs).solveFor(v);
                // solveFor returns a plain JS array of nerdamer Symbol objects (one per solution).
                // Guard against null/undefined and wrap non-array values.
                var solArr = Array.isArray(solObj) ? solObj
                           : solObj && solObj.toArray ? solObj.toArray()
                           : solObj != null ? [solObj]
                           : [];
                if (!solArr || solArr.length === 0) continue;

                var solStrs = [];
                for (var j = 0; j < solArr.length; j++) {
                    var sol = solArr[j];
                    var solText = roundLongDecimals(sol.text ? sol.text() : String(sol), decimalPlaces);

                    // Check for free variables in this solution (parametric case).
                    var freeVars = [];
                    try {
                        freeVars = nerdamer(solText).variables().filter(function (sv) {
                            return sv !== v;
                        });
                    } catch (e) { /* keep symbolic */ }

                    if (freeVars.length === 0) {
                        // No free variables: evaluate numerically (e.g. "7/2" → "3.5").
                        try {
                            var evaled = nerdamer(solText).evaluate().text();
                            solText = evaled;
                        } catch (e) { /* keep symbolic */ }
                    }

                    // Filter trivial: solution equals the variable itself.
                    if (solText !== v) {
                        solStrs.push(solText);
                    }
                }

                if (solStrs.length > 0) {
                    results.push({ variable: v, solutions: solStrs });
                }
            } catch (e) {
                // solveFor failed for this variable — skip it.
            }
        }

        if (results.length === 0) return null;
        return JSON.stringify(results);
    } catch (e) {
        return null;
    }
}

// getAlgebraResults(expr) → JSON string [{label, result}, ...] | null
//
// Tries simplify, expand, factor, diff(per variable), integrate(single var only).
// Filters: drops cells where result equals the raw input expression.
// Deduplicates: keeps first cell per unique result string.
// Returns null when: no variables found, nerdamer can't parse, or all cells filtered.
function getAlgebraResults(expr, decimalPlaces) {
    if (decimalPlaces === undefined) decimalPlaces = 2;
    try {
        var vars;
        try {
            vars = nerdamer(expr).variables();
        } catch (e) {
            return null;
        }
        if (!vars || vars.length === 0) return null;

        // Guard: reject multi-letter variable names (e.g. "hello", "world") — likely plain text, not math.
        for (var vi = 0; vi < vars.length; vi++) {
            if (vars[vi].length > 1) return null;
        }

        var results = [];
        var seenResults = {};

        function tryOp(label, fn) {
            try {
                var r = fn();
                if (!r) return;
                var text = roundLongDecimals(r.text ? r.text() : String(r), decimalPlaces);
                if (text === expr) return;          // no-op: result equals raw input
                if (seenResults[text]) return;      // deduplicate
                seenResults[text] = true;
                results.push({ label: label, result: text });
            } catch (e) { /* skip failed operations */ }
        }

        tryOp('simplify', function() { return nerdamer(expr); });
        tryOp('expand',   function() { return nerdamer.expand(expr); });
        tryOp('factor',   function() { return nerdamer.factor(expr); });

        // Derivatives — one per variable, alphabetical order
        var sortedVars = vars.slice().sort();
        for (var i = 0; i < sortedVars.length; i++) {
            (function(v) {
                tryOp('d/d' + v, function() { return nerdamer.diff(expr, v); });
            })(sortedVars[i]);
        }

        // Integral — only for single-variable expressions
        if (vars.length === 1) {
            tryOp('∫d' + vars[0], function() { return nerdamer.integrate(expr, vars[0]); });
        }

        if (results.length === 0) return null;
        return JSON.stringify(results);
    } catch (e) {
        return null;
    }
}