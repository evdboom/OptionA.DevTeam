//# hash=e62642fbd73b1c5c01d4c871341cb055
//# sourceMappingURL=layout-full-width.test.js.map

function asyncGeneratorStep(gen, resolve, reject, _next, _throw, key, arg) {
    try {
        var info = gen[key](arg);
        var value = info.value;
    } catch (error) {
        reject(error);
        return;
    }
    if (info.done) {
        resolve(value);
    } else {
        Promise.resolve(value).then(_next, _throw);
    }
}
function _async_to_generator(fn) {
    return function() {
        var self = this, args = arguments;
        return new Promise(function(resolve, reject) {
            var gen = fn.apply(self, args);
            function _next(value) {
                asyncGeneratorStep(gen, resolve, reject, _next, _throw, "next", value);
            }
            function _throw(err) {
                asyncGeneratorStep(gen, resolve, reject, _next, _throw, "throw", err);
            }
            _next(undefined);
        });
    };
}
function _ts_generator(thisArg, body) {
    var f, y, t, _ = {
        label: 0,
        sent: function() {
            if (t[0] & 1) throw t[1];
            return t[1];
        },
        trys: [],
        ops: []
    }, g = Object.create((typeof Iterator === "function" ? Iterator : Object).prototype), d = Object.defineProperty;
    return d(g, "next", {
        value: verb(0)
    }), d(g, "throw", {
        value: verb(1)
    }), d(g, "return", {
        value: verb(2)
    }), typeof Symbol === "function" && d(g, Symbol.iterator, {
        value: function() {
            return this;
        }
    }), g;
    function verb(n) {
        return function(v) {
            return step([
                n,
                v
            ]);
        };
    }
    function step(op) {
        if (f) throw new TypeError("Generator is already executing.");
        while(g && (g = 0, op[0] && (_ = 0)), _)try {
            if (f = 1, y && (t = op[0] & 2 ? y["return"] : op[0] ? y["throw"] || ((t = y["return"]) && t.call(y), 0) : y.next) && !(t = t.call(y, op[1])).done) return t;
            if (y = 0, t) op = [
                op[0] & 2,
                t.value
            ];
            switch(op[0]){
                case 0:
                case 1:
                    t = op;
                    break;
                case 4:
                    _.label++;
                    return {
                        value: op[1],
                        done: false
                    };
                case 5:
                    _.label++;
                    y = op[1];
                    op = [
                        0
                    ];
                    continue;
                case 7:
                    op = _.ops.pop();
                    _.trys.pop();
                    continue;
                default:
                    if (!(t = _.trys, t = t.length > 0 && t[t.length - 1]) && (op[0] === 6 || op[0] === 2)) {
                        _ = 0;
                        continue;
                    }
                    if (op[0] === 3 && (!t || op[1] > t[0] && op[1] < t[3])) {
                        _.label = op[1];
                        break;
                    }
                    if (op[0] === 6 && _.label < t[1]) {
                        _.label = t[1];
                        t = op;
                        break;
                    }
                    if (t && _.label < t[2]) {
                        _.label = t[2];
                        _.ops.push(op);
                        break;
                    }
                    if (t[2]) _.ops.pop();
                    _.trys.pop();
                    continue;
            }
            op = body.call(thisArg, _);
        } catch (e) {
            op = [
                6,
                e
            ];
            y = 0;
        } finally{
            f = t = 0;
        }
        if (op[0] & 5) throw op[1];
        return {
            value: op[0] ? op[1] : void 0,
            done: true
        };
    }
}
import { test, expect } from "@microsoft/tui-test";
import { cliArgs } from "./helpers.js";
var BUILD_TIMEOUT = 120000;
var COLUMNS = 120;
test.use({
    program: {
        file: "dotnet",
        args: cliArgs("start", "--workspace", ".devteam-e2e-layout")
    },
    rows: 40,
    columns: COLUMNS
});
function rowToString(row) {
    return row.join("");
}
function lastNonSpaceIndex(text) {
    for(var i = text.length - 1; i >= 0; i--){
        if (text[i] !== " ") {
            return i;
        }
    }
    return -1;
}
function assertRowEndsAtTerminalEdge(buffer, needle) {
    var renderedRows = buffer.map(rowToString);
    var row = renderedRows.find(function(line) {
        return line.includes(needle);
    });
    if (!row) {
        throw new Error("Could not find a rendered row containing '".concat(needle, "'."));
    }
    var rightEdge = COLUMNS - 1;
    var lastChar = lastNonSpaceIndex(row);
    if (lastChar !== rightEdge) {
        throw new Error("Row containing '".concat(needle, "' did not reach full width. Expected right edge at column ").concat(rightEdge, ", got ").concat(lastChar, ".\nRow='").concat(row, "'"));
    }
}
test("shell renders 3 full-width panels", function(param) {
    var terminal = param.terminal;
    return _async_to_generator(function() {
        var buffer;
        return _ts_generator(this, function(_state) {
            switch(_state.label){
                case 0:
                    return [
                        4,
                        expect(terminal.getByText("Phase:")).toBeVisible({
                            timeout: BUILD_TIMEOUT
                        })
                    ];
                case 1:
                    _state.sent();
                    return [
                        4,
                        expect(terminal.getByText("Workspace:")).toBeVisible()
                    ];
                case 2:
                    _state.sent();
                    return [
                        4,
                        expect(terminal.getByText("help for commands")).toBeVisible()
                    ];
                case 3:
                    _state.sent();
                    buffer = terminal.getViewableBuffer();
                    // Header panel body line
                    assertRowEndsAtTerminalEdge(buffer, "Phase:");
                    // Progress panel body line
                    assertRowEndsAtTerminalEdge(buffer, "Workspace:");
                    // Input panel line
                    assertRowEndsAtTerminalEdge(buffer, "> ");
                    // Visual regression snapshot: catches panel frame shape regressions.
                    return [
                        4,
                        expect(terminal).toMatchSnapshot()
                    ];
                case 4:
                    _state.sent();
                    return [
                        2
                    ];
            }
        });
    })();
});
