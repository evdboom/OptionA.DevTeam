namespace DevTeam.Core;

public interface IConfigurationLoader
{
    List<ModelDefinition> LoadModels(string repoRoot);
    List<ProviderDefinition> LoadProviders(string repoRoot);
    List<RoleDefinition> LoadRoles(string repoRoot);
    List<ModeDefinition> LoadModes(string repoRoot);
    List<SuperpowerDefinition> LoadSuperpowers(string repoRoot);
    List<McpServerDefinition> LoadMcpServers(string repoRoot);
}
