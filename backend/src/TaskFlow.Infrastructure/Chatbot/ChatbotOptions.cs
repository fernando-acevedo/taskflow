using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace TaskFlow.Infrastructure.Chatbot;

/// <summary>
/// Settings under <c>Chatbot</c>. They are the values of the <c>{support}</c> and <c>{help}</c> placeholders of the
/// knowledge base (ADR 0026): the KB files never hold real contact data.
/// </summary>
public sealed class ChatbotOptions
{
    public const string SectionName = "Chatbot";

    /// <summary>Support contact (an email address) shown for <c>{support}</c>.</summary>
    public string? SupportContact { get; set; }

    /// <summary>URL of the help page shown for <c>{help}</c>.</summary>
    public string? HelpUrl { get; set; }
}

/// <summary>Both values are required outside Development, so the API does not start without them.</summary>
public sealed class ChatbotOptionsValidator(IHostEnvironment environment) : IValidateOptions<ChatbotOptions>
{
    public ValidateOptionsResult Validate(string? name, ChatbotOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (environment.IsDevelopment())
        {
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();
        if (string.IsNullOrWhiteSpace(options.SupportContact))
        {
            failures.Add("Chatbot:SupportContact is required outside Development (the {support} placeholder of the chatbot).");
        }

        if (string.IsNullOrWhiteSpace(options.HelpUrl))
        {
            failures.Add("Chatbot:HelpUrl is required outside Development (the {help} placeholder of the chatbot).");
        }
        else if (!Uri.TryCreate(options.HelpUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            failures.Add("Chatbot:HelpUrl must be an absolute http or https URL.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
