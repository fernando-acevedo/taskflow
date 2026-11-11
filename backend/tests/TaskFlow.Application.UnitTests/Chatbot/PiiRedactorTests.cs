using Shouldly;
using TaskFlow.Application.Chatbot;

namespace TaskFlow.Application.UnitTests.Chatbot;

public class PiiRedactorTests
{
    [Theory]
    [InlineData("write to ana.perez@example.com please", "write to [email] please")]
    [InlineData("see https://taskflow.example/reset#token=abc now", "see [url] now")]
    [InlineData("call 1234567 or 12345", "call [number] or 12345")]
    [InlineData("my password is Hunter2!", "my password is [secret]")]
    [InlineData("mi contraseña es abc123", "mi contraseña es [secret]")]
    [InlineData("mi clave es zeta9 gracias", "mi clave es [secret] gracias")]
    [InlineData("pwd: s3cr3t", "pwd: [secret]")]
    [InlineData("clave: xyz", "clave: [secret]")]
    [InlineData("my password Hunter2", "my password [secret]")]
    public void Redacts(string input, string expected) =>
        PiiRedactor.Redact(input).ShouldBe(expected);

    [Theory]
    [InlineData("How do I reset my password?")]
    [InlineData("I forgot my password")]
    [InlineData("my password does not work")]
    [InlineData("No puedo iniciar sesión")]
    [InlineData("")]
    public void LeavesPlainSentencesUntouched(string input) =>
        PiiRedactor.Redact(input).ShouldBe(input);

    [Fact]
    public void NullReturnsEmpty() => PiiRedactor.Redact(null).ShouldBe("");

    [Fact]
    public void DoesNotThrowOnPathologicalInput()
    {
        var input = new string('a', 100_000) + "@" + new string('.', 50_000) + "password is " + new string('9', 100_000);
        Should.NotThrow(() => PiiRedactor.Redact(input));
    }
}
