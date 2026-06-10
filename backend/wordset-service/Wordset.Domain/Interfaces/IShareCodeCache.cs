namespace Wordset.Domain.Interfaces;

public interface IShareCodeCache
{
    Guid? TryGet(string shareCode);
    void Set(string shareCode, Guid id);
}
