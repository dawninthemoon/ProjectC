#if UNITY_EDITOR_WIN || UNITY_EDITOR_OSX
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using ProjectC.Client;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace ProjectC.Tests
{
    public sealed class AccountProfilePlayModeTests
    {
        private sealed class Account
        {
            public string Id = Guid.NewGuid().ToString();
            public readonly List<JObject> Decks = new List<JObject>();
        }

        private HttpListener _listener;
        private Task _apiWorker;
        private string _url, _pathRoot, _catalog;
        private volatile bool _stopping;
        private volatile int _guests;
        private readonly Dictionary<string, Account> _access = new Dictionary<string, Account>();
        private readonly Dictionary<string, Account> _refresh = new Dictionary<string, Account>();
        private readonly Dictionary<string, Account> _accounts = new Dictionary<string, Account>();
        private readonly List<WebSocketTransport> _transports = new List<WebSocketTransport>();
        private System.Diagnostics.Process _server;
        private string _directory;
        private string _editorProfile;
        private ManualResetEventSlim _refreshEntered, _releaseRefresh;
        private volatile bool _holdRefresh;
        private GameObject _appObject;
        private ServerConnectionSettings _settings;

        [SetUp]
        public void Setup()
        {
            _editorProfile = AccountProfiles.EditorProfile;
            _stopping = _holdRefresh = false;
            _guests = 0;
            _access.Clear();
            _refresh.Clear();
            _accounts.Clear();
            _directory = Path.Combine(Application.temporaryCachePath, "AccountProfiles-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            int port = FreePort();
            // Unique URL namespace ensures tests never touch any existing user's credentials.
            _pathRoot = "/account-test-" + Guid.NewGuid().ToString("N");
            _url = "http://127.0.0.1:" + port + _pathRoot;
            _catalog = CardLibrary.LoadResources().CatalogVersion;
            _refreshEntered = new ManualResetEventSlim();
            _releaseRefresh = new ManualResetEventSlim();
            _listener = new HttpListener();
            _listener.Prefixes.Add("http://127.0.0.1:" + port + "/");
            _listener.Start();
            _apiWorker = Task.Run(() =>
            {
                try
                {
                    while (!_stopping)
                    {
                        var context = _listener.GetContext();
                        Respond(context);
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

        private JToken Session(Account account)
        {
            string access = Guid.NewGuid().ToString("N");
            string refresh = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
            _access[access] = account;
            _refresh[refresh] = account;
            return JObject.FromObject(new
            {
                accessToken = access, refreshToken = refresh,
                expiresAt = DateTimeOffset.UtcNow.AddHours(1).ToString("O"),
                refreshExpiresAt = DateTimeOffset.UtcNow.AddDays(30).ToString("O"),
                user = new { playerId = account.Id, displayName = "Profile test" },
            });
        }

        private void Respond(HttpListenerContext context)
        {
            string path = context.Request.Url.AbsolutePath.Substring(_pathRoot.Length);
            string text;
            using (var reader = new StreamReader(context.Request.InputStream))
            {
                text = reader.ReadToEnd();
            }
            var body = string.IsNullOrEmpty(text) ? new JObject() : JObject.Parse(text);
            int status = 200;
            JToken result = JValue.CreateNull();
            if (path == "/api/auth/guest")
            {
                var account = new Account();
                _accounts.Add(account.Id, account);
                ++_guests;
                result = Session(account);
            }
            else if (path == "/api/auth/refresh")
            {
                if (_holdRefresh)
                {
                    _refreshEntered.Set();
                    if (!_releaseRefresh.Wait(TimeSpan.FromSeconds(10)))
                    {
                        throw new TimeoutException("Test did not release refresh.");
                    }
                }
                string token = body.Value<string>("refreshToken");
                if (_refresh.TryGetValue(token, out var account))
                {
                    _refresh.Remove(token);
                    result = Session(account);
                }
                else
                {
                    status = 401;
                }
            }
            else if (path == "/api/auth/revoke")
            {
                _refresh.Remove(body.Value<string>("refreshToken"));
                status = 204;
            }
            else if (path == "/api/internal/battle/players/verify")
            {
                if (_access.TryGetValue(body.Value<string>("accessToken"), out var account))
                {
                    result = JObject.FromObject(new { playerId = account.Id, displayName = "Profile test" });
                }
                else
                {
                    status = 401;
                }
            }
            else if (path.EndsWith("/validate", StringComparison.Ordinal))
            {
                string[] parts = path.Split('/');
                var account = _accounts[parts[5]];
                var deck = account.Decks.Single(d => d.Value<string>("id") == parts[7]);
                var ids = new JArray();
                foreach (var card in deck["cards"])
                {
                    for (int i = 0; i < card.Value<int>("quantity"); ++i)
                    {
                        ids.Add(card.Value<int>("cardId"));
                    }
                }
                Assert.AreEqual(_catalog, body.Value<string>("catalogVersion"));
                result = JObject.FromObject(new { playerId = account.Id, deckId = deck.Value<string>("id"),
                    snapshotId = Guid.NewGuid().ToString(), catalogVersion = _catalog, cardIds = ids });
            }
            else
            {
                string token = (context.Request.Headers["Authorization"] ?? "").Replace("Bearer ", "");
                if (!_access.TryGetValue(token, out var account))
                {
                    status = 401;
                }
                else if (path == "/api/users/me")
                {
                    result = JObject.FromObject(new { playerId = account.Id, displayName = "Profile test" });
                }
                else if (path == "/api/rank/me")
                {
                    result = JObject.FromObject(new { tier = "Bronze", division = 5, rp = 0, mmr = 1000 });
                }
                else if (path == "/api/decks")
                {
                    if (context.Request.HttpMethod == "POST")
                    {
                        var deck = new JObject { ["id"] = Guid.NewGuid().ToString(), ["name"] = body["name"],
                            ["catalogVersion"] = _catalog, ["cards"] = body["cards"] };
                        account.Decks.Add(deck);
                        result = deck;
                        status = 201;
                    }
                    else
                    {
                        result = new JArray(account.Decks);
                    }
                }
                else
                {
                    status = 404;
                }
            }
            byte[] output = status == 204 ? Array.Empty<byte>() : System.Text.Encoding.UTF8.GetBytes(result.ToString(Formatting.None));
            context.Response.StatusCode = status;
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = output.Length;
            context.Response.OutputStream.Write(output, 0, output.Length);
            context.Response.Close();
        }

        private static int FreePort()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            int port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }

        private static IEnumerator Complete(Task task)
        {
            float deadline = Time.realtimeSinceStartup + 20;
            while (!task.IsCompleted)
            {
                Assert.Less(Time.realtimeSinceStartup, deadline, "Profile task timed out.");
                yield return null;
            }
            Assert.IsFalse(task.IsFaulted || task.IsCanceled, task.Exception?.GetBaseException().GetType().Name);
        }

        private static IEnumerator Until(Func<bool> ready)
        {
            float deadline = Time.realtimeSinceStartup + 20;
            while (!ready())
            {
                Assert.Less(Time.realtimeSinceStartup, deadline, "Profile condition timed out.");
                yield return null;
            }
        }

        private int StartBattleServer()
        {
            string path = Path.GetFullPath(Path.Combine(Application.dataPath,
                "../../ProjectC.Server/out/build/x64-Debug/ProjectCNetworkServer.exe"));
            string[] arguments = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < arguments.Length; ++i)
            {
                if (arguments[i] == "-projectcServerPath")
                {
                    path = arguments[i + 1];
                }
            }
            Assert.IsTrue(File.Exists(path), "Pass -projectcServerPath pointing to a built Battle Server.");
            int port = FreePort();
            var start = new System.Diagnostics.ProcessStartInfo(path,
                "--port " + port + " --mulligan off --data \"" + Path.Combine(Path.GetDirectoryName(path), "Data", "HyakkiYagyo") + "\"")
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            };
            start.EnvironmentVariables["WEBSITE_API_URL"] = _url;
            start.EnvironmentVariables["BATTLE_API_KEY"] = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
            start.EnvironmentVariables["BATTLE_RESULT_OUTBOX"] = Path.Combine(_directory, "outbox");
            start.EnvironmentVariables["BATTLE_RESULT_STORAGE"] = "ephemeral";
            _server = System.Diagnostics.Process.Start(start);
            _server.BeginOutputReadLine();
            _server.BeginErrorReadLine();
            return port;
        }

        [Test]
        public void ReleasePolicyAndLegacyCredentialTargetRemainCompatible()
        {
            Assert.AreEqual("ProjectC.Session/" + _url, SecureSessionStore.TargetFor(_url, AccountProfiles.Default));
            Assert.AreNotEqual(SecureSessionStore.TargetFor(_url, AccountProfiles.ClientOne), SecureSessionStore.TargetFor(_url, AccountProfiles.ClientTwo));
            Assert.AreNotEqual(SecureSessionStore.TargetFor(_url, AccountProfiles.ClientOne), SecureSessionStore.TargetFor(_url + "/other", AccountProfiles.ClientOne));
            var legacy = new SecureSessionStore(_url);
            legacy.Write("legacy credential fixture");
            Assert.AreEqual("legacy credential fixture", new SecureSessionStore(_url, AccountProfiles.Default).Read());
            Assert.IsNull(new SecureSessionStore(_url, AccountProfiles.Editor).Read());
            Assert.AreEqual(AccountProfiles.Default, AccountProfiles.ResolveLaunchProfile(new[] { "--account-profile=invalid" }, AccountProfiles.Editor, false, false));
            Assert.AreEqual(AccountProfiles.Default, AccountProfiles.ResolveLaunchProfile(Array.Empty<string>(), AccountProfiles.Editor, false, true));
            Assert.AreEqual(AccountProfiles.Editor, AccountProfiles.ResolveLaunchProfile(Array.Empty<string>(), AccountProfiles.Editor, true, false));
            Assert.AreEqual(AccountProfiles.ClientOne, AccountProfiles.ResolveLaunchProfile(new[] { "--account-profile=dev-client-1" }, AccountProfiles.Default, false, true));
            Assert.Throws<ArgumentException>(() => AccountProfiles.ResolveLaunchProfile(new[] { "--account-profile=unknown" }, AccountProfiles.Default, false, true));
            Assert.Throws<ArgumentException>(() => AccountProfiles.ResolveLaunchProfile(new[] { "--account-profile=dev-client-1", "--account-profile=dev-client-2" }, AccountProfiles.Default, false, true));
        }

        [UnityTest]
        public IEnumerator TwoWindowsProfilesRestoreDistinctPlayersAndMatchOnRealServer()
        {
            var first = new WebsiteSession(_url, 5, AccountProfiles.ClientOne);
            var second = new WebsiteSession(_url, 5, AccountProfiles.ClientTwo);
            yield return Complete(first.EnsureSessionAsync());
            yield return Complete(second.EnsureSessionAsync());
            Assert.AreNotEqual(first.User.playerId, second.User.playerId);
            var sample = CardLibrary.Samples(Resources.Load<TextAsset>("CardData/HyakkiDecks").text, CardLibrary.LoadResources())[0];
            yield return Complete(first.UploadDeckAsync(sample));
            yield return Complete(second.UploadDeckAsync(sample));
            Assert.AreNotEqual(first.SelectedDeckId, second.SelectedDeckId);
            var restored = new WebsiteSession(_url, 5, AccountProfiles.ClientOne);
            yield return Complete(restored.EnsureSessionAsync());
            yield return Complete(restored.LoadUserDataAsync());
            Assert.AreEqual(first.User.playerId, restored.User.playerId);
            Assert.AreEqual(first.SelectedDeckId, restored.SelectedDeckId);
            Assert.AreEqual(2, _guests);
            int port = StartBattleServer();
            yield return new WaitForSecondsRealtime(.5f);
            foreach (var session in new[] { first, second })
            {
                var transport = new WebSocketTransport();
                _transports.Add(transport);
                _ = transport.ConnectAsync("ws://127.0.0.1:" + port);
                yield return Until(() => transport.Connected);
                yield return Complete(transport.SendAsync(new JObject
                {
                    ["Type"] = "QueueMatch", ["ProtocolVersion"] = 2, ["RequestId"] = Guid.NewGuid().ToString("N"),
                    ["AccessToken"] = session.AccessToken, ["DeckId"] = session.SelectedDeckId,
                }.ToString(Formatting.None)));
            }
            var matches = new List<string>();
            foreach (var transport in _transports)
            {
                bool started = false;
                yield return Until(() =>
                {
                    while (transport.Messages.TryDequeue(out string message))
                    {
                        var value = JObject.Parse(message);
                        Assert.AreEqual(2, value.Value<int>("ProtocolVersion"));
                        Assert.AreNotEqual("ServerError", value.Value<string>("Type"), value.Value<string>("Reason"));
                        if (value.Value<string>("Type") == "StateSnapshot" && value["Payload"].Value<bool>("Started"))
                        {
                            matches.Add(value.Value<string>("MatchId"));
                            started = true;
                        }
                    }
                    return started;
                });
            }
            Assert.AreEqual(matches[0], matches[1]);
            string otherCredential = new SecureSessionStore(_url, AccountProfiles.ClientTwo).Read();
            yield return Complete(first.RetireAsync());
            yield return Complete(first.RevokeAsync());
            Assert.IsNull(new SecureSessionStore(_url, AccountProfiles.ClientOne).Read());
            Assert.AreEqual(otherCredential, new SecureSessionStore(_url, AccountProfiles.ClientTwo).Read());
            var other = new WebsiteSession(_url, 5, AccountProfiles.ClientTwo);
            yield return Complete(other.EnsureSessionAsync());
            yield return Complete(other.LoadUserDataAsync());
            Assert.AreEqual(second.User.playerId, other.User.playerId);
            Assert.AreEqual(second.SelectedDeckId, other.SelectedDeckId);
            Assert.AreEqual(1000, other.Rank.mmr);
        }

        [UnityTest]
        public IEnumerator RetiringProfileWaitsForRotatedTokenBeforeNextRestore()
        {
            var session = new WebsiteSession(_url, 5, AccountProfiles.Editor);
            yield return Complete(session.EnsureSessionAsync());
            string id = session.User.playerId;
            string oldCredential = new SecureSessionStore(_url, AccountProfiles.Editor).Read();
            _holdRefresh = true;
            var refresh = session.EnsureSessionAsync(true);
            yield return Until(() => _refreshEntered.IsSet);
            var retire = session.RetireAsync();
            Assert.IsFalse(retire.IsCompleted);
            _releaseRefresh.Set();
            yield return Complete(refresh);
            yield return Complete(retire);
            Assert.AreNotEqual(oldCredential, new SecureSessionStore(_url, AccountProfiles.Editor).Read());
            var restored = new WebsiteSession(_url, 5, AccountProfiles.Editor);
            yield return Complete(restored.EnsureSessionAsync());
            Assert.AreEqual(id, restored.User.playerId);
            Assert.AreEqual(1, _guests);
            Assert.IsTrue(session.EnsureSessionAsync().IsFaulted);
        }

        [Test]
        public void AccountResetClearsMatchAndReconnectCapability()
        {
            var root = new GameObject("Profile reset test", typeof(MultiplayerDebugScreen));
            try
            {
                var network = root.GetComponent<MultiplayerDebugScreen>();
                network.ApplyMessage("{\"Type\":\"MatchCreated\",\"ProtocolVersion\":2,\"MatchId\":\"old\",\"PlayerId\":1,\"SessionToken\":\"old-account-token\",\"GameNumber\":0}");
                Assert.AreEqual("old", network.CurrentMatch);
                network.ResetAccountConnection();
                Assert.AreEqual("", network.CurrentMatch);
                Assert.AreEqual(0, network.PlayerId);
                Assert.AreEqual(MatchmakingState.Idle, network.Matchmaking);
                Assert.IsFalse(network.Busy);
                Assert.IsNull(typeof(MultiplayerDebugScreen).GetField("token", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(network));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void SetField(object target, string name, object value)
        {
            target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
        }

        [UnityTest]
        public IEnumerator AppSwitchRestoresProfilesAndLogoutDoesNotCreateGuestOrDeleteOtherData()
        {
            Assert.IsNull(ClientApp.Instance);
            AccountProfiles.EditorProfile = AccountProfiles.Default;
            ClientApp.StorageOverride = _directory;
            int port = StartBattleServer();
            _settings = ScriptableObject.CreateInstance<ServerConnectionSettings>();
            SetField(_settings, "_localWebsiteEnabled", true);
            SetField(_settings, "_localApiAddress", _url);
            SetField(_settings, "_localBattleAddress", "ws://127.0.0.1:" + port);
            _appObject = new GameObject("Profile app test", typeof(MultiplayerDebugScreen));
            var app = _appObject.AddComponent<ClientApp>();
            SetField(app, "_connectionSettings", _settings);
            typeof(ClientApp).GetProperty("Environment").SetValue(app, ServerEnvironment.Local);
            app.ServerAddress = "ws://127.0.0.1:" + port;
            Assert.IsNotNull(app.GetComponent<DeveloperAccountPanel>());
            yield return Complete(app.PrepareWebsiteAsync());
            string defaultId = app.Website.User.playerId;
            yield return Complete(app.Website.UploadDeckAsync(app.Starter));
            string defaultDeck = app.Website.SelectedDeckId;
            string defaultCredential = new SecureSessionStore(_url).Read();

            yield return Complete(app.ChangeAccountProfileAsync(AccountProfiles.ClientOne));
            yield return Until(() => app.Network.ConnectionReady && !app.Navigating);
            string firstId = app.Website.User.playerId;
            Assert.AreNotEqual(defaultId, firstId);
            yield return Complete(app.ChangeAccountProfileAsync(AccountProfiles.ClientTwo));
            yield return Until(() => app.Network.ConnectionReady && !app.Navigating);
            string secondId = app.Website.User.playerId;
            Assert.AreNotEqual(firstId, secondId);
            string secondCredential = new SecureSessionStore(_url, AccountProfiles.ClientTwo).Read();

            yield return Complete(app.ChangeAccountProfileAsync(AccountProfiles.ClientOne));
            yield return Until(() => app.Network.ConnectionReady && !app.Navigating);
            Assert.AreEqual(firstId, app.Website.User.playerId);
            yield return Complete(app.LogoutCurrentProfileAsync());
            yield return Until(() => !app.Navigating && SceneManager.GetActiveScene().name == "ConnectionScene");
            yield return null;
            Assert.IsTrue(app.AccountLoggedOut);
            Assert.IsNull(app.Website);
            Assert.AreEqual(3, _guests, "Logging out must not silently create another guest.");
            Assert.IsNull(new SecureSessionStore(_url, AccountProfiles.ClientOne).Read());
            Assert.AreEqual(secondCredential, new SecureSessionStore(_url, AccountProfiles.ClientTwo).Read());
            Assert.AreEqual(defaultCredential, new SecureSessionStore(_url).Read());

            yield return Complete(app.ChangeAccountProfileAsync(AccountProfiles.Default));
            yield return Until(() => app.Network.ConnectionReady && !app.Navigating);
            Assert.AreEqual(defaultId, app.Website.User.playerId);
            Assert.AreEqual(defaultDeck, app.Website.SelectedDeckId);
            Assert.AreEqual(1000, app.Website.Rank.mmr);
            Assert.AreEqual(3, _guests);
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            _releaseRefresh.Set();
            if (_appObject != null)
            {
                // Stop scene-owned connection coroutines before destroying their
                // persistent services, so a late success cannot load another scene.
                foreach (var presenter in UnityEngine.Object.FindObjectsByType<ConnectionScenePresenter>(FindObjectsSortMode.None))
                {
                    presenter.StopAllCoroutines();
                    presenter.enabled = false;
                }
                var app = _appObject.GetComponent<ClientApp>();
                yield return Until(() => !app.Navigating);
                var current = SceneManager.GetActiveScene();
                var empty = SceneManager.CreateScene("ProfileTestEmpty-" + Guid.NewGuid().ToString("N"));
                SceneManager.SetActiveScene(empty);
                yield return SceneManager.UnloadSceneAsync(current);
                UnityEngine.Object.Destroy(_appObject);
                yield return null;
                Assert.IsNull(ClientApp.Instance, "Profile fixture must not leave account services in later tests.");
            }
            if (_settings != null)
            {
                UnityEngine.Object.Destroy(_settings);
            }
            foreach (var transport in _transports)
            {
                transport.Dispose();
            }
            _transports.Clear();
            if (_server != null)
            {
                if (!_server.HasExited)
                {
                    _server.Kill();
                    _server.WaitForExit(5000);
                }
                _server.Dispose();
                _server = null;
            }
            _stopping = true;
            _listener.Close();
            Assert.IsTrue(_apiWorker.Wait(TimeSpan.FromSeconds(5)));
            foreach (string profile in AccountProfiles.Options)
            {
                new SecureSessionStore(_url, profile).Delete();
            }
            AccountProfiles.EditorProfile = _editorProfile;
            _refreshEntered.Dispose();
            _releaseRefresh.Dispose();
            ClientApp.StorageOverride = null;
            string root = Path.GetFullPath(Application.temporaryCachePath).TrimEnd(Path.DirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            string target = Path.GetFullPath(_directory);
            Assert.IsTrue(target.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                && Path.GetFileName(target).StartsWith("AccountProfiles-", StringComparison.Ordinal));
            Directory.Delete(target, true);
        }
    }
}
#endif
