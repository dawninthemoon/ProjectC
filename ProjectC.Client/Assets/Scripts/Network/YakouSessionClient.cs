using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ProjectC.Client
{
    // This controller only handles the Yakou wire protocol. The server owns all
    // rules and sends a separate viewer projection to each connected seat.
    public sealed class YakouSessionClient
    {
        private const int ProtocolVersion = 2;
        private readonly Action<string> _send;
        private string _resumeToken;
        private string _pendingRequest;
        private JObject _pendingEnvelope;
        private long _acceptedSequence = -1;
        private string _bindingRequest;
        private string _bindingType;
        private string _bindingMatch;

        public string MatchId { get; private set; }
        public int Seat { get; private set; }
        public long Sequence { get; private set; } = -1;
        public JObject State { get; private set; }
        public string Error { get; private set; }
        public bool Bound { get; private set; }
        public bool Busy => _bindingRequest != null || _pendingRequest != null || _acceptedSequence >= 0;
        public bool HasPendingCommand => _pendingEnvelope != null;
        public event Action Changed;

        public YakouSessionClient(Action<string> send)
        {
            _send = send ?? throw new ArgumentNullException(nameof(send));
        }

        private void Send(JObject envelope)
        {
            _send(envelope.ToString(Formatting.None));
        }

        private JObject Request(string type)
        {
            return new JObject
            {
                ["Type"] = type,
                ["ProtocolVersion"] = ProtocolVersion,
                ["RequestId"] = Guid.NewGuid().ToString("N"),
            };
        }

        public void Create()
        {
            RequireUnbound();
            SendBinding(Request("YakouCreate"), "YakouCreated");
        }

        public void Join(string matchId)
        {
            RequireUnbound();
            if (string.IsNullOrWhiteSpace(matchId))
            {
                throw new ArgumentException("Match ID is required.", nameof(matchId));
            }
            var request = Request("YakouJoin");
            request["MatchId"] = matchId;
            SendBinding(request, "YakouJoined");
        }

        public void Resume()
        {
            if (Bound || _bindingRequest != null || string.IsNullOrEmpty(MatchId) || string.IsNullOrEmpty(_resumeToken))
            {
                throw new InvalidOperationException("No disconnected Yakou session to resume.");
            }
            var request = Request("YakouResume");
            request["MatchId"] = MatchId;
            request["ResumeToken"] = _resumeToken;
            SendBinding(request, "YakouResumed");
        }

        private void SendBinding(JObject request, string responseType)
        {
            _bindingRequest = (string)request["RequestId"];
            _bindingType = responseType;
            _bindingMatch = (string)request["MatchId"];
            try
            {
                Send(request);
            }
            catch
            {
                _bindingRequest = null;
                _bindingType = null;
                _bindingMatch = null;
                throw;
            }
            Changed?.Invoke();
        }

        public void Disconnected()
        {
            if (_bindingRequest != null)
            {
                Error = "ConnectionLostDuringBinding";
            }
            Bound = false;
            State = null;
            _bindingRequest = null;
            _bindingType = null;
            _bindingMatch = null;
            _acceptedSequence = -1;
            Changed?.Invoke();
        }

        public void ForgetDisconnectedRoom()
        {
            if (Bound || _bindingRequest != null)
            {
                throw new InvalidOperationException("Leave the active room first.");
            }
            MatchId = null;
            Seat = 0;
            Sequence = -1;
            State = null;
            _resumeToken = null;
            _pendingRequest = null;
            _pendingEnvelope = null;
            _acceptedSequence = -1;
            Error = null;
            Changed?.Invoke();
        }

        public void RequestSnapshot()
        {
            RequireBound();
            var request = Request("YakouSnapshot");
            request["MatchId"] = MatchId;
            Send(request);
        }

        public void Leave()
        {
            RequireBound();
            var request = Request("YakouLeave");
            request["MatchId"] = MatchId;
            Send(request);
        }

        public void Command(JObject command)
        {
            RequireBound();
            if (Busy || State == null)
            {
                throw new InvalidOperationException("Wait for the current Yakou snapshot.");
            }
            if (command == null || command["Type"]?.Type != JTokenType.String)
            {
                throw new ArgumentException("A command type is required.", nameof(command));
            }
            _pendingEnvelope = Request("YakouCommand");
            _pendingEnvelope["MatchId"] = MatchId;
            _pendingEnvelope["Command"] = command.DeepClone();
            _pendingRequest = (string)_pendingEnvelope["RequestId"];
            try
            {
                Send(_pendingEnvelope);
            }
            catch
            {
                _pendingRequest = null;
                _pendingEnvelope = null;
                throw;
            }
            Changed?.Invoke();
        }

        // A lost result may have committed. Retry only the identical envelope;
        // the server caches request IDs across reconnects.
        public void RetryPending()
        {
            RequireBound();
            if (_pendingEnvelope == null)
            {
                throw new InvalidOperationException("No Yakou request to retry.");
            }
            Send(_pendingEnvelope);
        }

        public bool ApplyMessage(JObject message)
        {
            string type = (string)message["Type"];
            if (type == "ServerError" && (string)message["RequestId"] != null
                && ((string)message["RequestId"] == _bindingRequest || (string)message["RequestId"] == _pendingRequest))
            {
                Error = (string)message["Reason"] ?? "ServerError";
                _bindingRequest = null;
                _bindingType = null;
                _bindingMatch = null;
                _pendingRequest = null;
                _pendingEnvelope = null;
                Changed?.Invoke();
                return true;
            }
            if (type == null || !type.StartsWith("Yakou", StringComparison.Ordinal))
            {
                return false;
            }
            if ((int?)message["ProtocolVersion"] != ProtocolVersion)
            {
                throw new InvalidOperationException("Yakou protocol version mismatch.");
            }
            string match = (string)message["MatchId"];
            var payload = message["Payload"] as JObject ?? throw new FormatException("Missing Yakou payload.");
            if (type == "YakouCreated" || type == "YakouJoined" || type == "YakouResumed")
            {
                if (_bindingType != type || (string)message["RequestId"] != _bindingRequest)
                {
                    return true;
                }
                int seat = (int?)payload["Seat"] ?? 0;
                string token = (string)payload["ResumeToken"];
                if (string.IsNullOrEmpty(match) || string.IsNullOrEmpty(token) || (seat != 1 && seat != 2)
                    || (_bindingMatch != null && match != _bindingMatch)
                    || (type == "YakouCreated" && seat != 1)
                    || (type == "YakouJoined" && seat != 2)
                    || (type == "YakouResumed" && seat != Seat))
                {
                    throw new FormatException("Invalid Yakou session binding.");
                }
                MatchId = match;
                Seat = seat;
                _resumeToken = token;
                Bound = true;
                _bindingRequest = null;
                _bindingType = null;
                _bindingMatch = null;
                Error = null;
            }
            else if (type == "YakouRejected")
            {
                Error = (string)payload["Reason"] ?? "Rejected";
                if ((string)message["RequestId"] == _bindingRequest && _bindingRequest != null)
                {
                    _bindingRequest = null;
                    _bindingType = null;
                    _bindingMatch = null;
                }
                if ((string)message["RequestId"] == _pendingRequest)
                {
                    _pendingRequest = null;
                    _pendingEnvelope = null;
                }
            }
            else if (match == MatchId && type == "YakouSnapshot" && Bound)
            {
                long next = (long?)payload["Sequence"] ?? -1;
                var state = payload["State"] as JObject;
                if (next < 0 || state == null)
                {
                    throw new FormatException("Invalid Yakou snapshot.");
                }
                if (next >= Sequence)
                {
                    Sequence = next;
                    State = (JObject)state.DeepClone();
                    if (_acceptedSequence >= 0 && next >= _acceptedSequence)
                    {
                        _acceptedSequence = -1;
                    }
                }
            }
            else if (match == MatchId && type == "YakouCommandResult" && (string)message["RequestId"] == _pendingRequest)
            {
                bool accepted = (bool?)payload["Accepted"] ?? throw new FormatException("Missing command result.");
                long next = (long?)payload["Sequence"] ?? -1;
                if (next < 0)
                {
                    throw new FormatException("Invalid command sequence.");
                }
                _pendingRequest = null;
                _pendingEnvelope = null;
                Error = accepted ? null : (string)payload["Reason"] ?? "Rejected";
                if (accepted && (State == null || Sequence < next))
                {
                    _acceptedSequence = next;
                    // Cached retry results do not trigger a broadcast.
                    RequestSnapshot();
                }
                else if (!accepted && (State == null || Sequence < next))
                {
                    _acceptedSequence = next;
                    RequestSnapshot();
                }
            }
            else if (match == MatchId && (type == "YakouClosed" || type == "YakouReplaced"))
            {
                Bound = false;
                State = null;
                Error = (string)payload["Reason"];
                if (type == "YakouClosed")
                {
                    MatchId = null;
                    _resumeToken = null;
                    _pendingRequest = null;
                    _pendingEnvelope = null;
                    _acceptedSequence = -1;
                    Sequence = -1;
                    Seat = 0;
                }
            }
            Changed?.Invoke();
            return true;
        }

        private void RequireBound()
        {
            if (!Bound)
            {
                throw new InvalidOperationException("Yakou session is disconnected.");
            }
        }

        private void RequireUnbound()
        {
            if (Bound || _bindingRequest != null || !string.IsNullOrEmpty(MatchId))
            {
                throw new InvalidOperationException("A Yakou session already exists.");
            }
        }
    }
}
