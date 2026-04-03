using System;
using Design.Configs;
using Game.Sim;
using Game.View.Events;
using Game.View.Events.Vfx;
using UnityEngine;
using UnityEngine.U2D.Animation;
using Utils;

namespace Game.View.Fighters
{
    [RequireComponent(typeof(Animator))]
    [RequireComponent(typeof(SpriteLibrary))]
    public class FighterView : EntityView
    {
        private Animator _animator;
        private SpriteLibrary _spriteLibrary;
        private CharacterConfig _characterConfig;
        private RuntimeAnimatorController _oldController;

        [SerializeField]
        private Transform _dustEmitterLocation;

        public virtual void Awake()
        {
            _animator = GetComponent<Animator>();
            _spriteLibrary = GetComponent<SpriteLibrary>();
            _animator.speed = 0f;
        }

        public virtual void Init(CharacterConfig characterConfig, int skinIndex)
        {
            if (skinIndex < 0 || skinIndex >= characterConfig.Skins.Length)
            {
                throw new InvalidOperationException("Skin index out of range");
            }

            _characterConfig = characterConfig;
            _oldController = _animator.runtimeAnimatorController;
            _animator.runtimeAnimatorController = characterConfig.AnimationController;
            _spriteLibrary.spriteLibraryAsset = characterConfig.Skins[skinIndex];
        }

        public virtual void Render(Frame frame, in FighterState state)
        {
            Vector3 pos = transform.position;
            pos.x = (float)state.Position.x;
            pos.y = (float)state.Position.y;

            transform.position = pos;
            transform.localScale = new Vector3(state.FacingDir == FighterFacing.Left ? -1 : 1, 1f, 1f);

            CharacterState animState = state.State;
            int totalTicks = _characterConfig.GetHitboxData(animState).TotalTicks;

            int ticks = frame - state.StateStart;
            if (_characterConfig.AnimLoops(animState))
            {
                ticks %= totalTicks;
            }
            else
            {
                ticks = Mathf.Min(ticks, totalTicks - 1);
            }

            _animator.Play(animState.ToString(), 0, (float)ticks / (totalTicks - 1));
            _animator.Update(0f); // force pose evaluation this frame while paused
        }

        public virtual void RollbackRender(
            Frame realFrame,
            in FighterState state,
            VfxManager vfxManager,
            SfxManager sfxManager
        )
        {
            if (state.StateChangedThisRealFrame)
            {
                foreach (SfxKind sfxKind in _characterConfig.MoveSfx.Sfx[state.State].Kinds)
                {
                    sfxManager.AddDesired(
                        new ViewEvent<SfxEvent>
                        {
                            Event = new SfxEvent { Kind = sfxKind },
                            StartFrame = realFrame,
                            Hash = 0,
                        }
                    );
                }
            }
            if (state.BlockedLastRealFrame)
            {
                vfxManager.AddDesired(
                    new ViewEvent<VfxEvent>
                    {
                        Event = new VfxEvent
                        {
                            Kind = VfxKind.Block,
                            Direction = (Vector2)state.HitProps.Value.Knockback,
                            Position = (Vector2)state.HitLocation.Value,
                        },
                        StartFrame = realFrame,
                        Hash = 0,
                    }
                );
                sfxManager.AddDesired(
                    new ViewEvent<SfxEvent>
                    {
                        Event = new SfxEvent { Kind = SfxKind.Block },
                        StartFrame = realFrame,
                        Hash = 0,
                    }
                );
            }
            if (state.HitLastRealFrame)
            {
                vfxManager.AddDesired(
                    new ViewEvent<VfxEvent>()
                    {
                        Event = new VfxEvent { Kind = VfxKind.SmallHit, Position = (Vector2)state.HitLocation },
                        StartFrame = realFrame,
                        Hash = 0,
                    }
                );
            }
            if (state.DashedLastRealFrame)
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
                        StartFrame = realFrame,
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
