using Game.Sim;
using Netcode.P2P;

namespace Scenes.Menus.CharacterSelect
{
    /// <summary>
    /// Per-player selection state for the CharacterSelect screen. Not to be
    /// confused with the runtime <see cref="Game.Sim.GameState"/>; this is a
    /// UI-layer snapshot that gets translated into <see cref="GameOptions"/>
    /// at commit time.
    /// </summary>
    public class PlayerSelectionState
    {
        public SelectPhase Phase;
        public int CharacterIndex;
        public int SkinIndex;
        public ComboMode ComboMode;
        public ManiaDifficulty ManiaDifficulty;
        public BeatCancelWindow BeatCancelWindow = BeatCancelWindow.Medium;

        /// <summary>
        /// Controls preset index into <c>CharacterSelectDirectory._controlsPresets</c>.
        /// Local-only; never included in <see cref="CharacterSelectPayload"/>.
        /// </summary>
        public int ControlsIndex;

        /// <summary>
        /// Currently-highlighted row inside <see cref="PlayerOptionsPanel"/>.
        /// Local-only; not synced to remote peers.
        /// </summary>
        public int OptionsRow;

        public CharacterSelectPayload ToPayload()
        {
            return new CharacterSelectPayload
            {
                Phase = Phase,
                CharacterIndex = CharacterIndex,
                SkinIndex = SkinIndex,
                ComboMode = ComboMode,
                ManiaDifficulty = ManiaDifficulty,
                BeatCancelWindow = BeatCancelWindow,
            };
        }

        public void ApplyPayload(in CharacterSelectPayload payload)
        {
            Phase = payload.Phase;
            CharacterIndex = payload.CharacterIndex;
            SkinIndex = payload.SkinIndex;
            ComboMode = payload.ComboMode;
            ManiaDifficulty = payload.ManiaDifficulty;
            BeatCancelWindow = payload.BeatCancelWindow;
        }
    }

    public class CharacterSelectState
    {
        public readonly PlayerSelectionState[] Players = new PlayerSelectionState[2];

        public CharacterSelectState()
        {
            Players[0] = new PlayerSelectionState();
            Players[1] = new PlayerSelectionState();
        }

        public bool BothConfirmed =>
            Players[0].Phase == SelectPhase.Confirmed && Players[1].Phase == SelectPhase.Confirmed;
    }
}
