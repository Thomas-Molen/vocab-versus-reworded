using Wordset.Application.Services;
using Wordset.Domain.Exceptions;
using Xunit;

namespace Wordset.Tests.Unit;

public class ValidateWordTests
{
    private const string ShareCode = "ABCDEF";

    private static Domain.Entities.Wordset MakeWordset() =>
        new() { Name = "Test", ShareCode = ShareCode };

    private static WordGameService MakeService(
        Domain.Entities.Wordset? wordset = null,
        string? exactMatch = null,
        string? fuzzyMatch = null)
    {
        var wordsetRepo = new StubWordsetRepository(existing: wordset);
        var gameRepo = new StubWordGameRepository(exactMatch: exactMatch, fuzzyMatch: fuzzyMatch);
        var cache = new StubShareCodeCache();
        return new WordGameService(wordsetRepo, gameRepo, cache);
    }

    [Fact]
    public async Task ValidateWordAsync_ThrowsWordsetNotFoundException_WhenWordsetDoesNotExist()
    {
        var service = MakeService();

        await Assert.ThrowsAsync<WordsetNotFoundException>(
            () => service.ValidateWordAsync("ZZZZZZ", "apple", 0));
    }

    [Fact]
    public async Task ValidateWordAsync_ReturnsValid_WhenExactMatchFound()
    {
        var wordset = MakeWordset();
        var service = MakeService(wordset, exactMatch: "apple");

        var result = await service.ValidateWordAsync(ShareCode, "apple", 0);

        Assert.True(result.Valid);
        Assert.Equal("apple", result.MatchedWord);
    }

    [Fact]
    public async Task ValidateWordAsync_ReturnsInvalid_WhenExactMatchNotFound()
    {
        var wordset = MakeWordset();
        var service = MakeService(wordset, exactMatch: null);

        var result = await service.ValidateWordAsync(ShareCode, "apple", 0);

        Assert.False(result.Valid);
        Assert.Equal(string.Empty, result.MatchedWord);
    }

    [Fact]
    public async Task ValidateWordAsync_ReturnsValid_WhenFuzzyMatchFound()
    {
        var wordset = MakeWordset();
        var service = MakeService(wordset, fuzzyMatch: "apple");

        var result = await service.ValidateWordAsync(ShareCode, "aple", 1);

        Assert.True(result.Valid);
        Assert.Equal("apple", result.MatchedWord);
    }

    [Fact]
    public async Task ValidateWordAsync_NormalizesToLowercase_BeforeMatch()
    {
        var wordset = MakeWordset();
        var wordsetRepo = new StubWordsetRepository(existing: wordset);
        var gameRepo = new StubWordGameRepository(exactMatch: "apple");
        var cache = new StubShareCodeCache();
        var service = new WordGameService(wordsetRepo, gameRepo, cache);

        // "APPLE" should be normalised to "apple" before the exact match
        var result = await service.ValidateWordAsync(ShareCode, "APPLE", 0);

        Assert.True(result.Valid);
    }

    [Fact]
    public async Task ValidateWordAsync_StripsSpaces_BeforeMatch()
    {
        var wordset = MakeWordset();
        var wordsetRepo = new StubWordsetRepository(existing: wordset);

        string? passedWord = null;
        var gameRepo = new CapturingWordGameRepository(
            onExactMatch: (word) => { passedWord = word; return "icecream"; });
        var cache = new StubShareCodeCache();
        var service = new WordGameService(wordsetRepo, gameRepo, cache);

        await service.ValidateWordAsync(ShareCode, "ice cream", 0);

        Assert.Equal("icecream", passedWord);
    }

    [Fact]
    public async Task ValidateWordAsync_StripsHyphens_BeforeMatch()
    {
        var wordset = MakeWordset();
        var wordsetRepo = new StubWordsetRepository(existing: wordset);

        string? passedWord = null;
        var gameRepo = new CapturingWordGameRepository(
            onExactMatch: (word) => { passedWord = word; return "selfmade"; });
        var cache = new StubShareCodeCache();
        var service = new WordGameService(wordsetRepo, gameRepo, cache);

        await service.ValidateWordAsync(ShareCode, "self-made", 0);

        Assert.Equal("selfmade", passedWord);
    }

    [Fact]
    public async Task ValidateWordAsync_ThrowsArgumentException_WhenWordIsEmpty()
    {
        var wordset = MakeWordset();
        var service = MakeService(wordset);

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => service.ValidateWordAsync(ShareCode, "", 0));

        Assert.Equal("word", ex.ParamName);
    }

    [Fact]
    public async Task ValidateWordAsync_ThrowsArgumentOutOfRangeException_WhenFuzzyToleranceIsNegative()
    {
        var wordset = MakeWordset();
        var service = MakeService(wordset);

        var ex = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => service.ValidateWordAsync(ShareCode, "apple", -1));

        Assert.Equal("fuzzyTolerance", ex.ParamName);
    }

    [Fact]
    public async Task StripExcludedChars_RemovesSpacesAndHyphens()
    {
        Assert.Equal("icecream", WordGameService.StripExcludedChars("ice cream"));
        Assert.Equal("selfmade", WordGameService.StripExcludedChars("self-made"));
        Assert.Equal("word", WordGameService.StripExcludedChars("word"));
    }
}
