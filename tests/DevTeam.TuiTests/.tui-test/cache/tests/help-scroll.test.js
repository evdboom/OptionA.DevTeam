//# hash=1753cce1cb8546bef82c75d9a4786e6d
//# sourceMappingURL=help-scroll.test.js.map

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
import { test, expect, Key } from "@microsoft/tui-test";
import { cliArgs } from "./helpers.js";
// All /help commands that must be reachable via scrolling.
var HELP_COMMANDS = [
    "/init",
    "/customize",
    "/bug",
    "/status",
    "/history",
    "/mode",
    "/keep-awake",
    "/add-issue",
    "/plan",
    "/questions",
    "/budget",
    "/check-update",
    "/update",
    "/max-iterations",
    "/max-subagents",
    "/run",
    "/stop",
    "/wait",
    "/feedback",
    "/approve",
    "/answer",
    "/goal",
    "/exit",
    "@role"
];
var BASE_ARGS = cliArgs("start", "--workspace", ".devteam-tui-test");
// ── Test 1: Home key reaches oldest content (40×120) ────────────────────────
// Uses a realistic terminal size where long help lines wrap to 2-3 rows each.
// This is the scenario that exposed the MaxScrollOffset bug — without the fix,
// the Home key caps too early and /init is never reachable.
test.describe("help scroll — MaxScrollOffset fix (40×120)", function() {
    test.use({
        program: {
            file: "dotnet",
            args: BASE_ARGS
        },
        rows: 40,
        columns: 120
    });
    test("all /help commands are visible after scrolling", function(param) {
        var terminal = param.terminal;
        return _async_to_generator(function() {
            var _iteratorNormalCompletion, _didIteratorError, _iteratorError, _iterator, _step, cmd, err;
            return _ts_generator(this, function(_state) {
                switch(_state.label){
                    case 0:
                        return [
                            4,
                            expect(terminal.getByText("help for commands")).toBeVisible({
                                timeout: 120000
                            })
                        ];
                    case 1:
                        _state.sent();
                        terminal.submit("/help");
                        return [
                            4,
                            expect(terminal.getByText("@role")).toBeVisible()
                        ];
                    case 2:
                        _state.sent();
                        // Jump to oldest content — the key assertion: without the MaxScrollOffset fix
                        // the Home key caps too early and /init never appears.
                        terminal.keyPress(Key.Home);
                        _iteratorNormalCompletion = true, _didIteratorError = false, _iteratorError = undefined;
                        _state.label = 3;
                    case 3:
                        _state.trys.push([
                            3,
                            8,
                            9,
                            10
                        ]);
                        _iterator = HELP_COMMANDS.slice(0, 6)[Symbol.iterator]();
                        _state.label = 4;
                    case 4:
                        if (!!(_iteratorNormalCompletion = (_step = _iterator.next()).done)) return [
                            3,
                            7
                        ];
                        cmd = _step.value;
                        // Use strict:false — some commands (e.g. /init) also appear in the
                        // "No workspace found. Use /init..." startup message, causing a strict-mode
                        // violation if we require exactly one match.
                        return [
                            4,
                            expect(terminal.getByText(cmd, {
                                strict: false
                            })).toBeVisible()
                        ];
                    case 5:
                        _state.sent();
                        _state.label = 6;
                    case 6:
                        _iteratorNormalCompletion = true;
                        return [
                            3,
                            4
                        ];
                    case 7:
                        return [
                            3,
                            10
                        ];
                    case 8:
                        err = _state.sent();
                        _didIteratorError = true;
                        _iteratorError = err;
                        return [
                            3,
                            10
                        ];
                    case 9:
                        try {
                            if (!_iteratorNormalCompletion && _iterator.return != null) {
                                _iterator.return();
                            }
                        } finally{
                            if (_didIteratorError) {
                                throw _iteratorError;
                            }
                        }
                        return [
                            7
                        ];
                    case 10:
                        terminal.keyPress(Key.End);
                        return [
                            4,
                            expect(terminal.getByText("@role")).toBeVisible()
                        ];
                    case 11:
                        _state.sent();
                        return [
                            2
                        ];
                }
            });
        })();
    });
});
// ── Test 2: Full coverage via PgUp (30×120) ──────────────────────────────────
// At 40 rows: PageStep(25) > MaxScrollOffset(19) — one PgUp jumps directly to
// the top, leaving lines at the chunk boundary (like /update, /max-iterations)
// in an unreachable gap. At 30 rows: PageStep(15) < MaxScrollOffset(26), so
// three positions (bottom → middle → top) cover the entire help content.
test.describe("help scroll — full command coverage (30×120)", function() {
    test.use({
        program: {
            file: "dotnet",
            args: BASE_ARGS
        },
        rows: 30,
        columns: 120
    });
    test("scrolling through /help reveals all commands", function(param) {
        var terminal = param.terminal;
        return _async_to_generator(function() {
            var visible, collectVisible, i, missing, buf;
            return _ts_generator(this, function(_state) {
                switch(_state.label){
                    case 0:
                        return [
                            4,
                            expect(terminal.getByText("help for commands")).toBeVisible({
                                timeout: 120000
                            })
                        ];
                    case 1:
                        _state.sent();
                        terminal.submit("/help");
                        return [
                            4,
                            expect(terminal.getByText("@role")).toBeVisible()
                        ];
                    case 2:
                        _state.sent();
                        // Allow an extra render cycle after the /help response before scrolling.
                        return [
                            4,
                            new Promise(function(r) {
                                return setTimeout(r, 300);
                            })
                        ];
                    case 3:
                        _state.sent();
                        visible = new Set();
                        // Check all commands at the current scroll position.
                        // Uses a 500 ms timeout so tui-test has time to poll the rendered rows.
                        // Already-found commands are skipped so later iterations are fast.
                        collectVisible = function collectVisible() {
                            return _async_to_generator(function() {
                                var _iteratorNormalCompletion, _didIteratorError, _iteratorError, _iterator, _step, cmd, unused, err;
                                return _ts_generator(this, function(_state) {
                                    switch(_state.label){
                                        case 0:
                                            _iteratorNormalCompletion = true, _didIteratorError = false, _iteratorError = undefined;
                                            _state.label = 1;
                                        case 1:
                                            _state.trys.push([
                                                1,
                                                8,
                                                9,
                                                10
                                            ]);
                                            _iterator = HELP_COMMANDS[Symbol.iterator]();
                                            _state.label = 2;
                                        case 2:
                                            if (!!(_iteratorNormalCompletion = (_step = _iterator.next()).done)) return [
                                                3,
                                                7
                                            ];
                                            cmd = _step.value;
                                            if (visible.has(cmd)) return [
                                                3,
                                                6
                                            ];
                                            _state.label = 3;
                                        case 3:
                                            _state.trys.push([
                                                3,
                                                5,
                                                ,
                                                6
                                            ]);
                                            // Use strict:false — /init also appears in the startup message
                                            // "No workspace found. Use /init…" so it has 2 matches when visible.
                                            return [
                                                4,
                                                expect(terminal.getByText(cmd, {
                                                    strict: false
                                                })).toBeVisible({
                                                    timeout: 500
                                                })
                                            ];
                                        case 4:
                                            _state.sent();
                                            visible.add(cmd);
                                            return [
                                                3,
                                                6
                                            ];
                                        case 5:
                                            unused = _state.sent();
                                            return [
                                                3,
                                                6
                                            ];
                                        case 6:
                                            _iteratorNormalCompletion = true;
                                            return [
                                                3,
                                                2
                                            ];
                                        case 7:
                                            return [
                                                3,
                                                10
                                            ];
                                        case 8:
                                            err = _state.sent();
                                            _didIteratorError = true;
                                            _iteratorError = err;
                                            return [
                                                3,
                                                10
                                            ];
                                        case 9:
                                            try {
                                                if (!_iteratorNormalCompletion && _iterator.return != null) {
                                                    _iterator.return();
                                                }
                                            } finally{
                                                if (_didIteratorError) {
                                                    throw _iteratorError;
                                                }
                                            }
                                            return [
                                                7
                                            ];
                                        case 10:
                                            return [
                                                2
                                            ];
                                    }
                                });
                            })();
                        };
                        // Position 1: bottom (scrollOffset=0) — shows latest content.
                        return [
                            4,
                            collectVisible()
                        ];
                    case 4:
                        _state.sent();
                        i = 0;
                        _state.label = 5;
                    case 5:
                        if (!(i < 6)) return [
                            3,
                            9
                        ];
                        terminal.keyPress(Key.PageUp);
                        // Wait for the TUI to render the new scroll position (RefreshMs = 100 ms).
                        return [
                            4,
                            new Promise(function(r) {
                                return setTimeout(r, 300);
                            })
                        ];
                    case 6:
                        _state.sent();
                        return [
                            4,
                            collectVisible()
                        ];
                    case 7:
                        _state.sent();
                        _state.label = 8;
                    case 8:
                        i++;
                        return [
                            3,
                            5
                        ];
                    case 9:
                        // Use Home key as a safety net — guarantees we're at MaxScrollOffset
                        // and the very oldest content (/init and first commands) is in view.
                        terminal.keyPress(Key.Home);
                        return [
                            4,
                            new Promise(function(r) {
                                return setTimeout(r, 300);
                            })
                        ];
                    case 10:
                        _state.sent();
                        return [
                            4,
                            collectVisible()
                        ];
                    case 11:
                        _state.sent();
                        missing = HELP_COMMANDS.filter(function(c) {
                            return !visible.has(c);
                        });
                        if (missing.length > 0) {
                            buf = terminal.getViewableBuffer().map(function(row) {
                                return row.join("").trimEnd();
                            }).join("\n");
                            throw new Error("The following /help commands were never visible while scrolling: ".concat(missing.join(", "), "\n\nLast terminal frame:\n---\n").concat(buf, "\n---"));
                        }
                        return [
                            2
                        ];
                }
            });
        })();
    });
});
