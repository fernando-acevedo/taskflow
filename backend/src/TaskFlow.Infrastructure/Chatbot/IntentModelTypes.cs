using Microsoft.ML.Data;

namespace TaskFlow.Infrastructure.Chatbot;

internal sealed class IntentRow
{
    public string Text { get; set; } = "";

    public string Label { get; set; } = "";
}

internal sealed class IntentScores
{
    [ColumnName("Score")]
    public float[] Score { get; set; } = [];
}
