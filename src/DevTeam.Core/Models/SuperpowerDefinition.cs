namespace DevTeam.Core;

public sealed class SuperpowerDefinition
{
    public string Slug { get; set; } = "";
    public string Name { get; set; } = "";
    public string SourcePath { get; set; } = "";
    public string Body { get; set; } = "";
    public List<string> RequiredTools { get; set; } = [];
}
