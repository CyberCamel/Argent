namespace Argent.Contracts.DataSources;

/// <summary>
/// Encrypts/decrypts secret payloads at rest. Abstracted so the Runtime layer needn't depend
/// on ASP.NET DataProtection directly; the Web layer supplies the IDataProtector-backed impl.
/// </summary>
public interface ISecretProtector
{
    string Protect(string plaintext);
    string Unprotect(string ciphertext);
}
