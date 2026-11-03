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
