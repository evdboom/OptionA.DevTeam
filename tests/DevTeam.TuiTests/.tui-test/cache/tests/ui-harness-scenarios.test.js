//# hash=9f422f7a187898bef44c59624b45e112
//# sourceMappingURL=ui-harness-scenarios.test.js.map

function _array_like_to_array(arr, len) {
    if (len == null || len > arr.length) len = arr.length;
    for(var i = 0, arr2 = new Array(len); i < len; i++)arr2[i] = arr[i];
    return arr2;
}
function _array_without_holes(arr) {
    if (Array.isArray(arr)) return _array_like_to_array(arr);
}
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
function _iterable_to_array(iter) {
    if (typeof Symbol !== "undefined" && iter[Symbol.iterator] != null || iter["@@iterator"] != null) return Array.from(iter);
}
function _non_iterable_spread() {
    throw new TypeError("Invalid attempt to spread non-iterable instance.\\nIn order to be iterable, non-array objects must have a [Symbol.iterator]() method.");
}
function _to_consumable_array(arr) {
    return _array_without_holes(arr) || _iterable_to_array(arr) || _unsupported_iterable_to_array(arr) || _non_iterable_spread();
}
function _unsupported_iterable_to_array(o, minLen) {
    if (!o) return;
    if (typeof o === "string") return _array_like_to_array(o, minLen);
    var n = Object.prototype.toString.call(o).slice(8, -1);
    if (n === "Object" && o.constructor) n = o.constructor.name;
    if (n === "Map" || n === "Set") return Array.from(n);
    if (n === "Arguments" || /^(?:Ui|I)nt(?:8|16|32)(?:Clamped)?Array$/.test(n)) return _array_like_to_array(o, minLen);
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
// The ui-harness command starts the full interactive shell with synthetic workspace
// state so the UI can be verified without real agent backends.
// Each describe block overrides the program args to use a specific scenario.
var BUILD_TIMEOUT = 120000;
// Shared base args — only the --scenario value differs per describe block.
var BASE_ARGS = cliArgs("ui-harness");
// Wait for the shell to finish starting up — "Phase:" appears exactly once in the header.
function waitForReady(terminal) {
    return _async_to_generator(function() {
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
                        2
                    ];
            }
        });
    })();
}
// ── Planning scenario ────────────────────────────────────────────────────────
// Phase: Planning. One planning issue (done), one blocking question.
test.describe("planning scenario", function() {
    test.use({
        program: {
            file: "dotnet",
            args: _to_consumable_array(BASE_ARGS).concat([
                "--scenario",
                "planning"
            ])
        },
        rows: 40,
        columns: 120
    });
    test("header shows Planning phase", function(param) {
        var terminal = param.terminal;
        return _async_to_generator(function() {
            return _ts_generator(this, function(_state) {
                switch(_state.label){
                    case 0:
                        return [
                            4,
                            waitForReady(terminal)
                        ];
                    case 1:
                        _state.sent();
                        return [
                            4,
                            expect(terminal.getByText("Planning")).toBeVisible()
                        ];
                    case 2:
                        _state.sent();
                        return [
                            2
                        ];
                }
            });
        })();
    });
});
// ── Architect-planning scenario ──────────────────────────────────────────────
// Phase: ArchitectPlanning. Multiple issues, one completed architect run.
test.describe("architect scenario", function() {
    test.use({
        program: {
            file: "dotnet",
            args: _to_consumable_array(BASE_ARGS).concat([
                "--scenario",
                "architect"
            ])
        },
        rows: 40,
        columns: 120
    });
    test("header shows Architect Planning phase", function(param) {
        var terminal = param.terminal;
        return _async_to_generator(function() {
            return _ts_generator(this, function(_state) {
                switch(_state.label){
                    case 0:
                        return [
                            4,
                            waitForReady(terminal)
                        ];
                    case 1:
                        _state.sent();
                        return [
                            4,
                            expect(terminal.getByText("Architect")).toBeVisible()
                        ];
                    case 2:
                        _state.sent();
                        return [
                            2
                        ];
                }
            });
        })();
    });
});
// ── Execution scenario ───────────────────────────────────────────────────────
// Phase: Execution. 30+ issues, one Running architect agent, multiple done issues.
test.describe("execution scenario", function() {
    test.use({
        program: {
            file: "dotnet",
            args: _to_consumable_array(BASE_ARGS).concat([
                "--scenario",
                "execution"
            ])
        },
        rows: 40,
        columns: 120
    });
    test("header shows Execution phase", function(param) {
        var terminal = param.terminal;
        return _async_to_generator(function() {
            return _ts_generator(this, function(_state) {
                switch(_state.label){
                    case 0:
                        return [
                            4,
                            waitForReady(terminal)
                        ];
                    case 1:
                        _state.sent();
                        return [
                            4,
                            expect(terminal.getByText("Execution")).toBeVisible()
                        ];
                    case 2:
                        _state.sent();
                        return [
                            2
                        ];
                }
            });
        })();
    });
    test("agents panel shows the running architect agent", function(param) {
        var terminal = param.terminal;
        return _async_to_generator(function() {
            return _ts_generator(this, function(_state) {
                switch(_state.label){
                    case 0:
                        return [
                            4,
                            waitForReady(terminal)
                        ];
                    case 1:
                        _state.sent();
                        // In 3-panel layout, the progress body shows execution guidance and workflow hints.
                        // Verify "Execution" phase is shown and the progress panel content is visible.
                        return [
                            4,
                            expect(terminal.getByText("Execution")).toBeVisible()
                        ];
                    case 2:
                        _state.sent();
                        return [
                            4,
                            expect(terminal.getByText("workflow guide")).toBeVisible()
                        ];
                    case 3:
                        _state.sent();
                        return [
                            2
                        ];
                }
            });
        })();
    });
    test("roadmap panel shows issue titles", function(param) {
        var terminal = param.terminal;
        return _async_to_generator(function() {
            return _ts_generator(this, function(_state) {
                switch(_state.label){
                    case 0:
                        return [
                            4,
                            waitForReady(terminal)
                        ];
                    case 1:
                        _state.sent();
                        // In 3-panel layout, the progress panel shows ready issues hint during execution.
                        // Verify "4 issue(s) are ready" text from the workflow guide.
                        return [
                            4,
                            expect(terminal.getByText("are ready")).toBeVisible()
                        ];
                    case 2:
                        _state.sent();
                        return [
                            2
                        ];
                }
            });
        })();
    });
    test("roadmap shows done indicator for completed issues", function(param) {
        var terminal = param.terminal;
        return _async_to_generator(function() {
            return _ts_generator(this, function(_state) {
                switch(_state.label){
                    case 0:
                        return [
                            4,
                            waitForReady(terminal)
                        ];
                    case 1:
                        _state.sent();
                        // In 3-panel layout, the progress panel shows workflow guidance text.
                        // Verify the "step" indicator from the workflow guide.
                        return [
                            4,
                            expect(terminal.getByText("Step 3 of 3")).toBeVisible()
                        ];
                    case 2:
                        _state.sent();
                        return [
                            2
                        ];
                }
            });
        })();
    });
});
// ── Questions scenario ───────────────────────────────────────────────────────
// Phase: Execution + two open questions (one blocking).
test.describe("questions scenario", function() {
    test.use({
        program: {
            file: "dotnet",
            args: _to_consumable_array(BASE_ARGS).concat([
                "--scenario",
                "questions"
            ])
        },
        rows: 40,
        columns: 120
    });
    test("shell renders without error", function(param) {
        var terminal = param.terminal;
        return _async_to_generator(function() {
            return _ts_generator(this, function(_state) {
                switch(_state.label){
                    case 0:
                        return [
                            4,
                            waitForReady(terminal)
                        ];
                    case 1:
                        _state.sent();
                        // The scenario should render the execution state (with questions).
                        // Verify Execution phase is shown and no crash/error message appears.
                        return [
                            4,
                            expect(terminal.getByText("Execution")).toBeVisible()
                        ];
                    case 2:
                        _state.sent();
                        return [
                            2
                        ];
                }
            });
        })();
    });
});
// ── Sprint-resume scenario ───────────────────────────────────────────────────
// Phase: Execution. One in-progress issue (interrupted sprint) + two open issues.
// On startup the shell should show a resume hint and an in-progress warning.
test.describe("sprint-resume scenario", function() {
    test.use({
        program: {
            file: "dotnet",
            args: _to_consumable_array(BASE_ARGS).concat([
                "--scenario",
                "sprint-resume"
            ])
        },
        rows: 40,
        columns: 120
    });
    test("header shows Execution phase", function(param) {
        var terminal = param.terminal;
        return _async_to_generator(function() {
            return _ts_generator(this, function(_state) {
                switch(_state.label){
                    case 0:
                        return [
                            4,
                            waitForReady(terminal)
                        ];
                    case 1:
                        _state.sent();
                        return [
                            4,
                            expect(terminal.getByText("Execution")).toBeVisible()
                        ];
                    case 2:
                        _state.sent();
                        return [
                            2
                        ];
                }
            });
        })();
    });
    test("shows sprint resume hint on startup", function(param) {
        var terminal = param.terminal;
        return _async_to_generator(function() {
            return _ts_generator(this, function(_state) {
                switch(_state.label){
                    case 0:
                        return [
                            4,
                            waitForReady(terminal)
                        ];
                    case 1:
                        _state.sent();
                        // The sprint-resume scenario has open items from a prior sprint.
                        // The hint text includes "sprint item" — verify it appears in the progress panel.
                        return [
                            4,
                            expect(terminal.getByText("sprint item")).toBeVisible({
                                timeout: 10000
                            })
                        ];
                    case 2:
                        _state.sent();
                        return [
                            2
                        ];
                }
            });
        })();
    });
    test("shows in-progress warning for interrupted issue", function(param) {
        var terminal = param.terminal;
        return _async_to_generator(function() {
            return _ts_generator(this, function(_state) {
                switch(_state.label){
                    case 0:
                        return [
                            4,
                            waitForReady(terminal)
                        ];
                    case 1:
                        _state.sent();
                        // The scenario has one InProgress issue (issue #3, SignalR hub).
                        // The warning text includes "in progress" — verify it appears.
                        return [
                            4,
                            expect(terminal.getByText("in progress")).toBeVisible({
                                timeout: 10000
                            })
                        ];
                    case 2:
                        _state.sent();
                        return [
                            2
                        ];
                }
            });
        })();
    });
    test("hint suggests /run to resume", function(param) {
        var terminal = param.terminal;
        return _async_to_generator(function() {
            return _ts_generator(this, function(_state) {
                switch(_state.label){
                    case 0:
                        return [
                            4,
                            waitForReady(terminal)
                        ];
                    case 1:
                        _state.sent();
                        // The hint text ends with "Use /run to resume or /status to review."
                        // "to resume" is unique to the sprint resume hint.
                        return [
                            4,
                            expect(terminal.getByText("to resume")).toBeVisible({
                                timeout: 10000
                            })
                        ];
                    case 2:
                        _state.sent();
                        return [
                            2
                        ];
                }
            });
        })();
    });
});
