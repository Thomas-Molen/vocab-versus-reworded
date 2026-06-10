using System.Text;
using Wordset.Application.Services;
using Xunit;

namespace Wordset.Tests.Unit;

public class GetWordsetsTests
{
    private static Domain.Entities.Wordset MakeWordset(string shareCode, string name = "Test") =>
        new() { Name = name, ShareCode = shareCode };

    [Fact]
    public async Task GetWordsetPageAsync_ReturnsFirstPage_WhenNoCursor()
    {
        var repo = new StubWordsetRepository();
        await repo.CreateAsync(MakeWordset("AAAAA2"));
        await repo.CreateAsync(MakeWordset("BBBBB2"));
        await repo.CreateAsync(MakeWordset("CCCCC2"));
        await repo.CreateAsync(MakeWordset("DDDDD2"));
        var service = new WordsetService(repo);

        var result = await service.GetWordsetPageAsync(null, 2);

        Assert.Equal(2, result.Wordsets.Count);
        Assert.Equal("AAAAA2", result.Wordsets[0].ShareCode);
        Assert.Equal("BBBBB2", result.Wordsets[1].ShareCode);
        Assert.NotNull(result.NextCursor);
    }

    [Fact]
    public async Task GetWordsetPageAsync_ReturnsNextPage_UsingCursor()
    {
        var repo = new StubWordsetRepository();
        await repo.CreateAsync(MakeWordset("AAAAA2"));
        await repo.CreateAsync(MakeWordset("BBBBB2"));
        await repo.CreateAsync(MakeWordset("CCCCC2"));
        var service = new WordsetService(repo);

        var firstPage = await service.GetWordsetPageAsync(null, 2);
        var secondPage = await service.GetWordsetPageAsync(firstPage.NextCursor, 2);

        Assert.Single(secondPage.Wordsets);
        Assert.Equal("CCCCC2", secondPage.Wordsets[0].ShareCode);
        Assert.Null(secondPage.NextCursor);
    }

    [Fact]
    public async Task GetWordsetPageAsync_NextCursor_Null_WhenAllFitOnFirstPage()
    {
        var repo = new StubWordsetRepository();
        await repo.CreateAsync(MakeWordset("AAAAA2"));
        await repo.CreateAsync(MakeWordset("BBBBB2"));
        var service = new WordsetService(repo);

        var result = await service.GetWordsetPageAsync(null, 20);

        Assert.Null(result.NextCursor);
        Assert.Equal(2, result.Wordsets.Count);
    }

    [Fact]
    public async Task GetWordsetPageAsync_ReturnsWordsetsInShareCodeOrder()
    {
        var repo = new StubWordsetRepository();
        await repo.CreateAsync(MakeWordset("ZZZZZZ"));
        await repo.CreateAsync(MakeWordset("AAAAA2"));
        await repo.CreateAsync(MakeWordset("MMMMM2"));
        var service = new WordsetService(repo);

        var result = await service.GetWordsetPageAsync(null, 10);

        Assert.Equal(["AAAAA2", "MMMMM2", "ZZZZZZ"], result.Wordsets.Select(w => w.ShareCode).ToList());
    }

    [Fact]
    public async Task GetWordsetPageAsync_UsesDefaultPageSize_WhenNotSpecified()
    {
        var repo = new StubWordsetRepository();
        for (int i = 0; i < 30; i++)
            await repo.CreateAsync(MakeWordset($"A{i:D5}"));
        var service = new WordsetService(repo);

        var result = await service.GetWordsetPageAsync(null, null);

        Assert.Equal(WordsetService.DefaultWordsetPageSize, result.Wordsets.Count);
        Assert.NotNull(result.NextCursor);
    }

    [Fact]
    public async Task GetWordsetPageAsync_ThrowsArgumentOutOfRangeException_WhenPageSizeIsZero()
    {
        var repo = new StubWordsetRepository();
        var service = new WordsetService(repo);

        var ex = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => service.GetWordsetPageAsync(null, 0));

        Assert.Equal("pageSize", ex.ParamName);
    }

    [Fact]
    public async Task GetWordsetPageAsync_ThrowsArgumentOutOfRangeException_WhenPageSizeIsNegative()
    {
        var repo = new StubWordsetRepository();
        var service = new WordsetService(repo);

        var ex = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => service.GetWordsetPageAsync(null, -1));

        Assert.Equal("pageSize", ex.ParamName);
    }

    [Fact]
    public async Task GetWordsetPageAsync_ThrowsArgumentOutOfRangeException_WhenPageSizeExceedsMax()
    {
        var repo = new StubWordsetRepository();
        var service = new WordsetService(repo);

        var ex = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => service.GetWordsetPageAsync(null, WordsetService.MaxWordsetPageSize + 1));

        Assert.Equal("pageSize", ex.ParamName);
    }

    [Fact]
    public async Task GetWordsetPageAsync_EmptyRepository_ReturnsEmptyPage()
    {
        var repo = new StubWordsetRepository();
        var service = new WordsetService(repo);

        var result = await service.GetWordsetPageAsync(null, null);

        Assert.Empty(result.Wordsets);
        Assert.Null(result.NextCursor);
    }

    [Fact]
    public async Task GetWordsetPageAsync_ThrowsArgumentException_WhenCursorIsMalformed()
    {
        var repo = new StubWordsetRepository();
        var service = new WordsetService(repo);

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => service.GetWordsetPageAsync("not-valid-base64!!!", null));

        Assert.Equal("cursor", ex.ParamName);
    }

    [Fact]
    public async Task GetWordsetPageAsync_NextCursor_DecodesBackToLastShareCode()
    {
        var repo = new StubWordsetRepository();
        await repo.CreateAsync(MakeWordset("AAAAA2"));
        await repo.CreateAsync(MakeWordset("BBBBB2"));
        await repo.CreateAsync(MakeWordset("CCCCC2"));
        var service = new WordsetService(repo);

        var result = await service.GetWordsetPageAsync(null, 2);

        var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(result.NextCursor!));
        Assert.Equal("BBBBB2", decoded);
    }
}
