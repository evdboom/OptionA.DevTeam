namespace DevTeam.Core;

public class CopilotCliAgentClient(ICommandRunner? runner = null) : IAgentClient
{
    private readonly ICommandRunner _runner = runner ?? new ProcessCommandRunner();

    public string Name => "copilot-cli";

    public async Task<AgentInvocationResult> InvokeAsync(AgentInvocationRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt))
        {
            throw new InvalidOperationException("Prompt is required.");
        }

        if (request.Provider is not null)
        {
            throw new InvalidOperationException("Provider overrides are only supported by the sdk backend.");
        }

        var arguments = new List<string>();
        foreach (var argument in request.ExtraArguments)
        {
            arguments.Add(argument);
        }

        if (!string.IsNullOrWhiteSpace(request.Model))
        {
            arguments.Add("--model");
            arguments.Add(request.Model);
        }

        arguments.Add("--no-ask-user");
        arguments.Add("-p");
        arguments.Add(request.Prompt);

        var result = await _runner.RunAsync(
            new CommandExecutionSpec
            {
                FileName = "copilot",
                Arguments = arguments,
                WorkingDirectory = request.WorkingDirectory,
                Timeout = request.Timeout
            },
            cancellationToken);

        return new AgentInvocationResult
        {
            BackendName = Name,
            SessionId = request.SessionId ?? "",
            ExitCode = result.ExitCode,
            StdOut = result.StdOut,
            StdErr = result.StdErr
        };
    }
}
