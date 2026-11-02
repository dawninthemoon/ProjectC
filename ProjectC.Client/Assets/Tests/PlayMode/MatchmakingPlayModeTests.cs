#if UNITY_EDITOR
using System.Reflection;
using NUnit.Framework;
using ProjectC.Client;
using UnityEngine;

namespace ProjectC.Tests
{
    public sealed class MatchmakingPlayModeTests
    {
        GameObject _root;
        MultiplayerDebugScreen _client;

        [SetUp]
        public void Setup()
        {
            _root = new GameObject("Matchmaking test", typeof(MultiplayerDebugScreen));
            _client = _root.GetComponent<MultiplayerDebugScreen>();
            typeof(MultiplayerDebugScreen).GetMethod("ResetMatchmaking", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(_client, new object[] { MatchmakingState.Queuing, "" });
            Field("_queueRequestId", "queue");
            Field("_cancelQueueRequestId", "cancel");
        }

        void Field(string name, object value)
        {
            typeof(MultiplayerDebugScreen).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(_client, value);
        }

        void Message(string body)
        {
            _client.ApplyMessage("{\"ProtocolVersion\":2," + body + "}");
        }

        [TearDown]
        public void Cleanup()
        {
            UnityEngine.Object.DestroyImmediate(_root);
        }

        [Test]
        public void QueueAcknowledgementAndCancellationRequireMatchingRequest()
        {
            Message("\"Type\":\"MatchmakingQueued\",\"RequestId\":\"old\",\"QueuePosition\":9");
            Assert.AreEqual(0, _client.QueuePosition);
            Message("\"Type\":\"MatchmakingQueued\",\"RequestId\":\"queue\",\"QueuePosition\":1");
            Assert.AreEqual(1, _client.QueuePosition);
            Message("\"Type\":\"MatchmakingCancelled\",\"RequestId\":\"old\"");
            Assert.AreEqual(MatchmakingState.Queuing, _client.Matchmaking);
            Message("\"Type\":\"MatchmakingCancelled\",\"RequestId\":\"cancel\"");
            Assert.AreEqual(MatchmakingState.Idle, _client.Matchmaking);
            Assert.IsFalse(_client.Busy);
        }

        [TestCase("AlreadyQueued", MatchmakingState.Queuing)]
        [TestCase("AlreadyJoined", MatchmakingState.Error)]
        [TestCase("InvalidDeck", MatchmakingState.Error)]
        [TestCase("NotQueued", MatchmakingState.Idle)]
        public void QueueErrorsRecover(string reason, MatchmakingState expected)
        {
            Message("\"Type\":\"ServerError\",\"RequestId\":\"queue\",\"Reason\":\"" + reason + "\"");
            Assert.AreEqual(expected, _client.Matchmaking);
            StringAssert.Contains(reason, _client.MatchmakingError);
        }

        [Test]
        public void MatchWinsCancelRaceAndDuplicateFoundPreservesSnapshotAndMulligan()
        {
            Message("\"Type\":\"MatchJoined\",\"MatchId\":\"M1\",\"GameNumber\":1,\"PlayerId\":2,\"SessionToken\":\"test\"");
            Message("\"Type\":\"StateSnapshot\",\"MatchId\":\"M1\",\"GameNumber\":1,\"SequenceNumber\":3,\"Payload\":{\"Started\":true,\"MulliganActive\":true}");
            var snapshot = _client.State;
            _client.MulliganSelection.Add(42);
            Field("pending", true);
            Field("deckRequestId", "deck");
            Message("\"Type\":\"MatchmakingFound\",\"MatchId\":\"M1\"");
            Message("\"Type\":\"MatchmakingFound\",\"MatchId\":\"M1\"");
            Message("\"Type\":\"ServerError\",\"RequestId\":\"cancel\",\"MatchId\":\"M1\",\"Reason\":\"NotQueued\"");
            Message("\"Type\":\"MatchmakingCancelled\",\"RequestId\":\"cancel\"");
            Assert.AreEqual(MatchmakingState.Matched, _client.Matchmaking);
            Assert.AreSame(snapshot, _client.State);
            Assert.AreEqual(3, _client.Sequence);
            Assert.IsTrue(_client.MulliganSelection.Contains(42));
            Assert.IsTrue(_client.Busy, "Late cancel must not clear a pending deck submission");
        }

        [Test]
        public void ForeignFoundAndDisconnectedQueueCannotBecomeMatched()
        {
            Message("\"Type\":\"MatchmakingFound\",\"MatchId\":\"foreign\"");
            Assert.AreEqual(MatchmakingState.Queuing, _client.Matchmaking);
            _client.DisconnectNetwork();
            Message("\"Type\":\"MatchmakingQueued\",\"RequestId\":\"queue\",\"QueuePosition\":1");
            Assert.AreEqual(MatchmakingState.Error, _client.Matchmaking);
            Assert.AreEqual(0, _client.QueuePosition);
        }
    }
}
#endif
