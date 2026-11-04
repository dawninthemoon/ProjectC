#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NUnit.Framework;
using ProjectC.Client;
using UnityEngine.TestTools;

namespace ProjectC.Tests
{
    public sealed class WebsiteSessionPlayModeTests
    {
        private sealed class MemoryStore : ISessionStore
        {
            public string Value;
            public string Read() => Value;
            public void Write(string value) => Value = value;
            public void Delete() => Value = null;
        }

        private HttpListener _listener;
        private Task _worker;
        private string _url;
        private int _guests, _refreshes, _sessions, _rankReads;
        private volatile bool _failRefresh;
        private volatile bool _stopping;
        private volatile bool _failRankReads;
        private const string Player = "11111111-1111-4111-8111-111111111111";
        private const string Deck = "22222222-2222-4222-8222-222222222222";

        [SetUp]
        public void Setup()
        {
            _guests = _refreshes = _sessions = _rankReads = 0;
            _failRefresh = _stopping = false;
            _failRankReads = false;
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            int port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            _url = "http://127.0.0.1:" + port;
            _listener = new HttpListener();
            _listener.Prefixes.Add(_url + "/");
            _listener.Start();
            _worker = Task.Run(() =>
            {
                try
                {
                    while (_listener.IsListening)
                    {
                        var context = _listener.GetContext();
                        string path = context.Request.Url.AbsolutePath;
                        string response;
                        int status = 200;
                        using (var reader = new StreamReader(context.Request.InputStream))
                        {
                            reader.ReadToEnd();
                        }
                        if (path == "/api/auth/guest")
                        {
                            ++_guests;
                            response = Session("guest");
                        }
                        else if (path == "/api/auth/refresh")
                        {
                            ++_refreshes;
                            status = _failRefresh ? 503 : 200;
                            response = Session("rotated");
                        }
                        else if (path == "/api/auth/session")
                        {
                            ++_sessions;
                            response = Session("recovered");
                        }
                        else if (path == "/api/users/me")
                        {
                            response = JsonConvert.SerializeObject(new { playerId = Player, displayName = "Test" });
                        }
                        else if (path == "/api/decks")
                        {
                            response = JsonConvert.SerializeObject(new[] { new { id = Deck, name = "Online", catalogVersion = "test",
                                cards = new[] { new { cardId = 1001, quantity = 3 } } } });
                        }
                        else if (path == "/api/rank/me")
                        {
                            ++_rankReads;
                            status = _failRankReads ? 503 : 200;
                            response = "{\"tier\":\"Bronze\",\"division\":5,\"rp\":20,\"mmr\":1016}";
                        }
                        else if (path == "/api/auth/revoke")
                        {
                            status = 204;
                            response = "";
                        }
                        else
                        {
                            status = 404;
                            response = "";
                        }
                        byte[] output = Encoding.UTF8.GetBytes(response);
                        context.Response.StatusCode = status;
                        context.Response.ContentType = "application/json";
                        context.Response.ContentLength64 = output.Length;
                        context.Response.OutputStream.Write(output, 0, output.Length);
                        context.Response.Close();
                    }
                }
                catch (HttpListenerException) when (_stopping)
                {
                }
                catch (ObjectDisposedException) when (_stopping)
                {
                }
            });
        }

        private static string Session(string token)
        {
            return JsonConvert.SerializeObject(new
            {
                accessToken = token,
                refreshToken = new string('A', 64),
                expiresAt = DateTimeOffset.UtcNow.AddHours(1).ToString("O"),
                refreshExpiresAt = DateTimeOffset.UtcNow.AddDays(30).ToString("O"),
                user = new { playerId = Player, displayName = "Test" },
            });
        }

        private static IEnumerator Complete(Task task, bool success = true)
        {
            while (!task.IsCompleted)
            {
                yield return null;
            }
            Assert.AreEqual(success, !task.IsFaulted && !task.IsCanceled, task.Exception?.GetBaseException().Message);
        }

        [UnityTest]
        public IEnumerator GuestRestoreRefreshDecksRankAndRevoke()
        {
            var store = new MemoryStore();
            var session = new WebsiteSession(_url, 5, store);
            yield return Complete(session.EnsureSessionAsync());
            Assert.AreEqual(Player, session.User.playerId);
            Assert.AreEqual(1, _guests);
            var restored = new WebsiteSession(_url, 5, store);
            yield return Complete(restored.EnsureSessionAsync());
            Assert.AreEqual(1, _guests);
            yield return Complete(restored.EnsureSessionAsync(true));
            Assert.AreEqual(1, _refreshes);
            Assert.AreEqual("rotated", restored.AccessToken);
            yield return Complete(restored.LoadUserDataAsync());
            Assert.AreEqual(Deck, restored.SelectedDeckId);
            CollectionAssert.AreEqual(new[] { 1001, 1001, 1001 }, restored.SelectedDeck.ToPersonalDeck().Cards);
            yield return Complete(restored.ApplyRankChangeAsync(new WebsiteRankChange
            {
                playerId = Player, rpDelta = 20, mmrBefore = 1000, mmrAfter = 1016,
            }));
            Assert.AreEqual(1016, restored.Rank.mmr);
            Assert.AreEqual(2, _rankReads);
            yield return Complete(restored.RevokeAsync());
            Assert.IsNull(store.Value);
        }

