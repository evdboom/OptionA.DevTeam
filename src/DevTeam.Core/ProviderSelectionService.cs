namespace DevTeam.Core;

public static class ProviderSelectionService
{
    public static IReadOnlyList<string> GetConfiguredProviderNames(WorkspaceState state) =>
        state.Providers
            .Select(provider => provider.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static ProviderDefinition? ResolveProvider(WorkspaceState state, string? modelName, string? providerOverrideName = null)
    {
        var selectedName = !string.IsNullOrWhiteSpace(providerOverrideName)
            ? providerOverrideName
            : !string.IsNullOrWhiteSpace(state.Runtime.DefaultProviderName)
                ? state.Runtime.DefaultProviderName
                : state.Models.FirstOrDefault(model => string.Equals(model.Name, modelName, StringComparison.OrdinalIgnoreCase))?.ProviderName;

        return string.IsNullOrWhiteSpace(selectedName)
            ? null
            : GetRequiredProvider(state, selectedName);
    }

    public static ProviderDefinition GetRequiredProvider(WorkspaceState state, string providerName)
    {
        if (string.IsNullOrWhiteSpace(providerName))
        {
            throw new InvalidOperationException("Provider name must not be empty.");
        }

        var provider = state.Providers.FirstOrDefault(item => string.Equals(item.Name, providerName, StringComparison.OrdinalIgnoreCase));
        if (provider is null)
        {
            throw new InvalidOperationException(
                $"Unknown provider '{providerName}'. Configure it in .devteam-source/PROVIDERS.json or reset the provider override.");
        }

        return provider;
    }

    public static void SetDefaultProvider(WorkspaceState state, string? providerName)
    {
        state.Runtime.DefaultProviderName = string.IsNullOrWhiteSpace(providerName)
            ? ""
            : GetRequiredProvider(state, providerName).Name;
    }
}
