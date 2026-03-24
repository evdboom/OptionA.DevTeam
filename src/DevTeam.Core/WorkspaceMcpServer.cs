using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DevTeam.Core;

public sealed class WorkspaceMcpServer(string workspacePath)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _workspacePath = Path.GetFullPath(workspacePath);

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
                response = HandleRequest(root, method);
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

    private JsonNode? HandleRequest(JsonElement root, string method)
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
            "tools/call" => CreateSuccessResponse(root, HandleToolCall(root)),
            _ => CreateErrorResponse(root, -32601, $"Method '{method}' is not supported.")
        };
    }

    private JsonNode HandleToolCall(JsonElement root)
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
                    GetInt(arguments, "maxSubagents", 1));
                return new { selection.SelectedIssueIds, selection.Rationale, selection.SessionId };
            }, save: true)),
            "create_issue" => BuildToolResult(WithWorkspace((runtime, state) =>
            {
                var issue = runtime.AddIssue(
                    state,
                    GetRequiredString(arguments, "title"),
                    GetString(arguments, "detail"),
                    GetString(arguments, "roleSlug", "developer"),
                    GetInt(arguments, "priority", 50),
                    GetNullableInt(arguments, "roadmapItemId"),
                    GetIntList(arguments, "dependsOn"),
                    GetOptionalString(arguments, "area"),
                    GetOptionalString(arguments, "familyKey"),
                    GetNullableInt(arguments, "parentIssueId"),
                    GetNullableInt(arguments, "pipelineId"),
                    GetNullableInt(arguments, "pipelineStageIndex"));
                return new { issue.Id, issue.Title, issue.RoleSlug, issue.Area, issue.Priority };
            }, save: true)),
            "create_question" => BuildToolResult(WithWorkspace((runtime, state) =>
            {
                var question = runtime.AddQuestion(
                    state,
                    GetRequiredString(arguments, "text"),
                    GetBool(arguments, "blocking", true));
                return new { question.Id, question.Text, question.IsBlocking };
            }, save: true)),
            "remember_decision" => BuildToolResult(WithWorkspace((runtime, state) =>
            {
                var decision = runtime.RecordDecision(
                    state,
                    GetRequiredString(arguments, "title"),
                    GetRequiredString(arguments, "detail"),
                    GetString(arguments, "source", "workspace-mcp"),
                    GetNullableInt(arguments, "issueId"),
                    GetNullableInt(arguments, "runId"),
                    GetOptionalString(arguments, "sessionId"));
                return new { decision.Id, decision.Title, decision.Source };
            }, save: true)),
            "get_runtime_capabilities" => BuildToolResult(BuildRuntimeCapabilities()),
            "update_issue_status" => BuildToolResult(WithWorkspace((runtime, state) =>
            {
                var issue = runtime.UpdateIssueStatus(
                    state,
                    GetInt(arguments, "issueId", 0),
                    GetRequiredString(arguments, "status"),
                    GetOptionalString(arguments, "notes"));
                return new { issue.Id, issue.Title, Status = issue.Status.ToString(), issue.Notes };
            }, save: true)),
            _ => throw new InvalidOperationException($"Tool '{toolName}' is not supported.")
        };
    }

    private JsonArray BuildToolDefinitions()
    {
        return
        [
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
        ];
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

    private static string GetRequiredString(JsonElement element, string propertyName)
    {
        var value = GetString(element, propertyName);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Argument '{propertyName}' is required.");
        }

        return value;
    }

    private static string GetString(JsonElement element, string propertyName, string fallback = "")
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var value))
        {
            return fallback;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() ?? fallback : fallback;
    }

    private static string? GetOptionalString(JsonElement element, string propertyName)
    {
        var value = GetString(element, propertyName);
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
}
