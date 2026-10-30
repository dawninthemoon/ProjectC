using Game.StaticData;

namespace Game.Utils
{
    public static class LocalizeExtensions
    {
        public static string ToLocalizedString(this string lmk)
        {
            return LocalizeDataManager.Instance.GetString(lmk);
        }
    }
}
