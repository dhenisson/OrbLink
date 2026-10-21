using AMS.Transfer.Core;

namespace AMS.Transfer.Tests;

public sealed class FileNameSafetyTests
{
    [Fact]
    public void RemovesPathTraversal()
    {
        var result = FileNameSafety.Sanitize(@"..\..\proposta.pdf");
        Assert.Equal("proposta.pdf", result);
        Assert.DoesNotContain("..", result);
    }

    [Fact]
    public void ReplacesInvalidCharacters()
    {
        var result = FileNameSafety.Sanitize("relatório<2026>.pdf");
        Assert.Equal("relatório_2026_.pdf", result);
    }

    [Fact]
    public void CreatesDuplicateSuffixWithoutOverwriting()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ams-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "foto.png"), "first");
            Assert.Equal(Path.Combine(root, "foto (1).png"), FileNameSafety.GetUniquePath(root, "foto.png"));
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("CON.txt", "_CON.txt")]
    [InlineData("LPT¹.pdf", "_LPT¹.pdf")]
    [InlineData("aux", "_aux")]
    [InlineData(@"C:\fixture\ação (1) 日本語 😀.png", "ação (1) 日本語 😀.png")]
    [InlineData("/../proposta.pdf", "proposta.pdf")]
    [InlineData("  ação ç é 😀.txt", "  ação ç é 😀.txt")]
    [InlineData("nome. ", "nome")]
    [InlineData(".gitignore", ".gitignore")]
    public void WindowsNamesAreSafeWithoutLosingValidUnicode(string input, string expected) =>
        Assert.Equal(expected, FileNameSafety.Sanitize(input));

    [Fact]
    public void LongNamesAndExtensionsRemainBounded()
    {
        foreach (var input in new[] { string.Concat(Enumerable.Repeat("😀", 140)) + ".txt", "x." + new string('a', 500) })
        {
            var safe = FileNameSafety.Sanitize(input);
            Assert.InRange(safe.Length, 1, 180);
            Assert.DoesNotContain('�', safe);
            Assert.False(char.IsHighSurrogate(safe[^1]));
        }
    }

    [Fact]
    public async Task ConcurrentCommitsNeverOverwrite()
    {
        var root = Path.Combine(Path.GetTempPath(), $"orblink-names-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var results = await Task.WhenAll(Enumerable.Range(0, 16).Select(index => Task.Run(() =>
            {
                var temp = Path.Combine(root, $"{Guid.NewGuid():N}.part");
                File.WriteAllText(temp, index.ToString());
                return FileNameSafety.MoveToUniquePath(temp, root, "proposta.pdf");
            })));
            Assert.Equal(16, results.Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.Equal(16, results.Select(File.ReadAllText).Distinct().Count());
            Assert.Contains(Path.Combine(root, "proposta (1).pdf"), results);
            Assert.Empty(Directory.GetFiles(root, "*.part"));
        }
        finally { Directory.Delete(root, true); }
    }
}
