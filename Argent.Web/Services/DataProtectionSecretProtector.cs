using Argent.Contracts.DataSources;
using Microsoft.AspNetCore.DataProtection;

namespace Argent.Web.Services;

/// <summary>IDataProtector-backed <see cref="ISecretProtector"/> for encrypting data source secrets at rest.</summary>
public class DataProtectionSecretProtector : ISecretProtector
{
    private readonly IDataProtector _protector;

    public DataProtectionSecretProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("Argent.DataSources.v1");
    }

    public string Protect(string plaintext) => _protector.Protect(plaintext);
    public string Unprotect(string ciphertext) => _protector.Unprotect(ciphertext);
}
