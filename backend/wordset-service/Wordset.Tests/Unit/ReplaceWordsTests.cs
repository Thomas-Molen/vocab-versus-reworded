using Wordset.Application.Models;
using Wordset.Application.Services;
using Wordset.Domain.Exceptions;
using Xunit;

namespace Wordset.Tests.Unit;

public class ReplaceWordsTests
{
    private const string ShareCode = "ABCDEF";

    private static Domain.Entities.Wordset MakeWordset() =>
        new() { Name = "Test", ShareCode = ShareCode };

    [Fact]
    public async Task ReplaceWordsAsync_ThrowsWordsetNotFoundException_WhenWordsetDoesNotExist()
    {
        var repo = new StubWordsetRepository();
        var service = new WordService(repo);

        await Assert.ThrowsAsync<WordsetNotFoundException>(
            () => service.ReplaceWordsAsync("ZZZZZZ", new ReplaceWordsRequest(["apple"])));
    }

    [Fact]
    public async Task ReplaceWordsAsync_NormalizesToLowercase()
    {
        var wordset = MakeWordset();
        var repo = new StubWordsetRepository(existing: wordset);
        var service = new WordService(repo);

        await service.ReplaceWordsAsync(ShareCode, new ReplaceWordsRequest(["Apple", "BANANA", "Cherry"]));

        var result = await service.ReplaceWordsAsync(ShareCode, new ReplaceWordsRequest(["Apple"]));
        Assert.Equal(1, result.WordCount);
    }

    [Fact]
    public async Task ReplaceWordsAsync_TrimsWhitespace()
    {
        var wordset = MakeWordset();
        var repo = new StubWordsetRepository(existing: wordset);
        var service = new WordService(repo);

        var result = await service.ReplaceWordsAsync(ShareCode, new ReplaceWordsRequest(["  apple  ", " banana "]));

        Assert.Equal(2, result.WordCount);
    }

    [Fact]
    public async Task ReplaceWordsAsync_Deduplicates()
    {
        var wordset = MakeWordset();
        var repo = new StubWordsetRepository(existing: wordset);
        var service = new WordService(repo);

        var result = await service.ReplaceWordsAsync(ShareCode, new ReplaceWordsRequest(["apple", "Apple", "APPLE"]));

        Assert.Equal(1, result.WordCount);
    }

    [Fact]
    public async Task ReplaceWordsAsync_FiltersEmptyStrings()
    {
        var wordset = MakeWordset();
        var repo = new StubWordsetRepository(existing: wordset);
        var service = new WordService(repo);

        var result = await service.ReplaceWordsAsync(ShareCode, new ReplaceWordsRequest(["apple", "", "  ", "banana"]));

        Assert.Equal(2, result.WordCount);
    }

    [Fact]
    public async Task ReplaceWordsAsync_SetsZeroFrequencyRank_ForNewWords()
    {
        var wordset = MakeWordset();
        var repo = new StubWordsetRepository(existing: wordset);
        var service = new WordService(repo);

        await service.ReplaceWordsAsync(ShareCode, new ReplaceWordsRequest(["apple"]));

        var map = await repo.GetWordFrequencyMapAsync(wordset.Id);
        Assert.Equal(0, map["apple"]);
    }

    [Fact]
    public async Task ReplaceWordsAsync_PreservesFrequencyRank_ForExistingWords()
    {
        var wordset = MakeWordset();
        var repo = new StubWordsetRepository(existing: wordset);
        var service = new WordService(repo);

        // First replace — apple gets rank 0 (new word)
        await service.ReplaceWordsAsync(ShareCode, new ReplaceWordsRequest(["apple", "banana"]));

        // Manually set a known frequency rank on "apple" in the stub
        await repo.ReplaceWordsAsync(wordset.Id, [
            new Domain.Entities.Word { WordsetId = wordset.Id, Value = "apple", FrequencyRank = 42 },
            new Domain.Entities.Word { WordsetId = wordset.Id, Value = "banana", FrequencyRank = 99 },
        ]);

        // Replace again — apple survives; cherry is new
        await service.ReplaceWordsAsync(ShareCode, new ReplaceWordsRequest(["apple", "cherry"]));

        var map = await repo.GetWordFrequencyMapAsync(wordset.Id);
        Assert.Equal(42, map["apple"]);  // preserved
        Assert.Equal(0, map["cherry"]);  // new word — default rank
        Assert.False(map.ContainsKey("banana")); // removed
    }

    [Fact]
    public async Task ReplaceWordsAsync_EmptyList_ClearsAllWords()
    {
        var wordset = MakeWordset();
        var repo = new StubWordsetRepository(existing: wordset);
        var service = new WordService(repo);

        await service.ReplaceWordsAsync(ShareCode, new ReplaceWordsRequest(["apple", "banana"]));
        var result = await service.ReplaceWordsAsync(ShareCode, new ReplaceWordsRequest([]));

        Assert.Equal(0, result.WordCount);
    }
}
