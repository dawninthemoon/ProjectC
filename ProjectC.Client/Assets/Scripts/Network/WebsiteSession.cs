using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine.Networking;

namespace ProjectC.Client
{
    public sealed class WebsiteDeck
    {
        public string id, name, catalogVersion;
        public WebsiteCard[] cards;
        public PersonalDeck ToPersonalDeck()
        {
            return new PersonalDeck
            {
                DeckId = id,
                Name = name,
                Cards = cards.SelectMany(card => Enumerable.Repeat(card.cardId, card.quantity)).ToList(),
            };
        }
    }

    public sealed class WebsiteCard
    {
        public int cardId, quantity;
    }

    public sealed class WebsiteUser
    {
        public string playerId, displayName, createdAt;
    }

    public sealed class WebsiteRank
    {
        public string tier, promotionStatus;
        public int division, rp, mmr;
        public override string ToString() => $"{tier} {division} · {rp} RP · MMR {mmr}";
    }

    public sealed class WebsiteRankChange
    {
        public string playerId, tier, promotionStatus;
        public int division, rp, rpDelta, mmrBefore, mmrAfter;
        public override string ToString() => $"RP {rpDelta:+0;-0;0} · MMR {mmrAfter - mmrBefore:+0;-0;0}";
    }

    internal sealed class StoredWebsiteSession
    {
        public string accessToken, refreshToken, expiresAt, refreshExpiresAt;
        public WebsiteUser user;
        public bool refreshInFlight;
    }

    public sealed class WebsiteApiException : Exception
    {
        public long Status { get; }
        public WebsiteApiException(long status) : base(status == 401
            ? "로그인 세션을 복구할 수 없습니다. 계정 복구가 필요합니다."
            : $"Website API 연결에 실패했습니다. (HTTP {status})")
        {
            Status = status;
        }
    }

    public enum WebsiteResultState
    {
        None,
        AwaitingConfirmation,
        Unconfirmed,
        Stored,
        Complete,
    }

    public sealed class WebsiteSession
    {
        private const int RefreshMarginSeconds = 120;
        private const double ResultConfirmationTimeoutSeconds = 180;
        private double? _resultConfirmationDeadline;
        private readonly string _baseUrl;
        private readonly int _timeoutSeconds;
        private readonly ISessionStore _store;
        private readonly SemaphoreSlim _sessionLock = new SemaphoreSlim(1, 1);
        private StoredWebsiteSession _session;
        private StoredWebsiteSession _pendingPersistence;
        private bool _loaded;
        private bool _retired;
        public string AccountProfileId { get; private set; } = AccountProfiles.Default;
        public string AccessToken => _session?.accessToken;
        public WebsiteUser User { get; private set; }
        public WebsiteRank Rank { get; private set; }
        public IReadOnlyList<WebsiteDeck> Decks { get; private set; } = Array.Empty<WebsiteDeck>();
        public string SelectedDeckId { get; private set; }
        public WebsiteDeck SelectedDeck => Decks.FirstOrDefault(deck => deck.id == SelectedDeckId);
        public WebsiteRankChange LastRankChange { get; private set; }
        public WebsiteResultState ResultState { get; private set; }
        public string ResultStatus => ResultState == WebsiteResultState.Complete ? "랭크 갱신 확인 · " + LastRankChange
            : ResultState == WebsiteResultState.Stored ? "경기 결과 저장 확인 · 최신 랭크 조회 미완료"
            : ResultState == WebsiteResultState.Unconfirmed ? "경기 결과 저장 미확인 · 랭크 반영 여부를 확인할 수 없습니다."
            : ResultState == WebsiteResultState.AwaitingConfirmation ? "경기 결과 저장 확인 대기 중"
            : "";
        public string ResultRankSummary => (ResultState == WebsiteResultState.Complete ? "최신 랭크: "
            : "마지막 조회 랭크 (이번 경기 반영 미확인): ") + Rank;

        public WebsiteSession(string baseUrl, int timeoutSeconds)
            : this(baseUrl, timeoutSeconds, AccountProfiles.Default)
        {
        }

        public WebsiteSession(string baseUrl, int timeoutSeconds, string accountProfileId)
            : this(baseUrl, timeoutSeconds, new SecureSessionStore(baseUrl.TrimEnd('/'), accountProfileId))
        {
            AccountProfileId = AccountProfiles.DevelopmentFeaturesEnabled
                ? AccountProfiles.Validate(accountProfileId) : AccountProfiles.Default;
        }

