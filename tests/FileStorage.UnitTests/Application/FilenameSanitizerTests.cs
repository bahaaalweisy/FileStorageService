using FileStorage.Application.Common;
using FluentAssertions;
using Xunit;

namespace FileStorage.UnitTests.Application;

public class FilenameSanitizerTests
{
    [Theory]
    [InlineData("../../etc/passwd", "passwd")]
    [InlineData("..\\..\\windows\\system32\\evil.exe", "evil.exe")]
    [InlineData("a/b/c/report.pdf", "report.pdf")]
    [InlineData("a\\b\\c\\report.pdf", "report.pdf")]
    public void Sanitize_StripsPathComponents(string input, string expected)
    {
        FilenameSanitizer.Sanitize(input, 255).Should().Be(expected);
    }

    [Fact]
    public void Sanitize_RemovesControlCharacters_IncludingCrLf()
    {
        var input = "evil\r\nSet-Cookie: hijacked=true.txt";
        var result = FilenameSanitizer.Sanitize(input, 255);

        result.Should().NotContain("\r").And.NotContain("\n");
    }

    [Fact]
    public void Sanitize_TruncatesToMaxLength()
    {
        var input = new string('a', 500) + ".txt";
        var result = FilenameSanitizer.Sanitize(input, 50);

        result.Length.Should().Be(50);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("....")]
    public void Sanitize_FallsBackToDefaultName_WhenInputIsUnusable(string? input)
    {
        FilenameSanitizer.Sanitize(input, 255).Should().Be(FilenameSanitizer.FallbackName);
    }

    [Fact]
    public void ToContentDispositionValue_ProducesAsciiFallbackAndUtf8Star()
    {
        var value = FilenameSanitizer.ToContentDispositionValue("café.png");

        value.Should().Contain("filename=").And.Contain("filename*=UTF-8''caf%C3%A9.png");
    }
}
