#if UNITY_EDITOR_OSX
using System;
using NUnit.Framework;
using ProjectC.Client;

namespace ProjectC.Tests
{
    public sealed class MacKeychainPlayModeTests
    {
        [Test]
        public void NativeKeychainScopesAtomicPairsByApiAndProfile()
        {
            string url = "https://keychain-test.invalid/" + Guid.NewGuid().ToString("N");
            var first = new SecureSessionStore(url, AccountProfiles.ClientOne);
            var second = new SecureSessionStore(url, AccountProfiles.ClientTwo);
            var legacy = new SecureSessionStore(url, AccountProfiles.Default);
            var otherApi = new SecureSessionStore(url + "/other", AccountProfiles.ClientOne);
            try
            {
                Assert.IsNull(first.Read());
                first.Write("{\"accessToken\":\"첫 토큰\",\"refreshToken\":\"refresh-1\"}");
                second.Write("second fixture");
                legacy.Write("default fixture");
                otherApi.Write("other API fixture");
                first.Write("rotated token pair");
                Assert.AreEqual("rotated token pair", first.Read());
                Assert.Throws<InvalidOperationException>(() => first.Write(new string('x', 2561)));
                Assert.AreEqual("rotated token pair", first.Read());
                first.Delete();
                Assert.IsNull(first.Read());
                Assert.AreEqual("second fixture", second.Read());
                Assert.AreEqual("default fixture", legacy.Read());
                Assert.AreEqual("other API fixture", otherApi.Read());
                first.Write("");
                Assert.AreEqual("", first.Read(), "Empty data must not be reported as a missing item.");
            }
            finally
            {
                first.Delete();
                second.Delete();
                legacy.Delete();
                otherApi.Delete();
            }
        }

        [TestCase(-25308)]
        [TestCase(-25293)]
        [TestCase(-25299)]
        public void DeniedLockedAndDuplicateStatusesAreVisibleErrors(int status)
        {
            var error = Assert.Throws<SecureSessionStoreException>(() => MacKeychainSessionStore.RequireSuccess(status, "조회"));
            Assert.AreEqual(status, error.Status);
        }
    }
}
#endif
