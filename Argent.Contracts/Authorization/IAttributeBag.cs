namespace Argent.Contracts.Authorization;

public interface IAttributeBag
{
    object? GetValue(string key);
    Dictionary<string, object?> GetAllValues();
    List<string> UserRoles { get; }
}
