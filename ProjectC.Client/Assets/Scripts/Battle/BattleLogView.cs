using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
namespace ProjectC.Client
{
    public sealed class BattleLogView : MonoBehaviour
    {
        public Text Recent, Full, Feedback;
        public GameObject History;
        readonly Queue<string> lines = new Queue<string>();
        public void Add(string line)
        {
            lines.Enqueue(line); while (lines.Count > 40) lines.Dequeue();
            var all = lines.ToArray(); Full.text = string.Join("\n", all);
            int first = Mathf.Max(0, all.Length - 5);
            Recent.text = string.Join("\n", all, first, all.Length - first);
            if (line.StartsWith("Rejected:") || line.StartsWith("Protocol error") || line.StartsWith("Disconnected")) Feedback.text = line;
        }
        public void ClearFeedback() { Feedback.text = ""; }
        public void Toggle() { History.SetActive(!History.activeSelf); }
    }
}
