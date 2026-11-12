using System.Text.RegularExpressions;
using TaskFlow.Application.Chatbot.KnowledgeBase;

namespace TaskFlow.Application.Chatbot.Dialogue;

/// <summary>
/// Turns a KB step into what the user sees: one text variant picked at random, labels in the session locale and
/// <c>{support}</c>/<c>{help}</c> filled from the link values. Pass <paramref name="links"/> to override the KB's own
/// <c>Settings.Links</c> (for example the configured contact values); a placeholder without a value stays as <c>{name}</c>.
/// </summary>
public sealed partial class LocalizedReplyProvider(
    IKnowledgeBase kb,
    IVariantPicker picker,
    IReadOnlyDictionary<string, string>? links = null)
{
    private readonly IReadOnlyDictionary<string, string> _links = links ?? kb.Settings.Links;

    public string PickText(DialogStep step, string locale)
    {
        ArgumentNullException.ThrowIfNull(step);
        var variants = step.Text[locale];
        return Render(variants[picker.Pick(variants.Count)]);
    }

    /// <summary>Buttons the user can see and click: those whose url is an unset link are hidden.</summary>
    public IReadOnlyList<ReplyOption> VisibleQuickReplies(DialogStep step, string locale)
    {
        ArgumentNullException.ThrowIfNull(step);
        var options = new List<ReplyOption>();
        foreach (var reply in step.QuickReplies)
        {
            var url = ResolveUrl(reply);
            if (!string.IsNullOrEmpty(reply.Url) && url is null)
            {
                continue;
            }

            options.Add(new ReplyOption(reply.Id, Render(reply.Label[locale]), url));
        }

        return options;
    }

    /// <summary>The reply's url with placeholders filled, or null when it has none or it is an unset link.</summary>
    public string? ResolveUrl(QuickReply reply)
    {
        ArgumentNullException.ThrowIfNull(reply);
        if (string.IsNullOrEmpty(reply.Url))
        {
            return null;
        }

        var unresolved = false;
        var url = PlaceholderRegex().Replace(reply.Url, m =>
        {
            if (_links.TryGetValue(m.Groups[1].Value, out var value) && !string.IsNullOrEmpty(value))
            {
                return value;
            }

            unresolved = true;
            return m.Value;
        });
        return unresolved ? null : url;
    }

    private string Render(string text) =>
        PlaceholderRegex().Replace(text, m =>
            _links.TryGetValue(m.Groups[1].Value, out var value) && !string.IsNullOrEmpty(value) ? value : m.Value);

    [GeneratedRegex(@"\{(\w+)\}")]
    private static partial Regex PlaceholderRegex();
}
