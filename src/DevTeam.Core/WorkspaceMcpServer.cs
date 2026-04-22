using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DevTeam.Core;

[SuppressMessage("Major Code Smell", "S1192", Justification = "JSON-RPC and schema property names are protocol literals.")]
public sealed class WorkspaceMcpServer(
    string workspacePath,
    Func<int, string?, CancellationToken, Task<string>>? subAgentRunner = null)
{
    private static readonly IEqualityComparer<string> PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _workspacePath = Path.GetFullPath(workspacePath);
    private readonly Func<int, string?, CancellationToken, Task<string>>? _subAgentRunner = subAgentRunner;

    public async Task RunAsync(Stream input, Stream output, CancellationToken cancellationToken = default)
    {
        using var reader = new StreamReader(input, Encoding.UTF8, false, 4096, leaveOpen: true);
        using var writer = new StreamWriter(output, new UTF8Encoding(false), 4096, leaveOpen: true)
        {
            AutoFlush = true
        };

        while (!cancellationToken.IsCancellationRequested)
        {
            var payload = await ReadMessageAsync(reader, cancellationToken);
            if (payload is null)
            {
                break;
            }

            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            if (!root.TryGetProperty("method", out var methodElement))
            {
                continue;
            }

            var method = methodElement.GetString() ?? "";
            if (method.StartsWith("notifications/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            JsonNode? response;
            try
            {
                response = await HandleRequestAsync(root, method, cancellationToken);
            }
            catch (Exception ex)
            {
                response = CreateErrorResponse(root, -32000, ex.Message);
            }

            if (response is not null)
            {
                await WriteMessageAsync(writer, response.ToJsonString(JsonOptions), cancellationToken);
            }
        }
    }

    private async Task<JsonNode?> HandleRequestAsync(JsonElement root, string method, CancellationToken cancellationToken)
    {
        return method switch
        {
            "initialize" => CreateSuccessResponse(root, new JsonObject
            {
                ["protocolVersion"] = "2024-11-05",
                ["capabilities"] = new JsonObject
                {
                    ["tools"] = new JsonObject()
                },
                ["serverInfo"] = new JsonObject
                {
                    ["name"] = "devteam-workspace",
                    ["version"] = "0.1.15"
                }
            }),
            "ping" => CreateSuccessResponse(root, new JsonObject()),
            "tools/list" => CreateSuccessResponse(root, new JsonObject
            {
                ["tools"] = BuildToolDefinitions()
            }),
            "tools/call" => CreateSuccessResponse(root, await HandleToolCallAsync(root, cancellationToken)),
            _ => CreateErrorResponse(root, -32601, $"Method '{method}' is not supported.")
        };
    }

    private async Task<JsonNode> HandleToolCallAsync(JsonElement root, CancellationToken cancellationToken)
    {
        var parameters = root.TryGetProperty("params", out var paramsElement)
            ? paramsElement
            : throw new InvalidOperationException("Missing params.");
        var toolName = parameters.GetProperty("name").GetString()
            ?? throw new InvalidOperationException("Missing tool name.");
        var arguments = parameters.TryGetProperty("arguments", out var argsElement)
            ? argsElement
            : default;

        return toolName switch
        {
            "get_workspace_summary" => BuildToolResult(WithWorkspace((runtime, state) => runtime.BuildWorkspaceSnapshot(state))),
            "list_ready_issues" => BuildToolResult(WithWorkspace((runtime, state) =>
                runtime.GetExecutionCandidatesPreview(state))),
            "select_execution_batch" => BuildToolResult(WithWorkspace((runtime, state) =>
            {
                var selection = runtime.SetExecutionSelection(
                    state,
                    GetIntList(arguments, "issueIds"),
                    GetRequiredString(arguments, "rationale"),
                    GetOptionalString(arguments, "sessionId"),
                    GetInt(arguments, "maxSubagents", 4));
                return new { selection.SelectedIssueIds, selection.Rationale, selection.SessionId };
            }, save: true)),
            "create_issue" => BuildToolResult(WithWorkspace((runtime, state) =>
            {
                var issue = runtime.AddIssue(
                    state,
                    new IssueRequest
                    {
                        Title = GetRequiredString(arguments, "title", minLength: 3, maxLength: 200),
                        Detail = GetString(arguments, "detail", maxLength: 20_000),
                        RoleSlug = GetString(arguments, "roleSlug", "developer", maxLength: 64),
                        Priority = GetInt(arguments, "priority", 50),
                        RoadmapItemId = GetNullableInt(arguments, "roadmapItemId"),
                        DependsOn = GetIntList(arguments, "dependsOn"),
                        Area = GetOptionalString(arguments, "area", maxLength: 128),
                        FamilyKey = GetOptionalString(arguments, "familyKey", maxLength: 128),
                        ParentIssueId = GetNullableInt(arguments, "parentIssueId"),
                        PipelineId = GetNullableInt(arguments, "pipelineId"),
                        PipelineStageIndex = GetNullableInt(arguments, "pipelineStageIndex"),
                        ComplexityHint = GetNullableInt(arguments, "complexityHint")
                    });
                return new { issue.Id, issue.Title, issue.RoleSlug, issue.Area, issue.Priority, issue.ComplexityHint };
            }, save: true)),
            "create_question" => BuildToolResult(WithWorkspace((runtime, state) =>
            {
                var question = runtime.AddQuestion(
                    state,
                    GetRequiredString(arguments, "text", minLength: 3, maxLength: 10_000),
                    GetBool(arguments, "blocking", true));
                return new { question.Id, question.Text, question.IsBlocking };
            }, save: true)),
            "remember_decision" => BuildToolResult(WithWorkspace((runtime, state) =>
            {
                var decision = runtime.RecordDecision(
                    state,
                    GetRequiredString(arguments, "title", minLength: 3, maxLength: 200),
                    GetRequiredString(arguments, "detail", minLength: 3, maxLength: 20_000),
                    GetString(arguments, "source", "workspace-mcp", maxLength: 80),
                    GetNullableInt(arguments, "issueId"),
                    GetNullableInt(arguments, "runId"),
                    GetOptionalString(arguments, "sessionId", maxLength: 200));
                return new { decision.Id, decision.Title, decision.Source };
            }, save: true)),
            "spawn_agent" when _subAgentRunner is not null => BuildToolResult(
                await _subAgentRunner(
                    GetInt(arguments, "issueId", 0),
                    GetOptionalString(arguments, "contextHint", maxLength: 2_000),
                    cancellationToken)),
            "get_runtime_capabilities" => BuildToolResult(BuildRuntimeCapabilities()),
            "update_issue_status" => BuildToolResult(WithWorkspace((runtime, state) =>
            {
                var issue = DevTeamRuntime.UpdateIssueStatus(
                    state,
                    GetInt(arguments, "issueId", 0),
                    GetRequiredString(arguments, "status", maxLength: 32),
                    GetOptionalString(arguments, "notes", maxLength: 10_000));
                return new { issue.Id, issue.Title, Status = issue.Status.ToString(), issue.Notes };
            }, save: true)),
            "get_issue" => BuildToolResult(WithWorkspace((_, state) =>
            {
                var issue = DevTeamRuntime.GetIssue(state, GetInt(arguments, "issueId", 0));
                return new
                {
                    issue.Id,
                    issue.Title,
                    issue.Detail,
                    issue.Area,
                    issue.RoleSlug,
                    issue.Priority,
                    Status = issue.Status.ToString(),
                    RefinementState = issue.RefinementState.ToString(),
                    FilesInScope = GetSafeFilesInScope(issue.FilesInScope),
                    issue.LinkedDecisionIds,
                    issue.DependsOnIssueIds,
                    issue.ParentIssueId,
                    issue.ComplexityHint,
                    issue.Notes
                };
            })),
            "get_decisions" => BuildToolResult(WithWorkspace((_, state) =>
            {
                var ids = GetIntList(arguments, "decisionIds");
                var decisions = DevTeamRuntime.GetDecisions(state, ids);
                return new
                {
                    decisions = decisions.Select(d => new
                    {
                        d.Id,
                        d.Title,
                        d.Detail,
                        d.Source,
                        d.IssueId,
                        CreatedAtUtc = d.CreatedAtUtc.ToString("O")
                    }).ToList()
                };
            })),
            _ => throw new InvalidOperationException($"Tool '{toolName}' is not supported.")
        };
    }

    private JsonArray BuildToolDefinitions()
    {
        var tools = new List<JsonNode?>
        {
            BuildToolDefinition(
                "get_workspace_summary",
                "Read the current DevTeam workspace snapshot including issues, questions, decisions, and pipelines.",
                new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject(),
                    ["additionalProperties"] = false
                }),
            BuildToolDefinition(
                "list_ready_issues",
                "List ready issue leads that the scheduler could start, grouped by pipeline.",
                new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                    },
                    ["additionalProperties"] = false
                }),
            BuildToolDefinition(
                "select_execution_batch",
                "Persist the orchestrator's selected ready issue leads for the next execution batch.",
                new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["issueIds"] = new JsonObject
                        {
                            ["type"] = "array",
                            ["items"] = IntegerSchema()
                        },
                        ["rationale"] = StringSchema(),
                        ["sessionId"] = StringSchema(),
                        ["maxSubagents"] = IntegerSchema()
                    },
                    ["required"] = new JsonArray("issueIds", "rationale"),
                    ["additionalProperties"] = false
                }),
            BuildToolDefinition(
                "create_issue",
                "Create a new DevTeam issue in the workspace backlog.",
                new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["title"] = StringSchema(),
                        ["detail"] = StringSchema(),
                        ["roleSlug"] = StringSchema(),
                        ["priority"] = IntegerSchema(),
                        ["area"] = StringSchema(),
                        ["familyKey"] = StringSchema(),
                        ["roadmapItemId"] = IntegerSchema(),
                        ["parentIssueId"] = IntegerSchema(),
                        ["pipelineId"] = IntegerSchema(),
                        ["pipelineStageIndex"] = IntegerSchema(),
                        ["dependsOn"] = new JsonObject
                        {
                            ["type"] = "array",
                            ["items"] = IntegerSchema()
                        },
                        ["complexityHint"] = new JsonObject
                        {
                            ["type"] = "integer",
                            ["description"] = "Optional 0–100 complexity score. 0=trivial, 100=very complex/cross-cutting. Orchestrator uses this to decide whether to inject a navigator preflight issue."
                        }
                    },
                    ["required"] = new JsonArray("title"),
                    ["additionalProperties"] = false
                }),
            BuildToolDefinition(
                "create_question",
                "Create a blocking or non blocking question for the user.",
                new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["text"] = StringSchema(),
                        ["blocking"] = new JsonObject { ["type"] = "boolean" }
                    },
                    ["required"] = new JsonArray("text"),
                    ["additionalProperties"] = false
                }),
            BuildToolDefinition(
                "remember_decision",
                "Persist a durable decision note into the workspace history.",
                new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["title"] = StringSchema(),
                        ["detail"] = StringSchema(),
                        ["source"] = StringSchema(),
                        ["issueId"] = IntegerSchema(),
                        ["runId"] = IntegerSchema(),
                        ["sessionId"] = StringSchema()
                    },
                    ["required"] = new JsonArray("title", "detail"),
                    ["additionalProperties"] = false
                }),
            BuildToolDefinition(
                "get_issue",
                "Fetch a single issue by id, including its FilesInScope and LinkedDecisionIds set during refinement. Use this instead of get_workspace_summary when you only need to work on a specific issue.",
                new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["issueId"] = IntegerSchema()
                    },
                    ["required"] = new JsonArray("issueId"),
                    ["additionalProperties"] = false
                }),
            BuildToolDefinition(
                "get_decisions",
                "Fetch specific decision records by id. Use the LinkedDecisionIds from get_issue to retrieve only the decisions relevant to the current issue, rather than reading all decisions.",
                new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["decisionIds"] = new JsonObject
                        {
                            ["type"] = "array",
                            ["items"] = IntegerSchema()
                        }
                    },
                    ["required"] = new JsonArray("decisionIds"),
                    ["additionalProperties"] = false
                }),
            BuildToolDefinition(
                "get_runtime_capabilities",
                "Describe which concerns are fully managed by the DevTeam runtime so agents know what NOT to ask the user about.",
                new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject(),
                    ["additionalProperties"] = false
                }),
            BuildToolDefinition(
                "update_issue_status",
                "Update the status of an existing issue. Use this instead of editing workspace state files directly.",
                new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["issueId"] = IntegerSchema(),
                        ["status"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["enum"] = new JsonArray("open", "in-progress", "done", "blocked")
                        },
                        ["notes"] = StringSchema()
                    },
                    ["required"] = new JsonArray("issueId", "status"),
                    ["additionalProperties"] = false
                })
        };

        if (_subAgentRunner is not null)
        {
            tools.Add(BuildToolDefinition(
                "spawn_agent",
                "Execute a single ready issue as a child agent session and return the result synchronously. Use this to run a specific issue directly within the current session rather than waiting for the next loop iteration. Optional contextHint is supplemental caller context only: use it to pass concise context not yet captured in the issue, not to replace issue/decsion MCP context or inject a broad custom prompt.",
                new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["issueId"] = IntegerSchema(),
                        ["contextHint"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["description"] = "Optional supplemental caller context. Keep it short and focused. It is a convenience hint for missing context, not a replacement for get_issue/get_decisions or a way to bypass scoped execution."
                        }
                    },
                    ["required"] = new JsonArray("issueId"),
                    ["additionalProperties"] = false
                }));
        }

        return new JsonArray([.. tools]);
    }

    private static JsonObject BuildToolDefinition(string name, string description, JsonObject schema) =>
        new()
        {
            ["name"] = name,
            ["description"] = description,
            ["inputSchema"] = schema
        };
    private static object BuildRuntimeCapabilities() => new
    {
        ManagedConcerns = new[]
        {
            new
            {
                Area = "Budget and model selection",
                Description = "The runtime enforces credit caps (total and premium), selects the appropriate model tier for each issue based on available budget and budget-pressure thresholds, and falls back to cheaper tiers automatically. Agents must never ask the user whether to switch model tiers, enforce credit caps, or restrict premium usage."
            },
            new
            {
                Area = "Phase transitions",
                Description = "The runtime is the sole authority for moving the workspace through phases (Planning → ArchitectPlanning → Execution). Agents must not ask the user to trigger or approve phase changes."
            },
            new
            {
                Area = "Issue status tracking",
                Description = "Issue status (open, in-progress, done, blocked) must be updated via the update_issue_status MCP tool. Agents must never edit workspace state files (.devteam/state/*.json) directly. If the workspace state appears stale or conflicting, trust the runtime's authoritative copy and call update_issue_status to record the intended final status."
            },
            new
            {
                Area = "Run lifecycle",
                Description = "The runtime queues, starts, marks complete, and fails agent runs. Agents do not need to manage run records."
            },
            new
            {
                Area = "Pipeline scheduling",
                Description = "When pipeline scheduling is enabled, the runtime automatically chains architect → developer → tester stages. Agents should not manually create the entire chain; create only the next-stage issue when a handoff is truly required."
            },
            new
            {
                Area = "Question routing",
                Description = "Questions created via create_question are routed to the user by the runtime. Agents must not ask users questions interactively; all user input requests must go through create_question."
            },
            new
            {
                Area = "Approval gates",
                Description = "Plan and architect-plan approvals are managed by the user through the interactive shell. Agents must not prompt for or block on approval."
            }
        },
        Guidance = "Do NOT create a question for any concern listed above. If you encounter a situation covered by one of these areas (e.g. budget pressure, a stale issue status, a phase-change event), handle it using the appropriate MCP tool or simply record a decision noting what you observed."
    };
    private static JsonObject StringSchema() => new() { ["type"] = "string" };

    private static JsonObject IntegerSchema() => new() { ["type"] = "integer" };

    private T WithWorkspace<T>(Func<DevTeamRuntime, WorkspaceState, T> action, bool save = false)
    {
        var store = new WorkspaceStore(_workspacePath);
        var runtime = new DevTeamRuntime();
        var state = store.Load();
        var result = action(runtime, state);
        if (save)
        {
            store.Save(state);
        }

        return result;
    }

    private static JsonNode BuildToolResult<T>(T payload)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        return new JsonObject
        {
            ["content"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "text",
                    ["text"] = json
                }
            }
        };
    }

    private static JsonNode CreateSuccessResponse(JsonElement root, JsonNode result)
    {
        var idNode = root.TryGetProperty("id", out var idElement)
            ? JsonNode.Parse(idElement.GetRawText())
            : null;
        return new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = idNode,
            ["result"] = result
        };
    }

    private static JsonNode CreateErrorResponse(JsonElement root, int code, string message)
    {
        var idNode = root.TryGetProperty("id", out var idElement)
            ? JsonNode.Parse(idElement.GetRawText())
            : null;
        return new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = idNode,
            ["error"] = new JsonObject
            {
                ["code"] = code,
                ["message"] = message
            }
        };
    }

    private static async Task<string?> ReadMessageAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var contentLength = 0;
        while (true)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                return null;
            }

            if (line.Length == 0)
            {
                break;
            }

            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
            {
                contentLength = int.Parse(line["Content-Length:".Length..].Trim());
            }
        }

        if (contentLength <= 0 || contentLength > 10_000_000)
        {
            return null;
        }

        var buffer = new char[contentLength];
        var read = 0;
        while (read < contentLength)
        {
            var chunk = await reader.ReadAsync(buffer.AsMemory(read, contentLength - read), cancellationToken);
            if (chunk == 0)
            {
                break;
            }

            read += chunk;
        }

        if (read != contentLength)
        {
            return null;
        }

        return new string(buffer, 0, read);
    }

    private static async Task WriteMessageAsync(StreamWriter writer, string payload, CancellationToken cancellationToken)
    {
        var length = Encoding.UTF8.GetByteCount(payload);
        await writer.WriteAsync($"Content-Length: {length}\r\n\r\n");
        await writer.WriteAsync(payload);
        await writer.FlushAsync(cancellationToken);
    }

    private static string GetRequiredString(JsonElement element, string propertyName, int minLength = 1, int maxLength = 4096)
    {
        var value = GetString(element, propertyName, maxLength: maxLength);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Argument '{propertyName}' is required.");
        }

        if (value.Trim().Length < minLength)
        {
            throw new InvalidOperationException($"Argument '{propertyName}' must be at least {minLength} characters.");
        }

        return value;
    }

    private static string GetString(JsonElement element, string propertyName, string fallback = "", int maxLength = 4096)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var value))
        {
            return fallback;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            return fallback;
        }

        var parsed = value.GetString() ?? fallback;
        if (parsed.Length > maxLength)
        {
            throw new InvalidOperationException($"Argument '{propertyName}' exceeds maximum length of {maxLength} characters.");
        }

        return parsed;
    }

    private static string? GetOptionalString(JsonElement element, string propertyName, int maxLength = 4096)
    {
        var value = GetString(element, propertyName, maxLength: maxLength);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static int GetInt(JsonElement element, string propertyName, int fallback)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var value))
        {
            return fallback;
        }

        return value.TryGetInt32(out var parsed) ? parsed : fallback;
    }

    private static int? GetNullableInt(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.TryGetInt32(out var parsed) ? parsed : null;
    }

    private static bool GetBool(JsonElement element, string propertyName, bool fallback)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var value))
        {
            return fallback;
        }

        return value.ValueKind == JsonValueKind.True || (value.ValueKind != JsonValueKind.False && fallback);
    }

    private static IReadOnlyList<int> GetIntList(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return value.EnumerateArray()
            .Select(item => item.TryGetInt32(out var parsed) ? parsed : 0)
            .Where(item => item > 0)
            .ToList();
    }

    private static IReadOnlyList<string> GetSafeFilesInScope(IReadOnlyList<string> files)
    {
        return files
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path.Trim().Replace('\\', '/'))
            .Where(path => !path.StartsWith("../", StringComparison.Ordinal) && !path.StartsWith("/", StringComparison.Ordinal))
            .Distinct(PathComparer)
            .ToList();
    }
}
