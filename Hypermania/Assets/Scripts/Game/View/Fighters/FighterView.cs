using System;
using Design.Animation;
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

        public virtual void Init(CharacterConfig characterConfig, int skinIndex)
        {
            if (skinIndex < 0 || skinIndex >= characterConfig.Skins.Length)
            {
                throw new InvalidOperationException("Skin index out of range");
            }
            _animator = GetComponent<Animator>();
            _spriteLibrary = GetComponent<SpriteLibrary>();
            _animator.speed = 0f;

            _characterConfig = characterConfig;
            _oldController = _animator.runtimeAnimatorController;
            _animator.runtimeAnimatorController = characterConfig.AnimationController;
            _spriteLibrary.spriteLibraryAsset = characterConfig.Skins[skinIndex].SpriteLibrary;
        }

        public virtual void Render(Frame frame, in FighterState state)
        {
            Vector3 pos = transform.position;
            pos.x = (float)state.Position.x;
            pos.y = (float)state.Position.y;

            transform.position = pos;
            transform.localScale = new Vector3(state.FacingDir == FighterFacing.Left ? -1 : 1, 1f, 1f);

            CharacterState animState = state.State;
            HitboxData data = _characterConfig.GetHitboxData(animState);
            if (data == null)
                return;
            float normalizedTime = data.GetAnimNormalizedTime(frame - state.StateStart);
            _animator.Play(animState.ToString(), 0, normalizedTime);
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
                    sfxManager.AddDesired(sfxKind, realFrame);
                }
            }
            if (state.BlockedLastRealFrame)
            {
                vfxManager.AddDesired(
                    VfxKind.Block,
                    realFrame,
                    position: (Vector2)state.HitLocation.Value,
                    direction: (Vector2)state.HitProps.Value.Knockback
                );
                sfxManager.AddDesired(SfxKind.Block, realFrame);
            }
            if (state.HitLastRealFrame)
            {
                vfxManager.AddDesired(VfxKind.SmallHit, realFrame, position: (Vector2)state.HitLocation);
            }
            if (state.DashedLastRealFrame)
            {
                Vector2 dir = (Vector2)(
                    state.State == CharacterState.ForwardDash ? state.ForwardVector : state.BackwardVector
                );

                vfxManager.AddDesired(
                    VfxKind.DashDust,
                    realFrame,
                    position: (Vector2)state.Position + dir * _dustEmitterLocation.localPosition.x,
                    direction: dir
                );
            }
        }

        public void DeInit()
        {
            _animator.runtimeAnimatorController = _oldController;
            _oldController = null;
            _characterConfig = null;
        }

        /// <summary>
        /// Swaps the sprite library without re-binding the animator
        /// controller. Safe to call every frame when cycling skins.
        /// </summary>
        public void SetSkin(int skinIndex)
        {
            if (_characterConfig == null)
                return;
            if (skinIndex < 0 || skinIndex >= _characterConfig.Skins.Length)
                throw new ArgumentOutOfRangeException(nameof(skinIndex));
            _spriteLibrary.spriteLibraryAsset = _characterConfig.Skins[skinIndex].SpriteLibrary;
        }
    }
}
