using System.Text;
using Wordset.Application.Services;
using Wordset.Domain.Exceptions;
using Xunit;

namespace Wordset.Tests.Unit;

public class GetWordsTests
{
    private const string ShareCode = "ABCDEF";

    private static Domain.Entities.Wordset MakeWordset() =>
        new() { Name = "Test", ShareCode = ShareCode };

    private static StubWordsetRepository RepoWithWords(Domain.Entities.Wordset wordset, params string[] words)
    {
        var repo = new StubWordsetRepository(existing: wordset);
        var entities = words.Select(w => new Domain.Entities.Word
        {
            WordsetId = wordset.Id,
            Value = w,
            FrequencyRank = 0,
        }).ToList();
        repo.ReplaceWordsAsync(wordset.Id, entities).GetAwaiter().GetResult();
        return repo;
    }

    [Fact]
    public async Task GetWordsAsync_ThrowsWordsetNotFoundException_WhenWordsetDoesNotExist()
    {
        var repo = new StubWordsetRepository();
        var service = new WordService(repo);

        await Assert.ThrowsAsync<WordsetNotFoundException>(
            () => service.GetWordsAsync("ZZZZZZ", null, null));
    }

    [Fact]
    public async Task GetWordsAsync_ReturnsFirstPage_WhenNoCursor()
    {
        var wordset = MakeWordset();
        var repo = RepoWithWords(wordset, "apple", "banana", "cherry", "date", "elderberry");
        var service = new WordService(repo);

        var result = await service.GetWordsAsync(ShareCode, null, 3);

        Assert.Equal(["apple", "banana", "cherry"], result.Words);
        Assert.NotNull(result.NextCursor);
    }

    [Fact]
    public async Task GetWordsAsync_ReturnsNextPage_UsingCursor()
    {
        var wordset = MakeWordset();
        var repo = RepoWithWords(wordset, "apple", "banana", "cherry", "date", "elderberry");
        var service = new WordService(repo);

        var firstPage = await service.GetWordsAsync(ShareCode, null, 3);
        var secondPage = await service.GetWordsAsync(ShareCode, firstPage.NextCursor, 3);

        Assert.Equal(["date", "elderberry"], secondPage.Words);
        Assert.Null(secondPage.NextCursor);
    }

    [Fact]
    public async Task GetWordsAsync_NextCursor_Null_WhenAllWordsReturnedOnFirstPage()
    {
        var wordset = MakeWordset();
        var repo = RepoWithWords(wordset, "apple", "banana");
        var service = new WordService(repo);

        var result = await service.GetWordsAsync(ShareCode, null, 50);

        Assert.Null(result.NextCursor);
        Assert.Equal(2, result.Words.Count);
    }

    [Fact]
    public async Task GetWordsAsync_ReturnsWordsInAlphabeticalOrder()
    {
        var wordset = MakeWordset();
        var repo = RepoWithWords(wordset, "cherry", "apple", "banana");
        var service = new WordService(repo);

        var result = await service.GetWordsAsync(ShareCode, null, 10);

        Assert.Equal(["apple", "banana", "cherry"], result.Words);
    }

    [Fact]
    public async Task GetWordsAsync_ThrowsArgumentOutOfRangeException_WhenPageSizeIsZero()
    {
        var wordset = MakeWordset();
        var repo = new StubWordsetRepository(existing: wordset);
        var service = new WordService(repo);

        var ex = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => service.GetWordsAsync(ShareCode, null, 0));

        Assert.Equal("pageSize", ex.ParamName);
    }

    [Fact]
    public async Task GetWordsAsync_ThrowsArgumentOutOfRangeException_WhenPageSizeIsNegative()
    {
        var wordset = MakeWordset();
        var repo = new StubWordsetRepository(existing: wordset);
        var service = new WordService(repo);

        var ex = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => service.GetWordsAsync(ShareCode, null, -1));

        Assert.Equal("pageSize", ex.ParamName);
    }

    [Fact]
    public async Task GetWordsAsync_ThrowsArgumentOutOfRangeException_WhenPageSizeExceedsMax()
    {
        var wordset = MakeWordset();
        var repo = new StubWordsetRepository(existing: wordset);
        var service = new WordService(repo);

        var ex = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => service.GetWordsAsync(ShareCode, null, WordService.MaxPageSize + 1));

        Assert.Equal("pageSize", ex.ParamName);
    }

    [Fact]
    public async Task GetWordsAsync_UsesDefaultPageSize_WhenNotSpecified()
    {
        var wordset = MakeWordset();
        var words = Enumerable.Range(1, 100).Select(i => $"word{i:D4}").ToArray();
        var repo = RepoWithWords(wordset, words);
        var service = new WordService(repo);

        var result = await service.GetWordsAsync(ShareCode, null, null);

        Assert.Equal(WordService.DefaultPageSize, result.Words.Count);
        Assert.NotNull(result.NextCursor);
    }

    [Fact]
    public async Task GetWordsAsync_ThrowsArgumentException_WhenCursorIsMalformed()
    {
        var wordset = MakeWordset();
        var repo = new StubWordsetRepository(existing: wordset);
        var service = new WordService(repo);

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => service.GetWordsAsync(ShareCode, "not-valid-base64!!!", null));

        Assert.Equal("cursor", ex.ParamName);
    }

    [Fact]
    public async Task GetWordsAsync_NextCursor_DecodesBackToLastWord()
    {
        var wordset = MakeWordset();
        var repo = RepoWithWords(wordset, "apple", "banana", "cherry", "date");
        var service = new WordService(repo);

        var result = await service.GetWordsAsync(ShareCode, null, 2);

        var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(result.NextCursor!));
        Assert.Equal("banana", decoded);
    }

    [Fact]
    public async Task GetWordsAsync_EmptyWordset_ReturnsEmptyPage()
    {
        var wordset = MakeWordset();
        var repo = new StubWordsetRepository(existing: wordset);
        var service = new WordService(repo);

        var result = await service.GetWordsAsync(ShareCode, null, null);

        Assert.Empty(result.Words);
        Assert.Null(result.NextCursor);
    }
}
