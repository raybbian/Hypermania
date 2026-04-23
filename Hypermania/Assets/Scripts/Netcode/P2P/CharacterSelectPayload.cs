using System.Globalization;
using Game.Sim;
using Scenes.Menus.CharacterSelect;

namespace Netcode.P2P
{
    /// <summary>
    /// Wire format for a single slot's character-select selection. Controls
    /// preset is omitted because input bindings are inherently local.
    ///
    /// Format: <c>phase|char|skin|combo|maniaDiff|beatWin|optionsRow</c>
    /// All fields are integer enum values encoded as decimal text. Version
    /// prefix / field count are handled by the enclosing broadcast payload
    /// (see <see cref="CharacterSelectBroadcastPayload"/>).
    /// </summary>
    public struct CharacterSelectPayload
    {
        public const int FieldCount = 7;

        public SelectPhase Phase;
        public int CharacterIndex;
        public int SkinIndex;
        public ComboMode ComboMode;
        public ManiaDifficulty ManiaDifficulty;
        public BeatCancelWindow BeatCancelWindow;
        public int OptionsRow;

        public void AppendFields(System.Text.StringBuilder sb)
        {
            sb.Append(((int)Phase).ToString(CultureInfo.InvariantCulture)).Append('|');
            sb.Append(CharacterIndex.ToString(CultureInfo.InvariantCulture)).Append('|');
            sb.Append(SkinIndex.ToString(CultureInfo.InvariantCulture)).Append('|');
            sb.Append(((int)ComboMode).ToString(CultureInfo.InvariantCulture)).Append('|');
            sb.Append(((int)ManiaDifficulty).ToString(CultureInfo.InvariantCulture)).Append('|');
            sb.Append(((int)BeatCancelWindow).ToString(CultureInfo.InvariantCulture)).Append('|');
            sb.Append(OptionsRow.ToString(CultureInfo.InvariantCulture));
        }

        public static bool TryParseFields(string[] parts, int offset, out CharacterSelectPayload payload)
        {
            payload = default;
            if (parts == null || offset < 0 || parts.Length - offset < FieldCount)
                return false;

            if (!int.TryParse(parts[offset + 0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int phase))
                return false;
            if (!int.TryParse(parts[offset + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int character))
                return false;
            if (!int.TryParse(parts[offset + 2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int skin))
                return false;
            if (!int.TryParse(parts[offset + 3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int combo))
                return false;
            if (!int.TryParse(parts[offset + 4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int maniaDiff))
                return false;
            if (!int.TryParse(parts[offset + 5], NumberStyles.Integer, CultureInfo.InvariantCulture, out int beatWin))
                return false;
            if (!int.TryParse(parts[offset + 6], NumberStyles.Integer, CultureInfo.InvariantCulture, out int optionsRow))
                return false;

            payload = new CharacterSelectPayload
            {
                Phase = (SelectPhase)phase,
                CharacterIndex = character,
                SkinIndex = skin,
                ComboMode = (ComboMode)combo,
                ManiaDifficulty = (ManiaDifficulty)maniaDiff,
                BeatCancelWindow = (BeatCancelWindow)beatWin,
                OptionsRow = optionsRow,
            };
            return true;
        }
    }

    /// <summary>
    /// Host-authoritative broadcast carrying both slots' selections at once.
    /// Written by the host to Steam lobby data under
    /// <see cref="CharacterSelectNetSync.LobbyStateKey"/>; non-hosts read and
    /// apply to their local <c>CharacterSelectState</c>.
    ///
    /// Format: <c>v3|&lt;slot0 fields&gt;|&lt;slot1 fields&gt;</c>
    /// (version + 2 × 7 slot fields = 15 parts).
    /// </summary>
    public struct CharacterSelectBroadcastPayload
    {
        public const string Version = "v3";

        public CharacterSelectPayload Slot0;
        public CharacterSelectPayload Slot1;

        public string Serialize()
        {
            var sb = new System.Text.StringBuilder(64);
            sb.Append(Version).Append('|');
            Slot0.AppendFields(sb);
            sb.Append('|');
            Slot1.AppendFields(sb);
            return sb.ToString();
        }

        public static bool TryParse(string text, out CharacterSelectBroadcastPayload payload)
        {
            payload = default;
            if (string.IsNullOrEmpty(text))
                return false;

            string[] parts = text.Split('|');
            if (parts.Length != 1 + 2 * CharacterSelectPayload.FieldCount)
                return false;
            if (parts[0] != Version)
                return false;

            if (!CharacterSelectPayload.TryParseFields(parts, 1, out CharacterSelectPayload slot0))
                return false;
            if (!CharacterSelectPayload.TryParseFields(parts, 1 + CharacterSelectPayload.FieldCount, out CharacterSelectPayload slot1))
                return false;

            payload = new CharacterSelectBroadcastPayload { Slot0 = slot0, Slot1 = slot1 };
            return true;
        }
    }
}