        internal WebsiteSession(string baseUrl, int timeoutSeconds, ISessionStore store)
        {
            var uri = new Uri(baseUrl);
            if (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback))
            {
                throw new ArgumentException("Website API requires HTTPS or loopback HTTP.");
            }
            _baseUrl = baseUrl.TrimEnd('/');
            _timeoutSeconds = timeoutSeconds;
            _store = store;
        }

        private async Task<JToken> RequestAsync(string method, string path, JToken body = null, string accessToken = null,
            bool allowRetired = false)
        {
            if (_retired && !allowRetired)
            {
                throw new ObjectDisposedException(nameof(WebsiteSession));
            }
            using (var request = new UnityWebRequest(_baseUrl + path, method))
            {
                request.timeout = _timeoutSeconds;
                request.downloadHandler = new DownloadHandlerBuffer();
                if (body != null)
                {
                    request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body.ToString(Formatting.None)));
                    request.SetRequestHeader("Content-Type", "application/json");
                }
                if (!string.IsNullOrEmpty(accessToken))
                {
                    request.SetRequestHeader("Authorization", "Bearer " + accessToken);
                }
                request.redirectLimit = 0;
                var operation = request.SendWebRequest();
                while (!operation.isDone)
                {
                    await Task.Yield();
                }
                if (request.result != UnityWebRequest.Result.Success)
                {
                    // Upstream response/error text may contain private data. Never log it.
                    throw new WebsiteApiException(request.responseCode);
                }
                return string.IsNullOrEmpty(request.downloadHandler.text) ? JValue.CreateNull()
                    : JToken.Parse(request.downloadHandler.text);
            }
        }

        public async Task EnsureSessionAsync(bool forceRefresh = false, string expectedAccessToken = null)
        {
            await _sessionLock.WaitAsync();
            try
            {
                if (_retired)
                {
                    throw new ObjectDisposedException(nameof(WebsiteSession));
                }
                if (!_loaded)
                {
                    string stored = _store.Read();
                    _session = stored == null ? null : JsonConvert.DeserializeObject<StoredWebsiteSession>(stored);
                    if (stored != null && _session == null)
                    {
                        throw new InvalidOperationException("Stored login session is invalid; account recovery is required.");
                    }
                    _loaded = true;
                }
                PersistPendingSession();
                if (forceRefresh && expectedAccessToken != null && AccessToken != expectedAccessToken)
                {
                    forceRefresh = false;
                }
                if (_session != null && !_session.refreshInFlight && !forceRefresh
                    && DateTimeOffset.TryParse(_session.expiresAt, out var expires)
                    && expires > DateTimeOffset.UtcNow.AddSeconds(RefreshMarginSeconds))
                {
                    User = _session.user;
                    return;
                }
                JToken response;
                if (_session == null)
                {
                    response = await RequestAsync("POST", "/api/auth/guest", new JObject { ["displayName"] = "Guest" });
                }
                else if (string.IsNullOrEmpty(_session.refreshToken) || _session.refreshInFlight)
                {
                    // Recover an uncertain rotation using an independent family; never reuse
                    // the old refresh token after response loss (API revokes reused families).
                    response = await RequestAsync("POST", "/api/auth/session", accessToken: _session.accessToken);
                }
                else
                {
                    _session.refreshInFlight = true;
                    try
                    {
                        _store.Write(JsonConvert.SerializeObject(_session));
                    }
                    catch
                    {
                        // No refresh request was sent. Keep the previous token eligible
                        // for retry after secure storage is available again.
                        _session.refreshInFlight = false;
                        throw;
                    }
                    response = await RequestAsync("POST", "/api/auth/refresh", new JObject { ["refreshToken"] = _session.refreshToken });
                }
                var replacement = response.ToObject<StoredWebsiteSession>();
                if (string.IsNullOrEmpty(replacement?.accessToken) || string.IsNullOrEmpty(replacement.refreshToken)
                    || !Guid.TryParse(replacement.user?.playerId, out _))
                {
                    throw new InvalidOperationException("Website API returned an invalid session.");
                }
                _pendingPersistence = replacement;
                PersistPendingSession();
            }
            finally
            {
                _sessionLock.Release();
            }
        }

        private void PersistPendingSession()
        {
            if (_pendingPersistence == null)
            {
                return;
            }
            // A failed write retains the returned pair in memory. Retrying must not
            // create another guest or rotate an already rotated refresh token.
            _store.Write(JsonConvert.SerializeObject(_pendingPersistence));
            _session = _pendingPersistence;
            _pendingPersistence = null;
            User = _session.user;
        }

        private async Task<JToken> UserRequestAsync(string method, string path, JToken body = null)
        {
            await EnsureSessionAsync();
            string usedToken = AccessToken;
            try
            {
                return await RequestAsync(method, path, body, usedToken);
            }
            catch (WebsiteApiException error) when (error.Status == 401)
            {
                // Concurrent callers reuse the first caller's freshly rotated session.
                await EnsureSessionAsync(true, usedToken);
                return await RequestAsync(method, path, body, AccessToken);
            }
        }

        public async Task LoadUserDataAsync()
        {
            User = (await UserRequestAsync("GET", "/api/users/me")).ToObject<WebsiteUser>();
            Decks = (await UserRequestAsync("GET", "/api/decks")).ToObject<WebsiteDeck[]>();
            if (SelectedDeck == null)
            {
                SelectedDeckId = Decks.FirstOrDefault()?.id;
            }
            await RefreshRankAsync();
        }

        public async Task RefreshRankAsync()
        {
            var acknowledgedChange = LastRankChange;
            bool canConfirmResult = ResultState == WebsiteResultState.Stored;
            Rank = (await UserRequestAsync("GET", "/api/rank/me")).ToObject<WebsiteRank>();
            // A successful rank GET alone does not prove that this match was committed.
            if (canConfirmResult && ResultState == WebsiteResultState.Stored
                && ReferenceEquals(acknowledgedChange, LastRankChange))
            {
                ResultState = WebsiteResultState.Complete;
            }
        }

        public void CycleDeck()
        {
            int index = Decks.ToList().FindIndex(deck => deck.id == SelectedDeckId);
            SelectedDeckId = Decks.Count == 0 ? null : Decks[(index + 1) % Decks.Count].id;
        }

        public async Task UploadDeckAsync(PersonalDeck deck)
        {
            var cards = deck.Cards.GroupBy(id => id).Select(group => new WebsiteCard { cardId = group.Key, quantity = group.Count() });
            var result = await UserRequestAsync("POST", "/api/decks", new JObject
            {
                ["name"] = deck.Name,
                ["cards"] = JArray.FromObject(cards),
            });
            SelectedDeckId = result.Value<string>("id");
            await LoadUserDataAsync();
        }

        public void BeginResult()
        {
            LastRankChange = null;
            _resultConfirmationDeadline = null;
            ResultState = WebsiteResultState.AwaitingConfirmation;
        }

        public void WaitForResultConfirmation(double now)
        {
            if (ResultState == WebsiteResultState.AwaitingConfirmation && !_resultConfirmationDeadline.HasValue)
            {
                _resultConfirmationDeadline = now + ResultConfirmationTimeoutSeconds;
            }
        }

        public void CheckResultConfirmation(double now)
        {
            if (_resultConfirmationDeadline.HasValue && now >= _resultConfirmationDeadline.Value)
            {
                MarkResultUnconfirmed();
            }
        }

        public void MarkResultUnconfirmed()
        {
            if (ResultState == WebsiteResultState.AwaitingConfirmation)
            {
                ResultState = WebsiteResultState.Unconfirmed;
            }
        }

        public async Task ApplyRankChangeAsync(WebsiteRankChange change)
        {
            if (change.playerId != User?.playerId)
            {
                return;
            }
            LastRankChange = change;
            ResultState = WebsiteResultState.Stored;
            await RefreshRankAsync();
        }

        public async Task RevokeAsync()
        {
            await _sessionLock.WaitAsync();
            try
            {
                PersistPendingSession();
                if (!_loaded)
                {
                    string stored = _store.Read();
                    _session = stored == null ? null : JsonConvert.DeserializeObject<StoredWebsiteSession>(stored);
                    _loaded = true;
                }
                if (!string.IsNullOrEmpty(_session?.refreshToken) && !_session.refreshInFlight)
                {
                    await RequestAsync("POST", "/api/auth/revoke", new JObject { ["refreshToken"] = _session.refreshToken },
                        allowRetired: true);
                }
                _store.Delete();
                _session = null;
                User = null;
                Rank = null;
                Decks = Array.Empty<WebsiteDeck>();
                SelectedDeckId = null;
            }
            catch
            {
                // A failed logout must remain recoverable without creating another guest.
                _retired = false;
                throw;
            }
            finally
            {
                _sessionLock.Release();
            }
        }

        public async Task RetireAsync()
        {
            _retired = true;
            // Do not cancel an in-flight rotation: its replacement pair must reach the OS store.
            await _sessionLock.WaitAsync();
            try
            {
                PersistPendingSession();
            }
            catch
            {
                _retired = false;
                throw;
            }
            finally
            {
                _sessionLock.Release();
            }
        }
    }
}
