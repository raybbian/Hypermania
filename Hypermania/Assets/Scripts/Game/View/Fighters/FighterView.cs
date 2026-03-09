using Design.Configs;
using Game.Sim;
using Game.View.Events;
using Game.View.Events.Vfx;
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

        [SerializeField]
        private Transform _dustEmitterLocation;

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

            _animator.Play(animation.ToString(), 0, (float)ticks / (totalTicks - 1));
            _animator.Update(0f); // force pose evaluation this frame while paused
        }

        public virtual void RollbackRender(
            Frame frame,
            in FighterState state,
            VfxManager vfxManager,
            SfxManager sfxManager
        )
        {
            if (
                state.State == CharacterState.BlockCrouch
                || state.State == CharacterState.BlockStand && frame == state.StateStart
            )
            {
                vfxManager.AddDesired(
                    new ViewEvent<VfxEvent>
                    {
                        Event = new VfxEvent
                        {
                            Kind = VfxKind.Block,
                            Direction = (Vector2)state.HitProps.Knockback,
                            Position = (Vector2)state.HitLocation,
                        },
                        StartFrame = frame,
                        Hash = 0, // can't be more than one block per character on a frame?
                    }
                );
            }
            if (
                (state.State == CharacterState.BackDash || state.State == CharacterState.ForwardDash)
                && frame == state.StateStart
            )
            {
                Vector2 dir = (Vector2)(
                    state.State == CharacterState.ForwardDash ? state.ForwardVector : state.BackwardVector
                );

                vfxManager.AddDesired(
                    new ViewEvent<VfxEvent>
                    {
                        Event = new VfxEvent
                        {
                            Kind = VfxKind.DashDust,
                            Direction = dir,
                            Position = (Vector2)state.Position + dir * _dustEmitterLocation.localPosition.x,
                        },
                        StartFrame = frame,
                        Hash = 0,
                    }
                );
            }
        }

        public void DeInit()
        {
            _animator.runtimeAnimatorController = _oldController;
            _oldController = null;
            _characterConfig = null;
        }
    }
}
