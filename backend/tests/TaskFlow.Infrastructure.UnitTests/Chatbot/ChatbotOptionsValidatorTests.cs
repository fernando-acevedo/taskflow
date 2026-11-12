using Microsoft.Extensions.Hosting;
using TaskFlow.Infrastructure.Chatbot;

namespace TaskFlow.Infrastructure.UnitTests.Chatbot;

public class ChatbotOptionsValidatorTests
{
    private static ChatbotOptionsValidator For(string environment) =>
        new(new FakeEnvironment { EnvironmentName = environment });

    [Fact]
    public void DevelopmentDoesNotRequireTheLinks()
    {
        For(Environments.Development).Validate(null, new ChatbotOptions()).Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Testing")]
    public void OutsideDevelopmentBothLinksAreRequired(string environment)
    {
        var result = For(environment).Validate(null, new ChatbotOptions());

        result.Failed.ShouldBeTrue();
        result.Failures.ShouldContain(f => f.Contains("Chatbot:SupportContact", StringComparison.Ordinal));
        result.Failures.ShouldContain(f => f.Contains("Chatbot:HelpUrl", StringComparison.Ordinal));
    }

    [Fact]
    public void HelpUrlMustBeAnAbsoluteHttpUrl()
    {
        var result = For("Production").Validate(null, new ChatbotOptions { SupportContact = "a@b.test", HelpUrl = "/help" });

        result.Failures.ShouldHaveSingleItem().ShouldContain("absolute http");
    }

    [Fact]
    public void ValidValuesPassOutsideDevelopment()
    {
        var options = new ChatbotOptions { SupportContact = "a@b.test", HelpUrl = "https://example.test/help" };

        For("Production").Validate(null, options).Succeeded.ShouldBeTrue();
    }

    private sealed class FakeEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "";

        public string ApplicationName { get; set; } = "Tests";

        public string ContentRootPath { get; set; } = "";

        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
