using System.Security.Cryptography;
using System.Text;

internal static class AesService
{
    private const int AES_BLOCK_SIZE_IN_BYTES = 128 / 8;

    public static byte[] Encrypt(string plainText, string cipherBase64)
    {
        using (var aes = Aes.Create())
        {
            byte[] iv = new byte[AES_BLOCK_SIZE_IN_BYTES];
            RandomNumberGenerator.Fill(iv);

            using (ICryptoTransform encryptor = aes.CreateEncryptor(Convert.FromBase64String(cipherBase64), iv))
            {
                byte[] payload = Encoding.UTF8.GetBytes(plainText);
                byte[] cipherText = encryptor.TransformFinalBlock(payload, 0, payload.Length);

                byte[] result = new byte[iv.Length + cipherText.Length];
                iv.CopyTo(result, 0);
                cipherText.CopyTo(result, iv.Length);

                return result;
            }
        }
    }

    public static string Decrypt(byte[] encryptedData, string cipherBase64)
    {
        using (Aes aes = Aes.Create())
        {
            // Because Microsoft has yet to update their CreateDecryptor method,
            // we're forced to create copies of these arrays, when really we shouldn't...
            Span<byte> encryptedPayload = encryptedData.AsSpan();
            byte[] iv = encryptedPayload.Slice(0, AES_BLOCK_SIZE_IN_BYTES).ToArray();
            byte[] cipherText = encryptedPayload.Slice(AES_BLOCK_SIZE_IN_BYTES).ToArray();
            using (ICryptoTransform encryptor = aes.CreateDecryptor(Convert.FromBase64String(cipherBase64), iv))
            {
                byte[] decryptedBytes = encryptor
                    .TransformFinalBlock(cipherText, 0, cipherText.Length);
                return Encoding.UTF8.GetString(decryptedBytes);
            }
        }
    }
}