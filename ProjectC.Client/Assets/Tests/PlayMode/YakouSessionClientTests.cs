using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using ProjectC.Client;

namespace ProjectC.Tests
{
    public sealed class YakouSessionClientTests
    {
        private static JObject Message(string type, string match, JObject payload, string requestId = "")
        {
            return new JObject
            {
                ["ProtocolVersion"] = 2,
                ["Type"] = type,
                ["MatchId"] = match,
                ["RequestId"] = requestId,
                ["Payload"] = payload,
            };
        }

        private static JObject Snapshot(long sequence)
        {
            return new JObject { ["Sequence"] = sequence, ["State"] = new JObject { ["TurnNumber"] = sequence } };
        }

        [Test]
        public void AcceptedCommandWaitsForSnapshotAndIgnoresOlderState()
        {
            var sent = new List<string>();
            var client = new YakouSessionClient(sent.Add);
            client.Create();
            var bind = JObject.Parse(sent[0]);
            client.ApplyMessage(Message("YakouCreated", "Y1", new JObject { ["Seat"] = 1, ["ResumeToken"] = "secret" }, (string)bind["RequestId"]));
            sent.Clear();
            client.ApplyMessage(Message("YakouSnapshot", "Y1", Snapshot(0)));
            client.Command(new JObject { ["Type"] = "StartTurn" });
            var request = JObject.Parse(sent[0]);
            Assert.Throws<InvalidOperationException>(() => client.Command(new JObject { ["Type"] = "StartTurn" }));
            client.ApplyMessage(Message("YakouCommandResult", "Y1", new JObject
            {
                ["Accepted"] = true, ["Reason"] = "", ["Sequence"] = 1,
            }, (string)request["RequestId"]));
            Assert.IsTrue(client.Busy);
            Assert.AreEqual("YakouSnapshot", (string)JObject.Parse(sent[1])["Type"]);
            client.ApplyMessage(Message("YakouSnapshot", "Y1", Snapshot(1)));
            client.ApplyMessage(Message("YakouSnapshot", "Y1", Snapshot(0)));
            Assert.IsFalse(client.Busy);
            Assert.AreEqual(1, client.Sequence);
            Assert.AreEqual(1, (long)client.State["TurnNumber"]);
        }

        [Test]
        public void ReconnectRotatesTokenAndRetryPreservesRequestIdAndPayload()
        {
            var sent = new List<string>();
            var client = new YakouSessionClient(sent.Add);
            client.Join("Y1");
            var bind = JObject.Parse(sent[0]);
            client.ApplyMessage(Message("YakouJoined", "Y1", new JObject { ["Seat"] = 2, ["ResumeToken"] = "old" }, (string)bind["RequestId"]));
            sent.Clear();
            client.ApplyMessage(Message("YakouSnapshot", "Y1", Snapshot(0)));
            client.Command(new JObject { ["Type"] = "StartTurn" });
            string original = sent[0];
            client.Disconnected();
            client.Resume();
            Assert.AreEqual("old", (string)JObject.Parse(sent[1])["ResumeToken"]);
            client.ApplyMessage(Message("YakouResumed", "Y1", new JObject { ["Seat"] = 2, ["ResumeToken"] = "new" }, (string)JObject.Parse(sent[1])["RequestId"]));
            client.ApplyMessage(Message("YakouSnapshot", "Y1", Snapshot(1)));
            client.RetryPending();
            Assert.AreEqual(original, sent[2]);
            client.Disconnected();
            client.Resume();
            Assert.AreEqual("new", (string)JObject.Parse(sent[3])["ResumeToken"]);
        }

        [Test]
        public void RejectedCommandCanBeRetriedWithNewRequest()
        {
            var sent = new List<string>();
            var client = new YakouSessionClient(sent.Add);
            client.Create();
            var bind = JObject.Parse(sent[0]);
            client.ApplyMessage(Message("YakouCreated", "Y1", new JObject { ["Seat"] = 1, ["ResumeToken"] = "secret" }, (string)bind["RequestId"]));
            sent.Clear();
            client.ApplyMessage(Message("YakouSnapshot", "Y1", Snapshot(0)));
            client.Command(new JObject { ["Type"] = "StartTurn" });
            var id = (string)JObject.Parse(sent[0])["RequestId"];
            client.ApplyMessage(Message("YakouRejected", "Y1", new JObject { ["Reason"] = "WaitingForOpponent" }, id));
            Assert.IsFalse(client.Busy);
            Assert.AreEqual("WaitingForOpponent", client.Error);
            client.Command(new JObject { ["Type"] = "StartTurn" });
            Assert.AreNotEqual(id, (string)JObject.Parse(sent[1])["RequestId"]);
        }

        [Test]
        public void BindingReplyMustMatchRequestAndServerErrorUnblocksIt()
        {
            var sent = new List<string>();
            var client = new YakouSessionClient(sent.Add);
            client.Create();
            string id = (string)JObject.Parse(sent[0])["RequestId"];
            client.ApplyMessage(Message("YakouCreated", "stale", new JObject
            {
                ["Seat"] = 1, ["ResumeToken"] = "secret",
            }, "different"));
            Assert.IsFalse(client.Bound);
            Assert.IsTrue(client.Busy);
            client.ApplyMessage(new JObject
            {
                ["ProtocolVersion"] = 2, ["Type"] = "ServerError", ["RequestId"] = id,
                ["Reason"] = "InvalidRequest",
            });
            Assert.IsFalse(client.Busy);
            Assert.AreEqual("InvalidRequest", client.Error);
            client.Create();
            Assert.AreNotEqual(id, (string)JObject.Parse(sent[1])["RequestId"]);
        }
    }
}
