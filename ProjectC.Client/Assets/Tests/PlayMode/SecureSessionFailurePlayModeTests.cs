#if UNITY_EDITOR
using System;
using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NUnit.Framework;
using ProjectC.Client;
using UnityEngine;
using UnityEngine.TestTools;

namespace ProjectC.Tests
{
    public sealed class SecureSessionFailurePlayModeTests
    {
        private sealed class FaultStore : ISessionStore
        {
            public string Value;
            public bool FailRead, FailWrite, FailDelete;
            public int Writes, FailWriteNumber;
            public string Read()
            {
                if (FailRead)
                {
                    throw new SecureSessionStoreException("fixture", "조회", -25308);
                }
                return Value;
            }
            public void Write(string value)
            {
                ++Writes;
                if (FailWrite || Writes == FailWriteNumber)
                {
                    throw new SecureSessionStoreException("fixture", "저장", -25308);
                }
                Value = value;
            }
            public void Delete()
            {
                if (FailDelete)
                {
                    throw new SecureSessionStoreException("fixture", "삭제", -25308);
                }
                Value = null;
            }
        }

        private HttpListener _listener;
        private Task _worker;
        private volatile bool _stopping;
        private volatile int _guests, _refreshes;
        private string _url;
        private const string Player = "11111111-1111-4111-8111-111111111111";

        [SetUp]
        public void Setup()
        {
            _stopping = false;
            _guests = _refreshes = 0;
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
                    while (!_stopping)
                    {
                        var context = _listener.GetContext();
                        bool revoke = context.Request.Url.AbsolutePath == "/api/auth/revoke";
                        if (context.Request.Url.AbsolutePath == "/api/auth/guest")
                        {
                            ++_guests;
                        }
                        if (context.Request.Url.AbsolutePath == "/api/auth/refresh")
                        {
                            ++_refreshes;
                        }
                        string body = revoke ? "{}" : JsonConvert.SerializeObject(new
                        {
                            accessToken = Guid.NewGuid().ToString("N"),
                            refreshToken = Guid.NewGuid().ToString("N"),
                            expiresAt = DateTimeOffset.UtcNow.AddMinutes(30).ToString("O"),
                            refreshExpiresAt = DateTimeOffset.UtcNow.AddDays(7).ToString("O"),
                            user = new { playerId = Player, displayName = "Storage fixture" }
                        });
                        byte[] bytes = Encoding.UTF8.GetBytes(body);
                        context.Response.ContentType = "application/json";
                        context.Response.ContentLength64 = bytes.Length;
                        context.Response.OutputStream.Write(bytes, 0, bytes.Length);
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

        private static IEnumerator Complete(Task task, bool failure = false)
        {
            float deadline = Time.realtimeSinceStartup + 10;
            while (!task.IsCompleted)
            {
                Assert.Less(Time.realtimeSinceStartup, deadline);
                yield return null;
            }
            Assert.IsFalse(task.IsCanceled);
            Assert.AreEqual(failure, task.IsFaulted, "Unexpected secure session task result.");
            if (failure)
            {
                Assert.IsInstanceOf<SecureSessionStoreException>(task.Exception.GetBaseException());
            }
        }

        [UnityTest]
        public IEnumerator ReadFailureDoesNotCreateOrOverwriteGuest()
        {
            var store = new FaultStore { FailRead = true, Value = "untouched fixture" };
            var session = new WebsiteSession(_url, 5, store);
            yield return Complete(session.EnsureSessionAsync(), true);
            yield return Complete(session.EnsureSessionAsync(), true);
            Assert.AreEqual(0, _guests);
            Assert.AreEqual(0, store.Writes);
            Assert.AreEqual("untouched fixture", store.Value);
        }

        [UnityTest]
        public IEnumerator FailedGuestSaveAndRetirementKeepOnePendingAccount()
        {
            var store = new FaultStore { FailWrite = true };
            var session = new WebsiteSession(_url, 5, store);
            yield return Complete(session.EnsureSessionAsync(), true);
            yield return Complete(session.EnsureSessionAsync(), true);
            yield return Complete(session.RetireAsync(), true);
            Assert.AreEqual(1, _guests);
            Assert.IsNull(store.Value);
            store.FailWrite = false;
            yield return Complete(session.EnsureSessionAsync());
            Assert.AreEqual(Player, session.User.playerId);
            var restored = new WebsiteSession(_url, 5, store);
            yield return Complete(restored.EnsureSessionAsync());
            Assert.AreEqual(Player, restored.User.playerId);
            Assert.AreEqual(1, _guests);
        }

        [UnityTest]
        public IEnumerator RefreshMarkerAndReplacementWriteFailuresDoNotReuseRotation()
        {
            var store = new FaultStore();
            var session = new WebsiteSession(_url, 5, store);
            yield return Complete(session.EnsureSessionAsync());
            store.FailWrite = true;
            yield return Complete(session.EnsureSessionAsync(true), true);
            Assert.AreEqual(0, _refreshes, "A failed marker save must stop before HTTP refresh.");
            store.FailWrite = false;
            store.FailWriteNumber = store.Writes + 2;
            yield return Complete(session.EnsureSessionAsync(true), true);
            Assert.AreEqual(1, _refreshes);
            store.FailWriteNumber = 0;
            yield return Complete(session.EnsureSessionAsync());
            Assert.AreEqual(1, _refreshes, "Retry must save the returned pair, not rotate again.");
            Assert.AreEqual(1, _guests);
        }

        [UnityTest]
        public IEnumerator DeleteFailureRetainsCurrentSessionWithoutAutomaticGuestCreation()
        {
            var store = new FaultStore();
            var session = new WebsiteSession(_url, 5, store);
            yield return Complete(session.EnsureSessionAsync());
            store.FailDelete = true;
            yield return Complete(session.RetireAsync());
            yield return Complete(session.RevokeAsync(), true);
            Assert.IsNotNull(store.Value);
            yield return Complete(session.EnsureSessionAsync());
            Assert.AreEqual(1, _guests);
            Assert.AreEqual(Player, session.User.playerId);
            store.FailDelete = false;
            yield return Complete(session.RevokeAsync());
            Assert.IsNull(store.Value);
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
