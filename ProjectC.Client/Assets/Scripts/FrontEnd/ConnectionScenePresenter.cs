using System.Collections;
using Game.Utils;
using TMPro;
using UnityEngine;

namespace ProjectC.Client
{
    public sealed class ConnectionScenePresenter : MonoBehaviour
    {
        private ClientApp _app;
        private TMP_Text _loading;
        private Coroutine _attempt;
        private bool _succeeded;

        private void Start()
        {
            _app = ClientApp.Instance;
            if (_app == null || !this.Bind("Loading", out _loading))
            {
                throw new System.InvalidOperationException("ConnectionScene requires ClientServices and Loading text.");
            }
            Retry();
        }

        private void Retry()
        {
            if (_attempt != null || !isActiveAndEnabled)
            {
                return;
            }
            _succeeded = false;
            _loading.gameObject.SetActive(true);
            _attempt = StartCoroutine(Connect());
        }

        public void RestartConnection()
        {
            if (_attempt != null)
            {
                StopCoroutine(_attempt);
                _attempt = null;
            }
            Retry();
        }

        private IEnumerator Connect()
        {
            // Yield first so even an already connected session cannot leave a stale handle.
            yield return null;
            while (_app.IsChangingAccount)
            {
                yield return null;
            }
            int generation = _app.AccountGeneration;
            if (_app.AccountLoggedOut)
            {
                _attempt = null;
                _loading.gameObject.SetActive(false);
                _app.ShowPopup("현재 계정 프로필에서 로그아웃했습니다.",
                    new PopupAction("현재 프로필로 로그인", () => _app.ChangeAccountProfile(_app.AccountProfileId)));
                yield break;
            }
            if (_app.UseWebsite)
            {
                _loading.text = "로그인 세션 확인 중";
                var login = _app.PrepareWebsiteAsync();
                while (!login.IsCompleted)
                {
                    if (generation != _app.AccountGeneration)
                    {
                        _attempt = null;
                        yield break;
                    }
                    yield return null;
                }
                if (generation != _app.AccountGeneration)
                {
                    _attempt = null;
                    yield break;
                }
                if (login.IsFaulted || login.IsCanceled)
                {
                    _attempt = null;
                    _loading.gameObject.SetActive(false);
                    string message = login.Exception?.GetBaseException() is SecureSessionStoreException storageError
                        ? storageError.Message : "로그인 또는 사용자 정보 조회에 실패했습니다. 기존 계정 정보는 보존됩니다.";
                    _app.ShowPopup(message, new PopupAction("재시도", Retry));
                    yield break;
                }
            }
            if (!_app.Network.ConnectionReady)
            {
                _app.Network.ConnectTo(_app.ServerAddress);
            }
            float deadline = Time.realtimeSinceStartup + _app.ConnectionTimeoutSeconds;
            int previousDots = -1;
            while (!_app.Network.ConnectionReady && Time.realtimeSinceStartup < deadline)
            {
                if (generation != _app.AccountGeneration)
                {
                    _attempt = null;
                    yield break;
                }
                if (!string.IsNullOrEmpty(_app.Network.ConnectionError))
                {
                    break;
                }
                int dots = Mathf.FloorToInt(Time.unscaledTime * 2) % 4;
                if (previousDots != dots)
                {
                    _loading.text = "서버에 연결 중" + new string('.', dots);
                    previousDots = dots;
                }
                yield return null;
            }
            _attempt = null;
            if (_app.Network.ConnectionReady)
            {
                _succeeded = true;
                _app.Navigate("LobbyScene");
                yield break;
            }
            _app.Network.DisconnectNetwork();
            _loading.gameObject.SetActive(false);
            _app.ShowPopup("서버에 연결할 수 없습니다.", new PopupAction("재시도", Retry));
        }

        private void OnDisable()
        {
            if (_attempt != null)
            {
                StopCoroutine(_attempt);
                _attempt = null;
                if (!_succeeded && _app != null)
                {
                    _app.Network.DisconnectNetwork();
                }
            }
        }
    }
}
