using UnityEngine;

namespace ProjectC.Client
{
    [CreateAssetMenu(menuName = "Project C/Battle cost presentation")]
    public sealed class BattleCostDefinition : ScriptableObject
    {
        [SerializeField] private string _id;
        [SerializeField] private string _nameKey;
        [SerializeField] private string _descriptionKey;
        [SerializeField] private Sprite _icon;
        public string Id => _id;
        public string Name => BattleText.Get(_nameKey);
        public string Description => BattleText.Get(_descriptionKey);
        public Sprite Icon => _icon;
    }

    // Presentation snapshot only. Resource authority and spending remain in the server.
    public readonly struct BattleCostDisplay
    {
        public BattleCostDefinition Definition { get; }
        public int Current { get; }
        public int? Maximum { get; }
        public string Amount => Maximum.HasValue ? $"{Current}/{Maximum.Value}" : Current.ToString();
        public BattleCostDisplay(BattleCostDefinition definition, int current, int? maximum = null)
        {
            Definition = definition;
            Current = current;
            Maximum = maximum;
        }
    }
}
