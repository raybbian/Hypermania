using Netcode.P2P;
using Hypermania.Game;
using Hypermania.Shared;

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
        public SuperInputMode SuperInputMode = SuperInputMode.Hold;

        /// <summary>
        /// Name of the disk-backed <c>ControlsProfile</c> the player most
        /// recently landed on in the controls menu. Empty when the menu was
        /// never opened — <see cref="GameRunner"/> then falls back to
        /// <see cref="Game.View.Configs.Input.ControlsConfig.DefaultBindings"/>. Local-only.
        /// </summary>
        public string ControlsProfileName;

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
                SuperInputMode = SuperInputMode,
                OptionsRow = OptionsRow,
            };
        }

        public void ApplyPayload(in CharacterSelectPayload payload)
        {
            Phase = payload.Phase;
            CharacterIndex = payload.CharacterIndex;
            SkinIndex = payload.SkinIndex;
            ComboMode = payload.ComboMode;
            ManiaDifficulty = payload.ManiaDifficulty;
            SuperInputMode = payload.SuperInputMode;
            OptionsRow = payload.OptionsRow;
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
