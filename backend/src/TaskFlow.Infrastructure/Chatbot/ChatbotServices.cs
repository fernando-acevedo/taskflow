using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaskFlow.Application.Chatbot;
using TaskFlow.Application.Chatbot.Dialogue;
using TaskFlow.Application.Chatbot.KnowledgeBase;

namespace TaskFlow.Infrastructure.Chatbot;

public static class ChatbotServices
{
    /// <summary>
    /// Registers the chatbot (ADR 0026): the validated knowledge base, the intent classifier, the text helpers and the
    /// dialogue engine, all singletons. The KB is loaded and validated when the host starts, so a bad KB stops the API.
    /// </summary>
    public static IServiceCollection AddChatbot(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ChatbotOptions>().Bind(configuration.GetSection(ChatbotOptions.SectionName)).ValidateOnStart();
        services.AddSingleton<IValidateOptions<ChatbotOptions>, ChatbotOptionsValidator>();
        services.Configure<ChatbotModelOptions>(configuration.GetSection(ChatbotModelOptions.SectionName));

        services.AddSingleton<IKnowledgeBase>(_ => JsonKnowledgeBaseLoader.LoadFromAssembly(typeof(ChatbotServices).Assembly));
        services.AddSingleton<IIntentClassifier, MlNetIntentClassifier>();
        services.AddSingleton(provider => new TextNormalizer(provider.GetRequiredService<IKnowledgeBase>().Synonyms));
        services.AddSingleton<IVariantPicker, RandomVariantPicker>();
        services.AddSingleton(provider =>
        {
            var kb = provider.GetRequiredService<IKnowledgeBase>();
            var options = provider.GetRequiredService<IOptions<ChatbotOptions>>().Value;
            var replies = new LocalizedReplyProvider(kb, provider.GetRequiredService<IVariantPicker>(), Links(kb, options));
            return new DialogueEngine(kb, replies);
        });
        services.AddHostedService<ChatbotStartupCheck>();
        return services;
    }

    /// <summary>The KB's declared link names with the configured values for support and help (empty stays unset).</summary>
    internal static Dictionary<string, string> Links(IKnowledgeBase kb, ChatbotOptions options)
    {
        var links = new Dictionary<string, string>(kb.Settings.Links, StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(options.SupportContact))
        {
            links["support"] = options.SupportContact.Trim();
        }

        if (!string.IsNullOrWhiteSpace(options.HelpUrl))
        {
            links["help"] = options.HelpUrl.Trim();
        }

        return links;
    }
}

/// <summary>Builds the chatbot services at startup so an invalid KB or contact setting fails fast, not on the first chat.</summary>
internal sealed partial class ChatbotStartupCheck(IServiceProvider services, ILogger<ChatbotStartupCheck> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = services.GetRequiredService<IKnowledgeBase>();
        _ = services.GetRequiredService<DialogueEngine>();
        if (!services.GetRequiredService<IIntentClassifier>().IsLoaded)
        {
            ModelNotLoaded(logger);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Warning, Message = "The chatbot intent model is not loaded; every free-text message will get the fallback answer.")]
    private static partial void ModelNotLoaded(ILogger logger);
}
