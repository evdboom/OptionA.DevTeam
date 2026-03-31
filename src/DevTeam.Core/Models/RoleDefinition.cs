namespace DevTeam.Core;

public sealed class RoleDefinition
{
    public string Slug { get; set; } = "";
    public string Name { get; set; } = "";
    public string SuggestedModel { get; set; } = "";
    public string SourcePath { get; set; } = "";
    public string Body { get; set; } = "";
    public List<string> RequiredTools { get; set; } = [];
}
