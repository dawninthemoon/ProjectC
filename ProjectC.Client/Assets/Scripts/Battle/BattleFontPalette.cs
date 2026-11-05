using TMPro;
using UnityEngine;

namespace ProjectC.Client
{
    public enum BattleFontRole
    {
        Heading,
        Body,
        Hud,
        Number,
    }

    public sealed class BattleFontPalette : ScriptableObject
    {
        [SerializeField] private TMP_FontAsset _koreanHeading;
        [SerializeField] private TMP_FontAsset _japaneseHeading;
        [SerializeField] private TMP_FontAsset _koreanBody;
        [SerializeField] private TMP_FontAsset _japaneseBody;
        [SerializeField] private TMP_FontAsset _koreanHud;
        [SerializeField] private TMP_FontAsset _japaneseHud;
        [SerializeField] private TMP_FontAsset _numbers;

        public TMP_FontAsset Resolve(BattleFontRole role, string language)
        {
            bool japanese = language == "japanese";
            return role switch
            {
                BattleFontRole.Heading => japanese ? _japaneseHeading : _koreanHeading,
                BattleFontRole.Body => japanese ? _japaneseBody : _koreanBody,
                BattleFontRole.Number => _numbers,
                _ => japanese ? _japaneseHud : _koreanHud,
            };
        }
    }
}
