namespace Argent.Core.Forms;

public interface IFormDataStore
{
    Task<Dictionary<string, object?>> GetCustomDataAsync(Guid recordId, Guid formId);
    Task SaveCustomDataAsync(Guid recordId, Guid formId, Dictionary<string, object?> data);
}
