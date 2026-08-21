namespace GMS.Core.Interfaces;

/// <summary>
/// AES-256 encryption service for sensitive data (NationalId, etc.).
/// </summary>
public interface IEncryptionService
{
    /// <summary>Encrypts plaintext using AES-256.</summary>
    string Encrypt(string plainText);

    /// <summary>Decrypts AES-256 ciphertext.</summary>
    string Decrypt(string cipherText);
}
