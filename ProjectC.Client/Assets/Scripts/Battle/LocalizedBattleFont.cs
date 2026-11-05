using TMPro;
using UnityEngine;

namespace ProjectC.Client
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(TextMeshProUGUI))]
    public sealed class LocalizedBattleFont : MonoBehaviour
    {
        [SerializeField] private BattleFontPalette _palette;
        [SerializeField] private BattleFontRole _role = BattleFontRole.Hud;
        private TMP_Text _label;
        private string _language;
        private static BattleFontPalette _defaultPalette;
        public BattleFontRole Role => _role;

        public static void SetRole(TMP_Text label, BattleFontRole role)
        {
            if (label == null)
            {
                return;
            }
            var view = label.GetComponent<LocalizedBattleFont>() ?? label.gameObject.AddComponent<LocalizedBattleFont>();
            view.Configure(role, view._palette != null ? view._palette : DefaultPalette());
        }

        public void Configure(BattleFontRole role, BattleFontPalette palette)
        {
            _palette = palette;
            _role = role;
            _language = null;
            Apply(Application.isPlaying ? CardPresentation.Library.Localization.Language : "korean");
        }

        private static BattleFontPalette DefaultPalette()
        {
            if (_defaultPalette == null)
            {
                _defaultPalette = Resources.Load<BattleFontPalette>("UI/BattleFontPalette");
            }
            return _defaultPalette;
        }

        private void OnEnable()
        {
            if (Application.isPlaying)
            {
                Apply(CardPresentation.Library.Localization.Language);
            }
        }

        private void LateUpdate()
        {
            string language = CardPresentation.Library.Localization.Language;
            if (_language != language)
            {
                Apply(language);
            }
        }

        public void Apply(string language)
        {
            _palette = _palette != null ? _palette : DefaultPalette();
            if (_palette == null)
            {
                Debug.LogError("Battle UI requires Resources/UI/BattleFontPalette.", this);
                enabled = false;
                return;
            }
            _label = _label != null ? _label : GetComponent<TMP_Text>();
            var font = _palette.Resolve(_role, language);
            if (font == null)
            {
                Debug.LogError("Battle font palette is missing font for " + _role + ".", this);
                enabled = false;
                return;
            }
            _label.font = font;
            _label.fontSharedMaterial = font.material;
            // The supplied files already contain their intended medium/semibold/bold weight.
            _label.fontStyle &= ~FontStyles.Bold;
            _label.UpdateMeshPadding();
            _language = language;
        }
    }
}