        [UnityTest]
        public IEnumerator ConcurrentRefreshRotatesOnceAndLostResponseUsesSession()
        {
            var store = new MemoryStore { Value = Session("old") };
            var session = new WebsiteSession(_url, 5, store);
            yield return Complete(session.EnsureSessionAsync());
            yield return Complete(Task.WhenAll(session.EnsureSessionAsync(true, "old"), session.EnsureSessionAsync(true, "old")));
            Assert.AreEqual(1, _refreshes);
            _failRefresh = true;
            yield return Complete(session.EnsureSessionAsync(true), false);
            Assert.AreEqual(2, _refreshes);
            var restored = new WebsiteSession(_url, 5, store);
            yield return Complete(restored.EnsureSessionAsync());
            Assert.AreEqual(2, _refreshes);
            Assert.AreEqual(1, _sessions);
            Assert.AreEqual("recovered", restored.AccessToken);
        }

        [Test]
        public void ResultTimeoutAndConnectionLossCannotConfirmRank()
        {
            var session = new WebsiteSession(_url, 5, new MemoryStore());
            session.BeginResult();
            session.WaitForResultConfirmation(100);
            session.CheckResultConfirmation(279);
            Assert.AreEqual(WebsiteResultState.AwaitingConfirmation, session.ResultState);
            session.CheckResultConfirmation(280);
            Assert.AreEqual(WebsiteResultState.Unconfirmed, session.ResultState);
            Assert.IsNull(session.LastRankChange);
            StringAssert.Contains("미확인", session.ResultRankSummary);
            session.BeginResult();
            session.MarkResultUnconfirmed();
            Assert.AreEqual(WebsiteResultState.Unconfirmed, session.ResultState);
        }

        [UnityTest]
        public IEnumerator RankQueryAloneAndQueryFailureDoNotReportCompletion()
        {
            var session = new WebsiteSession(_url, 5, new MemoryStore());
            yield return Complete(session.EnsureSessionAsync());
            session.BeginResult();
            yield return Complete(session.RefreshRankAsync());
            Assert.AreEqual(WebsiteResultState.AwaitingConfirmation, session.ResultState);
            Assert.IsNull(session.LastRankChange);
            session.MarkResultUnconfirmed();
            yield return Complete(session.RefreshRankAsync());
            Assert.AreEqual(WebsiteResultState.Unconfirmed, session.ResultState);
            _failRankReads = true;
            yield return Complete(session.ApplyRankChangeAsync(new WebsiteRankChange
            {
                playerId = Player, rpDelta = 20, mmrBefore = 1000, mmrAfter = 1016,
            }), false);
            Assert.AreEqual(WebsiteResultState.Stored, session.ResultState);
            StringAssert.Contains("미완료", session.ResultStatus);
            StringAssert.DoesNotContain("랭크 갱신 확인", session.ResultStatus);
            _failRankReads = false;
            yield return Complete(session.RefreshRankAsync());
            Assert.AreEqual(WebsiteResultState.Complete, session.ResultState);
            session.MarkResultUnconfirmed();
            Assert.AreEqual(WebsiteResultState.Complete, session.ResultState);
        }

        [Test]
        public void SecureStoreRoundTripUsesIsolatedCredential()
        {
#if UNITY_EDITOR_WIN || UNITY_EDITOR_OSX
            var store = new SecureSessionStore("test/" + Guid.NewGuid());
            try
            {
                Assert.IsNull(store.Read());
                store.Write("first");
                Assert.AreEqual("first", store.Read());
                store.Write("second");
                Assert.AreEqual("second", store.Read());
            }
            finally
            {
                store.Delete();
            }
            Assert.IsNull(store.Read());
#else
            Assert.Ignore("Windows Credential Manager / macOS Keychain integration.");
#endif
        }

        [TearDown]
        public void Cleanup()
        {
            _stopping = true;
            _listener.Close();
            Assert.IsTrue(_worker.Wait(TimeSpan.FromSeconds(5)));
        }
    }
}
#endif
