using System;
using System.Collections.Generic;
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

        [SerializeField]
        private Transform _visualCenter;

        [SerializeField]
        private float _hitJitterMagnitude = 0.04f;

        [SerializeField]
        private float _thinHitKnockbackMagnitude = 0.04f;

        private int _jitterFramesRemaining;

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

            if (state.HitProps.HasValue && IsHitRecipient(state.State))
            {
                _jitterFramesRemaining = state.HitProps.Value.HitstopTicks;
            }

            if (_jitterFramesRemaining > 0)
            {
                Vector2 jitter = UnityEngine.Random.insideUnitCircle * _hitJitterMagnitude;
                pos.x += jitter.x;
                pos.y += jitter.y;
                _jitterFramesRemaining--;
            }

            transform.position = pos;
            transform.localScale = new Vector3(state.FacingDir == FighterFacing.Left ? -1 : 1, 1f, 1f);

            CharacterState animState = state.State;
            HitboxData data = _characterConfig.GetHitboxData(animState);
            if (data == null)
                return;
            float normalizedTime = data.GetAnimNormalizedTime(frame - state.StateStart);
            _animator.Play(animState.ToString(), 0, normalizedTime);
            _animator.Update(0f); // force pose evaluation this frame while paused

            if (data.ApplyRootMotion)
            {
                int rmTick = frame - state.StateStart;
                FrameData fd = _characterConfig.GetFrameData(animState, rmTick);
                float facingSign = state.FacingDir == FighterFacing.Left ? -1f : 1f;
                Vector3 animWorld = new Vector3(
                    (float)fd.RootMotionOffset.x * facingSign,
                    (float)fd.RootMotionOffset.y,
                    0f
                );
                Vector3 desired = pos;
                desired.z = transform.position.z;
                transform.position = desired - animWorld;
            }
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
                List<SfxKind> sfxKinds = _characterConfig?.MoveSfx?.Sfx[state.State].Kinds;
                if (sfxKinds != null)
                {
                    foreach (SfxKind sfxKind in _characterConfig.MoveSfx.Sfx[state.State].Kinds)
                    {
                        sfxManager.AddDesired(sfxKind, realFrame);
                    }
                }
            }

            if (state.BlockedLastRealFrame)
            {
                Vector2 center = _visualCenter.position;
                Vector2 hit = (Vector2)state.HitLocation.Value;
                vfxManager.AddDesired(VfxKind.Block, realFrame, position: center, direction: center - hit);
                sfxManager.AddDesired(SfxKind.Block, realFrame);
            }

            if (state.HitLastRealFrame)
            {
                VfxKind kind =
                    (float)state.HitProps.Value.Knockback.magnitude < _thinHitKnockbackMagnitude
                        ? VfxKind.SmallHit
                        : VfxKind.ThinHit;
                vfxManager.AddDesired(
                    kind,
                    realFrame,
                    position: (Vector2)state.HitLocation,
                    direction: (Vector2)state.HitProps.Value.Knockback
                );
            }

            if (state.ClankLocation.HasValue)
            {
                vfxManager.AddDesired(VfxKind.Clank, realFrame, position: (Vector2)state.ClankLocation.Value);
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

        private static bool IsHitRecipient(CharacterState s) =>
            s == CharacterState.Hit
            || s == CharacterState.Knockdown
            || s == CharacterState.HeavyKnockdown
            || s == CharacterState.Death;

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
