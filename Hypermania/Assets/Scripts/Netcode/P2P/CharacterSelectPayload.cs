using System.Globalization;
using Game.Sim;
using Scenes.Menus.CharacterSelect;

namespace Netcode.P2P
{
    /// <summary>
    /// Wire format for character-select sync over Steam lobby member data.
    /// Controls preset is omitted because input bindings are inherently local.
    ///
    /// Format: <c>v1|phase|char|skin|combo|maniaDiff|beatWin</c>
    /// All fields are integer enum values encoded as decimal text.
    /// </summary>
    public struct CharacterSelectPayload
    {
        public const string Version = "v1";

        public SelectPhase Phase;
        public int CharacterIndex;
        public int SkinIndex;
        public ComboMode ComboMode;
        public ManiaDifficulty ManiaDifficulty;
        public BeatCancelWindow BeatCancelWindow;

        public string Serialize()
        {
            return string.Join(
                "|",
                Version,
                ((int)Phase).ToString(CultureInfo.InvariantCulture),
                CharacterIndex.ToString(CultureInfo.InvariantCulture),
                SkinIndex.ToString(CultureInfo.InvariantCulture),
                ((int)ComboMode).ToString(CultureInfo.InvariantCulture),
                ((int)ManiaDifficulty).ToString(CultureInfo.InvariantCulture),
                ((int)BeatCancelWindow).ToString(CultureInfo.InvariantCulture)
            );
        }

        public static bool TryParse(string text, out CharacterSelectPayload payload)
        {
            payload = default;
            if (string.IsNullOrEmpty(text))
                return false;

            string[] parts = text.Split('|');
            if (parts.Length != 7)
                return false;
            if (parts[0] != Version)
                return false;

            if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int phase))
                return false;
            if (!int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int character))
                return false;
            if (!int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int skin))
                return false;
            if (!int.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int combo))
                return false;
            if (!int.TryParse(parts[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out int maniaDiff))
                return false;
            if (!int.TryParse(parts[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out int beatWin))
                return false;

            payload = new CharacterSelectPayload
            {
                Phase = (SelectPhase)phase,
                CharacterIndex = character,
                SkinIndex = skin,
                ComboMode = (ComboMode)combo,
                ManiaDifficulty = (ManiaDifficulty)maniaDiff,
                BeatCancelWindow = (BeatCancelWindow)beatWin,
            };
            return true;
        }
    }
}
