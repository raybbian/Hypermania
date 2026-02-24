using Design;
using Game.Sim;
using UnityEngine;
using Utils;

namespace Game.View.Fighters
{
    [RequireComponent(typeof(Animator))]
    public class FighterView : MonoBehaviour
    {
        private Animator _animator;
        private CharacterConfig _characterConfig;
        private RuntimeAnimatorController _oldController;

        public virtual void Awake()
        {
            _animator = GetComponent<Animator>();
            _animator.speed = 0f;
        }

        public virtual void Init(CharacterConfig characterConfig)
        {
            _characterConfig = characterConfig;
            _oldController = _animator.runtimeAnimatorController;
            _animator.runtimeAnimatorController = characterConfig.AnimationController;
        }

        public virtual void Render(Frame frame, in FighterState state)
        {
            Vector3 pos = transform.position;
            pos.x = (float)state.Position.x;
            pos.y = (float)state.Position.y;

            transform.position = pos;
            transform.localScale = new Vector3(state.FacingDir == FighterFacing.Left ? -1 : 1, 1f, 1f);

            CharacterState animation = state.State;
            int totalTicks = _characterConfig.GetHitboxData(animation).TotalTicks;

            int ticks = frame - state.StateStart;
            if (_characterConfig.AnimLoops(animation))
            {
                ticks %= totalTicks;
            }
            else
            {
                ticks = Mathf.Min(ticks, totalTicks - 1);
            }

            _animator.Play(animation.ToString(), 0, (float)ticks / totalTicks);
            _animator.Update(0f); // force pose evaluation this frame while paused
        }

        public void DeInit()
        {
            _animator.runtimeAnimatorController = _oldController;
            _oldController = null;
            _characterConfig = null;
        }
    }
}
