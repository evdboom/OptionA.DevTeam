namespace DevTeam.Core;

public sealed class RuntimeConfiguration
{
    public string ActiveModeSlug { get; set; } = "develop";
    public bool KeepAwakeEnabled { get; set; }
    public bool WorkspaceMcpEnabled { get; set; } = true;
    public bool PipelineSchedulingEnabled { get; set; } = true;
    public bool AutoApproveEnabled { get; set; }
    public string WorkspaceMcpServerName { get; set; } = "devteam-workspace";
    public List<string> DefaultPipelineRoles { get; set; } = ["architect", "developer", "tester"];
    public int DefaultMaxIterations { get; set; } = 15;
    public int DefaultMaxSubagents { get; set; } = 4;

    public static RuntimeConfiguration CreateDefault() => new();
}