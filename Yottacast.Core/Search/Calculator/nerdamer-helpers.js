// nerdamer-helpers.js
// Loaded in a dedicated Jint engine (separate from mathjs).
// Requires: nerdamer.core.min.js + Algebra.min.js loaded before this file.
//
// Exposes: solveEquation(query) → JSON string | null
//
// Returns null when:
//   - No '=' in query
//   - No variables found
//   - All solutions are trivial (solution === variable name)
//   - nerdamer throws (syntax error, unsupported expression)

function solveEquation(query) {
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

        var results = [];
        for (var i = 0; i < allVars.length; i++) {
            var v = allVars[i];
            try {
                // nerdamer supports the "lhs=rhs" equation format in solveFor.
                var solObj = nerdamer(lhs + '=' + rhs).solveFor(v);
                // solveFor returns a nerdamer object; toArray() yields individual solutions.
                var solArr = solObj.toArray ? solObj.toArray() : [solObj];
                if (!solArr || solArr.length === 0) continue;

                var solStrs = [];
                for (var j = 0; j < solArr.length; j++) {
                    var sol = solArr[j];
                    var solText = sol.text ? sol.text() : String(sol);

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