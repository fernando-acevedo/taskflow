using Shouldly;
using TaskFlow.Application.Chatbot;

namespace TaskFlow.Application.UnitTests.Chatbot;

public class LanguageGuesserTests
{
    [Theory]
    [InlineData("How do I reset my password?", "en")]
    [InlineData("I can't log in to the app", "en")]
    [InlineData("¿Cómo puedo cambiar mi contraseña?", "es")]
    [InlineData("No puedo entrar con mi usuario", "es")]
    [InlineData("mañana", "es")]
    [InlineData("", "unknown")]
    [InlineData(null, "unknown")]
    [InlineData("kanban", "unknown")]
    [InlineData("la the", "unknown")]
    public void Guesses(string? text, string expected) =>
        LanguageGuesser.Guess(text).ShouldBe(expected);
}
