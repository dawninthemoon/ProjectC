using UnityEngine;

namespace ProjectC.Client
{
    public enum ServerEnvironment
    {
        Local,
        Production
    }

    [CreateAssetMenu(menuName = "Project C/Server connection settings")]
    public sealed class ServerConnectionSettings : ScriptableObject
    {
        public const string LocalAddress = "ws://127.0.0.1:7777";
        public const string ProductionAddress = "wss://projectc-server.onrender.com";
        public const string ProductionApiAddress = "https://projectc-api.onrender.com";
        public const string LocalApiAddress = "http://127.0.0.1:8080";
        [SerializeField] private bool _localWebsiteEnabled;
        [SerializeField] private string _productionApiAddress = ProductionApiAddress;
        [SerializeField] private string _localApiAddress = LocalApiAddress;
        [SerializeField] private string _productionBattleAddress = ProductionAddress;
        [SerializeField] private string _localBattleAddress = LocalAddress;
        public bool LocalWebsiteEnabled => _localWebsiteEnabled;
        public string ApiAddressFor(ServerEnvironment environment) => environment == ServerEnvironment.Local
            ? _localApiAddress : _productionApiAddress;
        public string BattleAddressFor(ServerEnvironment environment) => environment == ServerEnvironment.Local
            ? _localBattleAddress : _productionBattleAddress;
        public const float DefaultTimeoutSeconds = 60f;
        [SerializeField] private ServerEnvironment _defaultEnvironment = ServerEnvironment.Production;
        [SerializeField, Min(1)] private float _timeoutSeconds = DefaultTimeoutSeconds;
        public ServerEnvironment DefaultEnvironment => _defaultEnvironment;
        public float TimeoutSeconds => Mathf.Max(1, _timeoutSeconds);
        public static string AddressFor(ServerEnvironment environment)
        {
            return environment == ServerEnvironment.Local ? LocalAddress : ProductionAddress;
        }
    }
}
