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
                runtime.GetReadyIssuesPreview(state, GetInt(arguments, "maxSubagents", 3)))),
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
                        ["maxSubagents"] = new JsonObject
                        {
                            ["type"] = "integer",
                            ["minimum"] = 1
                        }
                    },
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

        if (contentLength <= 0)
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
