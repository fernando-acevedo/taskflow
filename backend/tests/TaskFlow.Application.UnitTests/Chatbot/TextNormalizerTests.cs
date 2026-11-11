using Shouldly;
using TaskFlow.Application.Chatbot;

namespace TaskFlow.Application.UnitTests.Chatbot;

public class TextNormalizerTests
{
    private readonly TextNormalizer _normalizer = new(TextNormalizer.DefaultSynonyms);

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    public void BlankReturnsEmpty(string? input, string expected) =>
        _normalizer.Normalize(input).ShouldBe(expected);

    [Fact]
    public void RemovesDiacriticsAndLowercases() =>
        _normalizer.Normalize("MAÑANA Pingüino Árbol").ShouldBe("manana pinguino arbol");

    [Fact]
    public void PunctuationBecomesSpacesAndWhitespaceCollapses() =>
        _normalizer.Normalize("  hello,,   world!!  (test)  42 ").ShouldBe("hello world test 42");

    [Fact]
    public void TyposAreUntouched() =>
        _normalizer.Normalize("pasword resett").ShouldBe("pasword resett");

    [Fact]
    public void ReplacesSynonymsInBothLanguages() =>
        _normalizer.Normalize("Olvidé mi contraseña, quiero restablecer").ShouldBe("olvide mi password quiero reset");

    [Fact]
    public void MultiWordSynonymsAreSupported() =>
        _normalizer.Normalize("I can't Sign In, account locked out").ShouldBe("i can t login account locked");

    [Fact]
    public void LongestVariantWins() =>
        _normalizer.Normalize("correo electronico").ShouldBe("email");

    [Fact]
    public void SynonymsMatchWholeWordsOnly() =>
        _normalizer.Normalize("passage claves mailbox").ShouldBe("passage claves mailbox");

    [Fact]
    public void HyphenatedVariantIsMatchedAfterNormalization() =>
        _normalizer.Normalize("my E-Mail").ShouldBe("my email");

    [Fact]
    public void SameResultForAccentedAndPlainInput() =>
        _normalizer.Normalize("iniciar sesión").ShouldBe(_normalizer.Normalize("INICIAR SESION"));
}
