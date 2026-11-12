namespace TaskFlow.Infrastructure.Chatbot;

/// <summary>Settings under <c>Chatbot:Model</c>.</summary>
public sealed class ChatbotModelOptions
{
    public const string SectionName = "Chatbot:Model";

    /// <summary>Path of the trained model zip. When empty or missing, the embedded resource is tried.</summary>
    public string? Path { get; set; }

    /// <summary>Logical name of an embedded resource in the Infrastructure assembly holding the model zip.</summary>
    public string EmbeddedResource { get; set; } = "TaskFlow.Chatbot/intent-model.zip";
}
