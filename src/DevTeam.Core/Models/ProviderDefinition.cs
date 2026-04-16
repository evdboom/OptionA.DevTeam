namespace DevTeam.Core;

public sealed class ProviderDefinition
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string BaseUrl { get; set; } = "";
    public string WireApi { get; set; } = "";
    public string ApiKeyEnvVar { get; set; } = "";
    public string BearerTokenEnvVar { get; set; } = "";
    public string AzureApiVersion { get; set; } = "";
}
