using System;
using System.Security.Cryptography;
using System.Text;

namespace ProjectC.Client
{
    // Platform stores atomically persist the entire rotated token pair.
    // Tokens never enter PlayerPrefs, deck files, or Unity's serialized assets.
    internal interface ISessionStore
    {
        string Read();
        void Write(string value);
        void Delete();
    }

    internal sealed class SecureSessionStore : ISessionStore
    {
        private readonly ISessionStore _store;

        public SecureSessionStore(string apiUrl, string accountProfileId = AccountProfiles.Default)
        {
            string target = TargetFor(apiUrl, accountProfileId);
#if UNITY_EDITOR_WIN || (UNITY_STANDALONE_WIN && !UNITY_EDITOR)
            _store = new WindowsCredentialSessionStore(target);
#elif UNITY_EDITOR_OSX || (UNITY_STANDALONE_OSX && !UNITY_EDITOR)
            _store = new MacKeychainSessionStore(target);
#else
            throw new PlatformNotSupportedException("OS 보안 세션 저장소는 Windows와 macOS에서 지원됩니다.");
#endif
        }

        internal static string TargetFor(string apiUrl, string accountProfileId)
        {
            if (string.IsNullOrWhiteSpace(apiUrl))
            {
                throw new ArgumentException("Website API URL is required.", nameof(apiUrl));
            }
            string profile = AccountProfiles.DevelopmentFeaturesEnabled
                ? AccountProfiles.Validate(accountProfileId) : AccountProfiles.Default;
            string url = apiUrl.TrimEnd('/');
            if (profile == AccountProfiles.Default)
            {
                // Keep the exact legacy production target. No credential migration/deletion.
                return "ProjectC.Session/" + url;
            }
            using (var hash = SHA256.Create())
            {
                // Separate namespace and fixed-length URL hash avoid ambiguous URL/profile pairs.
                string digest = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(url)))
                    .Replace("-", "").ToLowerInvariant();
                return "ProjectC.Session.Profile/" + profile + "/" + digest;
            }
        }

        public string Read() => _store.Read();
        public void Write(string value) => _store.Write(value);
        public void Delete() => _store.Delete();
    }
}
