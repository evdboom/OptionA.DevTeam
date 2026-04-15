//# hash=51b6b0368447fd281e4353e0d32ebd1a
//# sourceMappingURL=shell-commands.test.js.map

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
var BUILD_TIMEOUT = 120000;
test.use({
    program: {
        file: "dotnet",
        args: cliArgs("start", "--workspace", ".devteam-e2e-commands")
    },
    rows: 40,
    columns: 120
});
test("unknown command shows error with /help hint", function(param) {
    var terminal = param.terminal;
    return _async_to_generator(function() {
        return _ts_generator(this, function(_state) {
            switch(_state.label){
                case 0:
                    return [
                        4,
                        expect(terminal.getByText("help for commands")).toBeVisible({
                            timeout: BUILD_TIMEOUT
                        })
                    ];
                case 1:
                    _state.sent();
                    terminal.submit("/xyzzy-not-a-real-command");
                    // "Unknown command" appears in the error line. Allow time for the async
                    // command processor (Task.Run in SpectreShellHost) to complete and re-render.
                    return [
                        4,
                        expect(terminal.getByText("Unknown command")).toBeVisible({
                            timeout: 10000
                        })
                    ];
                case 2:
                    _state.sent();
                    // "Type /help." is unique to the error message (unlike "/help" which also
                    // appears in the startup banner "· /help for commands ·").
                    return [
                        4,
                        expect(terminal.getByText("Type /help.")).toBeVisible({
                            timeout: 5000
                        })
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
test("history shows previously submitted commands", function(param) {
    var terminal = param.terminal;
    return _async_to_generator(function() {
        return _ts_generator(this, function(_state) {
            switch(_state.label){
                case 0:
                    return [
                        4,
                        expect(terminal.getByText("help for commands")).toBeVisible({
                            timeout: BUILD_TIMEOUT
                        })
                    ];
                case 1:
                    _state.sent();
                    // Submit a couple of commands that will land in history
                    terminal.submit("/help");
                    return [
                        4,
                        expect(terminal.getByText("@role")).toBeVisible()
                    ];
                case 2:
                    _state.sent();
                    terminal.submit("/history");
                    // /help must appear in the history output
                    return [
                        4,
                        expect(terminal.getByText("/help")).toBeVisible()
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
test("bug command produces a report in the progress panel", function(param) {
    var terminal = param.terminal;
    return _async_to_generator(function() {
        return _ts_generator(this, function(_state) {
            switch(_state.label){
                case 0:
                    return [
                        4,
                        expect(terminal.getByText("help for commands")).toBeVisible({
                            timeout: BUILD_TIMEOUT
                        })
                    ];
                case 1:
                    _state.sent();
                    terminal.submit("/bug");
                    // The bug report is added as a panel titled "bug report".
                    // Its separator line "── bug report ──" is rendered in the progress panel.
                    // The report content is long and ## Environment is near the top (scrolled off at
                    // follow-latest); instead verify the last section header which stays in the viewport.
                    return [
                        4,
                        expect(terminal.getByText("Recent agent runs")).toBeVisible({
                            timeout: 15000
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
test("exit command terminates the shell", function(param) {
    var terminal = param.terminal;
    return _async_to_generator(function() {
        return _ts_generator(this, function(_state) {
            switch(_state.label){
                case 0:
                    return [
                        4,
                        expect(terminal.getByText("help for commands")).toBeVisible({
                            timeout: BUILD_TIMEOUT
                        })
                    ];
                case 1:
                    _state.sent();
                    terminal.submit("/exit");
                    // After /exit the underlying process should terminate within a few seconds
                    return [
                        4,
                        new Promise(function(resolve, reject) {
                            var timeout = setTimeout(function() {
                                return reject(new Error("Shell did not exit within 10 seconds"));
                            }, 10000);
                            terminal.onExit(function() {
                                clearTimeout(timeout);
                                resolve();
                            });
                            // Also resolve immediately if already exited
                            if (terminal.exitResult !== null) {
                                clearTimeout(timeout);
                                resolve();
                            }
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
test("PageUp shows scroll hint when history is long", function(param) {
    var terminal = param.terminal;
    return _async_to_generator(function() {
        return _ts_generator(this, function(_state) {
            switch(_state.label){
                case 0:
                    return [
                        4,
                        expect(terminal.getByText("help for commands")).toBeVisible({
                            timeout: BUILD_TIMEOUT
                        })
                    ];
                case 1:
                    _state.sent();
                    // Generate enough output that the progress panel overflows
                    terminal.submit("/help");
                    return [
                        4,
                        expect(terminal.getByText("@role")).toBeVisible()
                    ];
                case 2:
                    _state.sent();
                    terminal.keyPress(Key.PageUp);
                    // After scrolling up the panel header should show the scrolled indicator
                    return [
                        4,
                        expect(terminal.getByText("scrolled")).toBeVisible()
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
test("End key returns to follow-latest mode after scrolling", function(param) {
    var terminal = param.terminal;
    return _async_to_generator(function() {
        return _ts_generator(this, function(_state) {
            switch(_state.label){
                case 0:
                    return [
                        4,
                        expect(terminal.getByText("help for commands")).toBeVisible({
                            timeout: BUILD_TIMEOUT
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
                    terminal.keyPress(Key.PageUp);
                    return [
                        4,
                        expect(terminal.getByText("scrolled")).toBeVisible()
                    ];
                case 3:
                    _state.sent();
                    terminal.keyPress(Key.End);
                    // Progress header goes back to non-scrolled title
                    return [
                        4,
                        expect(terminal.getByText("Progress")).toBeVisible()
                    ];
                case 4:
                    _state.sent();
                    // The scrolled indicator must be gone
                    return [
                        4,
                        expect(terminal.getByText("scrolled")).not.toBeVisible()
                    ];
                case 5:
                    _state.sent();
                    return [
                        2
                    ];
            }
        });
    })();
});
// ── /worktrees command (requires a workspace — use ui-harness) ──────────────
// The /worktrees command needs an existing workspace state to read/write.
// Run these tests using the ui-harness execution scenario to provide state.
test.describe("worktrees command (ui-harness)", function() {
    test.use({
        program: {
            file: "dotnet",
            args: cliArgs("ui-harness", "--scenario", "execution")
        },
        rows: 40,
        columns: 120
    });
    test("worktrees on shows enabled confirmation", function(param) {
        var terminal = param.terminal;
        return _async_to_generator(function() {
            return _ts_generator(this, function(_state) {
                switch(_state.label){
                    case 0:
                        // Wait for Phase: header which signals the harness is ready
                        return [
                            4,
                            expect(terminal.getByText("Phase:")).toBeVisible({
                                timeout: BUILD_TIMEOUT
                            })
                        ];
                    case 1:
                        _state.sent();
                        terminal.submit("/worktrees on");
                        return [
                            4,
                            expect(terminal.getByText("Worktree mode enabled")).toBeVisible({
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
    test("worktrees off shows disabled confirmation", function(param) {
        var terminal = param.terminal;
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
                        terminal.submit("/worktrees off");
                        return [
                            4,
                            expect(terminal.getByText("Worktree mode disabled")).toBeVisible({
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
