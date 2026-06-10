using Wordset.Domain.Entities;

namespace Wordset.Domain.Interfaces;

public interface IWordsetRepository
{
    Task<Entities.Wordset?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Entities.Wordset?> GetByShareCodeAsync(string shareCode, CancellationToken ct = default);
    Task<IReadOnlyList<Entities.Wordset>> GetWordsetPageAsync(string? afterShareCode, int pageSize, CancellationToken ct = default);
    Task<Entities.Wordset> CreateAsync(Entities.Wordset wordset, CancellationToken ct = default);
    Task<Entities.Wordset> UpdateAsync(Entities.Wordset wordset, CancellationToken ct = default);
    Task<bool> ShareCodeExistsAsync(string shareCode, CancellationToken ct = default);
    Task<int> GetWordCountAsync(Guid wordsetId, CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetWordsPageAsync(Guid wordsetId, string? afterWord, int pageSize, CancellationToken ct = default);
    Task<IReadOnlyDictionary<string, int?>> GetWordFrequencyMapAsync(Guid wordsetId, CancellationToken ct = default);
    Task ReplaceWordsAsync(Guid wordsetId, IReadOnlyList<Entities.Word> words, CancellationToken ct = default);
}
