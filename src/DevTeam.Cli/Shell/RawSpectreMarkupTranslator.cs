using RazorConsole.Core.Abstractions.Rendering;
using RazorConsole.Core.Rendering.Translation.Contexts;
using RazorConsole.Core.Vdom;
using Spectre.Console;
using Spectre.Console.Rendering;
using System.Reflection;

namespace DevTeam.Cli.Shell;

/// <summary>
/// Translation middleware that renders raw Spectre.Console markup strings and panels
/// without the auto-escaping that the built-in Markup component performs.
/// </summary>
internal sealed class RawSpectreMarkupTranslator : ITranslationMiddleware
{
    public IRenderable Translate(TranslationContext context, TranslationDelegate next, VNode node)
    {
        if (node.Kind != VNodeKind.Element)
            return next(node);

        // <span data-spectre-markup="..."> — emitted by RawMarkup.razor
        if (string.Equals(node.TagName, "span", StringComparison.OrdinalIgnoreCase)
            && node.Attributes.TryGetValue("data-spectre-markup", out var markup))
        {
            return new Markup(markup ?? "");
        }

        // <div class="panel" data-header="..." data-border-color="..."> — emitted by RazorConsole Panel component
        if (string.Equals(node.TagName, "div", StringComparison.OrdinalIgnoreCase)
            && node.Attributes.TryGetValue("class", out var cls)
            && cls == "panel")
        {
            return TranslatePanel(context, node);
        }

        return next(node);
    }

    private static IRenderable TranslatePanel(TranslationContext context, VNode node)
    {
        IRenderable content = node.Children.Count == 0
            ? new Text("")
            : context.Translate(node.Children[0]);

        var panel = new Panel(content);

        if (node.Attributes.TryGetValue("data-header", out var header) && !string.IsNullOrEmpty(header))
            panel.Header = new PanelHeader(header);

        if (node.Attributes.TryGetValue("data-border-color", out var colorName) && !string.IsNullOrEmpty(colorName))
        {
            var colorProp = typeof(Color).GetProperty(colorName,
                BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase);
            if (colorProp != null)
                panel.BorderStyle = new Style(foreground: (Color)colorProp.GetValue(null)!);
        }

        node.Attributes.TryGetValue("data-expand", out var expandStr);
        panel.Expand = expandStr == "true";

        return panel;
    }
}
