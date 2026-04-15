using Design.Configs;
using Game.View.Fighters;
using UnityEngine;

namespace Scenes.Menus.CharacterSelect
{
    /// <summary>
    /// Shared fighter stage for CharacterSelect. Driven each frame by
    /// <see cref="Render"/>: the panel describes the desired state (which
    /// config, which skin, visible or not) and this stage converges. No
    /// coroutines, no cross-frame command state — the per-slot fighter,
    /// config, and slide progress are always updated together inside a
    /// single <see cref="Render"/> call, so nothing can desync with an
    /// outside caller's view of state.
    /// </summary>
    public class CharacterFighterStage : MonoBehaviour
    {
        [Header("Stage")]
        [SerializeField]
        private Transform _stageRoot;

        [Header("Per-slot placement (indexed by player slot 0 / 1)")]
        [SerializeField]
        [Tooltip("World rest position of each slot. Fighter at this transform is fully on screen.")]
        private Transform[] _slotRest = new Transform[2];

        [SerializeField]
        [Tooltip(
            "World-space delta added to the rest position to reach the offscreen staging point. Typically pushes slot 0 left and slot 1 right so each enters from its own edge."
        )]
        private Vector3[] _slotSlideOffset = new Vector3[2];

        [SerializeField]
        private float _slideDuration = 0.3f;

        private readonly FighterView[] _fighters = new FighterView[2];
        private readonly CharacterConfig[] _currentConfig = new CharacterConfig[2];
        private readonly float[] _progress = { 0f, 0f };

        /// <summary>
        /// Reconcile slot <paramref name="slot"/> toward the described
        /// state. Safe to call every frame with the same args; safe to
        /// flip <paramref name="visible"/> on any frame. Teardown and
        /// respawn happen in this method — never async.
        /// </summary>
        public void Render(int slot, CharacterConfig config, int skinIndex, bool visible)
        {
            if (!IsValidSlot(slot) || _slotRest[slot] == null)
                return;

            bool wantFighter = visible && config != null;

            // Not visible → despawn immediately. No slide-out.
            if (!wantFighter)
            {
                if (_fighters[slot] != null)
                    Teardown(slot);
                return;
            }

            // Character changed while something's spawned — drop the old
            // fighter and spawn the new one from offscreen.
            if (_fighters[slot] != null && _currentConfig[slot] != config)
                Teardown(slot);

            if (_fighters[slot] == null)
                Spawn(slot, config, skinIndex);

            _progress[slot] = Mathf.MoveTowards(
                _progress[slot],
                1f,
                _slideDuration > 0f ? Time.deltaTime / _slideDuration : 1f
            );

            Vector3 rest = _slotRest[slot].position;
            Vector3 offscreen = rest + _slotSlideOffset[slot];
            float eased = Mathf.SmoothStep(0f, 1f, _progress[slot]);
            _fighters[slot].transform.position = Vector3.Lerp(offscreen, rest, eased);

            // Keep skin in sync. FighterView.SetSkin is just a sprite-
            // library asset swap, so re-assigning the same value each
            // frame is a no-op cost-wise.
            _fighters[slot].SetSkin(skinIndex);
        }

        private void Spawn(int slot, CharacterConfig config, int skinIndex)
        {
            FighterView fighter = Instantiate(config.Prefab, _stageRoot);
            fighter.Init(config, skinIndex);

            Animator anim = fighter.GetComponent<Animator>();
            if (anim != null)
            {
                anim.speed = 1f;
                anim.Play("Idle");
            }

            // Slot 0 faces right, slot 1 faces left, so the two fighters
            // square up across the middle of the stage.
            fighter.transform.localScale = new Vector3(slot == 0 ? 1f : -1f, 1f, 1f);

            Vector3 rest = _slotRest[slot].position;
            Vector3 offscreen = rest + _slotSlideOffset[slot];
            fighter.transform.position = offscreen;

            _fighters[slot] = fighter;
            _currentConfig[slot] = config;
            _progress[slot] = 0f;
        }

        private void Teardown(int slot)
        {
            if (_fighters[slot] != null)
            {
                Destroy(_fighters[slot].gameObject);
                _fighters[slot] = null;
            }
            _currentConfig[slot] = null;
            _progress[slot] = 0f;
        }

        private static bool IsValidSlot(int slot) => slot >= 0 && slot < 2;
    }
}
