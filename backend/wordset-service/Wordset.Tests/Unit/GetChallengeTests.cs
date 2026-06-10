using Wordset.Application.Services;
using Wordset.Domain.Exceptions;
using Xunit;

namespace Wordset.Tests.Unit;

public class GetChallengeTests
{
    private const string ShareCode = "ABCDEF";

    private static Domain.Entities.Wordset MakeWordset() =>
        new() { Name = "Test", ShareCode = ShareCode };

    private static WordGameService MakeService(
        Domain.Entities.Wordset? wordset = null,
        IEnumerable<string>? randomWords = null)
    {
        var wordsetRepo = new StubWordsetRepository(existing: wordset);
        var gameRepo = new StubWordGameRepository(words: randomWords);
        var cache = new StubShareCodeCache();
        return new WordGameService(wordsetRepo, gameRepo, cache);
    }

    [Fact]
    public async Task GetChallengeAsync_ThrowsWordsetNotFoundException_WhenWordsetDoesNotExist()
    {
        var service = MakeService();

        await Assert.ThrowsAsync<WordsetNotFoundException>(
            () => service.GetChallengeAsync("ZZZZZZ", 2));
    }

    [Fact]
    public async Task GetChallengeAsync_ThrowsArgumentOutOfRangeException_WhenLetterCountIsZero()
    {
        var wordset = MakeWordset();
        var service = MakeService(wordset, ["apple"]);

        var ex = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => service.GetChallengeAsync(ShareCode, 0));

        Assert.Equal("letterCount", ex.ParamName);
    }

    [Fact]
    public async Task GetChallengeAsync_ReturnsCorrectLetterCount()
    {
        var wordset = MakeWordset();
        var service = MakeService(wordset, ["strawberry"]);

        var result = await service.GetChallengeAsync(ShareCode, 3);

        Assert.Equal(3, result.Letters.Count);
    }

    [Fact]
    public async Task GetChallengeAsync_LettersAreDistinct()
    {
        var wordset = MakeWordset();
        var service = MakeService(wordset, ["strawberry"]);

        var result = await service.GetChallengeAsync(ShareCode, 3);

        Assert.Equal(result.Letters.Count, result.Letters.Distinct().Count());
    }

    [Fact]
    public async Task GetChallengeAsync_NeverReturnsExcludedChars()
    {
        var wordset = MakeWordset();
        // Word contains spaces and hyphens — those should never appear in the challenge
        var service = MakeService(wordset, ["ice-cream cone"]);

        for (int i = 0; i < 20; i++)
        {
            var result = await service.GetChallengeAsync(ShareCode, 2);
            Assert.DoesNotContain(" ", result.Letters);
            Assert.DoesNotContain("-", result.Letters);
        }
    }

    [Fact]
    public async Task GetChallengeAsync_ThrowsInvalidOperationException_WhenNoWordQualifies()
    {
        var wordset = MakeWordset();
        // Only excluded chars — no eligible characters at all
        var service = MakeService(wordset, ["- -"]);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.GetChallengeAsync(ShareCode, 2));
    }

    [Fact]
    public async Task GetChallengeAsync_ThrowsInvalidOperationException_WhenWordTooShort()
    {
        var wordset = MakeWordset();
        // Word has only 2 distinct eligible chars; requesting 3 should fail
        var service = MakeService(wordset, ["ab"]);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.GetChallengeAsync(ShareCode, 3));
    }

    [Fact]
    public async Task GetChallengeAsync_AllReturnedCharsAreFromTheWord()
    {
        var wordset = MakeWordset();
        const string word = "triangle";
        var service = MakeService(wordset, [word]);

        var result = await service.GetChallengeAsync(ShareCode, 4);

        foreach (var letter in result.Letters)
            Assert.Contains(letter[0], word);
    }

    [Fact]
    public async Task GetChallengeAsync_UsesCachedWordsetId_OnSecondCall()
    {
        var wordset = MakeWordset();
        var wordsetRepo = new StubWordsetRepository(existing: wordset);
        var gameRepo = new StubWordGameRepository(words: ["triangle"]);
        var cache = new StubShareCodeCache();
        var service = new WordGameService(wordsetRepo, gameRepo, cache);

        await service.GetChallengeAsync(ShareCode, 2);
        Assert.True(cache.Contains(ShareCode));

        // Second call should resolve from cache
        await service.GetChallengeAsync(ShareCode, 2);
        Assert.Equal(1, wordsetRepo.GetByShareCodeCallCount);
    }
}
