using Wordset.Application.Models;
using Wordset.Application.Services;
using Wordset.Domain.Exceptions;
using Xunit;

namespace Wordset.Tests.Unit;

public class ShareCodeTests
{
    [Fact]
    public async Task CreateAsync_GeneratesShareCode_CorrectLength()
    {
        var repo = new StubWordsetRepository();
        var service = new WordsetService(repo);

        var result = await service.CreateAsync(new CreateWordsetRequest("Test"));

        Assert.Equal(WordsetService.ShareCodeLength, result.ShareCode.Length);
    }

    [Fact]
    public async Task CreateAsync_GeneratesShareCode_OnlyValidCharacters()
    {
        var repo = new StubWordsetRepository();
        var service = new WordsetService(repo);

        for (int i = 0; i < 50; i++)
        {
            var result = await service.CreateAsync(new CreateWordsetRequest($"Test {i}"));
            foreach (char c in result.ShareCode)
                Assert.True(WordsetService.IsValidShareCodeChar(c),
                    $"Invalid char '{c}' found in share code '{result.ShareCode}'");
        }
    }

    [Fact]
    public async Task CreateAsync_ShareCode_NeverContainsConfusableCharacters()
    {
        var confusables = new[] { 'O', '0', 'I', '1' };
        var repo = new StubWordsetRepository();
        var service = new WordsetService(repo);

        for (int i = 0; i < 50; i++)
        {
            var result = await service.CreateAsync(new CreateWordsetRequest($"Test {i}"));
            foreach (char bad in confusables)
                Assert.DoesNotContain(bad, result.ShareCode);
        }
    }

    [Fact]
    public async Task CreateAsync_RetriesShareCode_WhenCollisionOccurs()
    {
        var repo = new StubWordsetRepository(shareCodeExistsSequence: [true, false]);
        var service = new WordsetService(repo);

        var result = await service.CreateAsync(new CreateWordsetRequest("Collision Test"));

        Assert.NotNull(result);
        Assert.Equal(2, repo.ShareCodeExistsCallCount);
    }

    [Fact]
    public async Task CreateAsync_TrimsWhitespaceFromName()
    {
        var repo = new StubWordsetRepository();
        var service = new WordsetService(repo);

        var result = await service.CreateAsync(new CreateWordsetRequest("  Padded Name  "));

        Assert.Equal("Padded Name", result.Name);
    }

    [Fact]
    public async Task UpdateAsync_ThrowsWordsetNotFoundException_WhenWordsetDoesNotExist()
    {
        var repo = new StubWordsetRepository();
        var service = new WordsetService(repo);

        await Assert.ThrowsAsync<WordsetNotFoundException>(
            () => service.UpdateAsync("ZZZZZZ", new UpdateWordsetRequest("New Name")));
    }

    [Fact]
    public async Task UpdateAsync_TrimsWhitespaceFromName()
    {
        var wordset = new Domain.Entities.Wordset { Name = "Old", ShareCode = "ABCDEF" };
        var repo = new StubWordsetRepository(existing: wordset);
        var service = new WordsetService(repo);

        var result = await service.UpdateAsync("ABCDEF", new UpdateWordsetRequest("  Trimmed  "));

        Assert.Equal("Trimmed", result.Name);
    }

    [Fact]
    public async Task UpdateAsync_DoesNotChangeShareCode()
    {
        var wordset = new Domain.Entities.Wordset { Name = "Old", ShareCode = "XK7P2M" };
        var repo = new StubWordsetRepository(existing: wordset);
        var service = new WordsetService(repo);

        var result = await service.UpdateAsync("XK7P2M", new UpdateWordsetRequest("New Name"));

        Assert.Equal("XK7P2M", result.ShareCode);
    }
}
