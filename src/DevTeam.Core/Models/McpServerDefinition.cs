namespace DevTeam.Core;

public sealed class McpServerDefinition
{
    public string Name { get; set; } = "";
    public string Command { get; set; } = "";
    public List<string> Args { get; set; } = [];
    public string? Cwd { get; set; }
    public string Description { get; set; } = "";
    public bool Enabled { get; set; } = true;
}
