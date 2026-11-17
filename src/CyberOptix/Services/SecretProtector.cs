using System;
using System.Security.Cryptography;
using System.Text;

namespace CyberOptix.Services
{
    /// <summary>
    /// Encrypts small secrets (like a local LLM API key) with Windows DPAPI, tied to the signed-in Windows account: only that account
    /// can decrypt them again, on this PC. Used instead of storing them as plain text in settings.json.
    /// </summary>
    public static class SecretProtector
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("CyberOptix.AiAgents.v1");

        /// <summary>Encrypts a secret to a Base64 string, or "" for an empty input (so an empty field clears the stored secret).</summary>
        public static string Protect(string plain)
        {
            if (string.IsNullOrEmpty(plain)) return "";
            try
            {
                var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), Entropy, DataProtectionScope.CurrentUser);
                return Convert.ToBase64String(bytes);
            }
            catch { return ""; }
        }

        /// <summary>Decrypts a value saved by <see cref="Protect"/>. Returns "" if there is nothing saved, or it cannot be read back.</summary>
        public static string Unprotect(string? cipher)
        {
            if (string.IsNullOrEmpty(cipher)) return "";
            try
            {
                var bytes = ProtectedData.Unprotect(Convert.FromBase64String(cipher), Entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(bytes);
            }
            catch { return ""; }
        }
    }
}
