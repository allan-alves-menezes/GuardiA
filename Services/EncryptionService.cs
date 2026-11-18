using System.Security.Cryptography;
using System.Text;

namespace GuardiAProjeto.Services
{
    public class EncryptionService
    {
        // Chave secreta de 32 bytes para o AES
        private readonly byte[] _key = Encoding.UTF8.GetBytes("12345678901234567890123456789012"); 

        public string Encrypt(string plainText)
        {
            using var aes = Aes.Create();
            aes.Key = _key;
            aes.GenerateIV();
            
            using var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
            using var ms = new MemoryStream();
            
            ms.Write(aes.IV, 0, aes.IV.Length);
            using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
            using (var sw = new StreamWriter(cs))
            {
                sw.Write(plainText);
            }
            
            return Convert.ToBase64String(ms.ToArray());
        }

        public string Decrypt(string cipherText)
        {
            var fullCipher = Convert.FromBase64String(cipherText);
            using var aes = Aes.Create();
            aes.Key = _key;
            
            var iv = new byte[16];
            Array.Copy(fullCipher, 0, iv, 0, iv.Length);
            
            using var decryptor = aes.CreateDecryptor(aes.Key, iv);
            using var ms = new MemoryStream(fullCipher, iv.Length, fullCipher.Length - iv.Length);
            using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
            using var sr = new StreamReader(cs);
            
            return sr.ReadToEnd();
        }
    }
}