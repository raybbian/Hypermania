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
        /// Synced to remote peers so the remote panel can mirror the peer's
        /// highlight position.
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
                OptionsRow = OptionsRow,
            };
        }

        /// <summary>
        /// Copies every synced field from <paramref name="payload"/>.
        /// <see cref="ControlsIndex"/> is intentionally not touched — it is
        /// local-only on every client and never part of the wire format.
        /// </summary>
        public void ApplyPayload(in CharacterSelectPayload payload)
        {
            Phase = payload.Phase;
            CharacterIndex = payload.CharacterIndex;
            SkinIndex = payload.SkinIndex;
            ComboMode = payload.ComboMode;
            ManiaDifficulty = payload.ManiaDifficulty;
            BeatCancelWindow = payload.BeatCancelWindow;
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

        /// <summary>
        /// Applies a host broadcast to both slots. Each slot's
        /// <see cref="PlayerSelectionState.ControlsIndex"/> is preserved
        /// because the broadcast never carries it — controls bindings are
        /// inherently local.
        /// </summary>
        public void ApplyBroadcast(in CharacterSelectBroadcastPayload payload)
        {
            Players[0].ApplyPayload(payload.Slot0);
            Players[1].ApplyPayload(payload.Slot1);
        }

        /// <summary>
        /// Produces a broadcast payload from both slots. Host calls this each
        /// tick after applying input edges and before publishing to lobby data.
        /// </summary>
        public CharacterSelectBroadcastPayload ToBroadcast()
        {
            return new CharacterSelectBroadcastPayload
            {
                Slot0 = Players[0].ToPayload(),
                Slot1 = Players[1].ToPayload(),
            };
        }
    }
}
