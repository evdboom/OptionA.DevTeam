using RazorConsole.Core.Abstractions.Rendering;
using RazorConsole.Core.Rendering.Translation.Contexts;
using RazorConsole.Core.Vdom;
using Spectre.Console.Rendering;

namespace DevTeam.Cli.Shell;

/// <summary>
/// Translation middleware that renders a raw Spectre.Console markup string
/// without the auto-escaping that the built-in Markup component performs.
/// Handles any <span data-spectre-markup="..." /> element emitted by RawMarkup.razor.
/// </summary>
internal sealed class RawSpectreMarkupTranslator : ITranslationMiddleware
{
    public IRenderable Translate(TranslationContext context, TranslationDelegate next, VNode node)
    {
        if (node.Kind != VNodeKind.Element
            || !string.Equals(node.TagName, "span", StringComparison.OrdinalIgnoreCase)
            || !node.Attributes.TryGetValue("data-spectre-markup", out var markup))
        {
            return next(node);
        }

        return new Spectre.Console.Markup(markup ?? "");
    }
}
