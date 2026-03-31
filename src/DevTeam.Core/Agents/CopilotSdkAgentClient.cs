using System.Text;
using GitHub.Copilot.SDK;

namespace DevTeam.Core;

public sealed class CopilotSdkAgentClient : IAgentClient
{
    public string Name => "copilot-sdk";

    public async Task<AgentInvocationResult> InvokeAsync(AgentInvocationRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt))
        {
            throw new InvalidOperationException("Prompt is required.");
        }

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var sawDelta = false;
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var clientOptions = new CopilotClientOptions
        {
            Cwd = request.WorkingDirectory,
            CliPath = CopilotCliPathResolver.Resolve(),
            CliArgs = request.ExtraArguments.ToArray()
        };

        await using var client = new CopilotClient(clientOptions);
        await client.StartAsync(cancellationToken);

        var sessionConfig = WorkspaceMcpSessionConfigFactory.BuildSessionConfig(request);

        await using var session = await client.CreateSessionAsync(sessionConfig, cancellationToken);
        using var subscription = session.On(evt =>
        {
            switch (evt)
            {
                case AssistantMessageDeltaEvent delta when !string.IsNullOrWhiteSpace(delta.Data?.DeltaContent):
                    sawDelta = true;
                    stdout.Append(delta.Data?.DeltaContent);
                    break;
                case AssistantMessageEvent message when !sawDelta && !string.IsNullOrWhiteSpace(message.Data?.Content):
                    stdout.Append(message.Data?.Content);
                    break;
                case SessionErrorEvent error when !string.IsNullOrWhiteSpace(error.Data?.Message):
                    stderr.AppendLine(error.Data?.Message);
                    done.TrySetResult();
                    break;
                case SessionIdleEvent:
                    done.TrySetResult();
                    break;
            }
        });

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(request.Timeout);
        await session.SendAsync(new MessageOptions { Prompt = request.Prompt }, timeoutCts.Token);
        await done.Task.WaitAsync(timeoutCts.Token);

        return new AgentInvocationResult
        {
            BackendName = Name,
            SessionId = session.SessionId,
            ExitCode = stderr.Length == 0 ? 0 : 1,
            StdOut = stdout.ToString(),
            StdErr = stderr.ToString()
        };
    }
}