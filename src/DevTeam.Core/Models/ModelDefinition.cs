namespace DevTeam.Core;

public sealed class ModelDefinition
{
    public string Name { get; set; } = "";
    public double Cost { get; set; }
    public bool IsDefault { get; set; }
    public bool IsPremium { get; set; }
}
