using System;
using System.Collections.Generic;
using Game.Sim.Configs;
using Game.Sim;
using Game.View.Configs;
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
        private CharacterPresentation _presentation;
        private CharacterStats _stats;
        private RuntimeAnimatorController _oldController;

        [SerializeField]
        private Transform _dustEmitterLocation;

        [SerializeField]
        private Transform _visualCenter;

        public Vector3 VisualCenter => _visualCenter.position;

        [SerializeField]
        private float _hitJitterMagnitude = 0.04f;

        [SerializeField]
        private float _thinHitKnockbackMagnitude = 0.04f;

        [SerializeField]
        private FighterShadow _shadow;

        private int _jitterFramesRemaining;

        public virtual void Init(CharacterPresentation presentation, int skinIndex)
        {
            if (skinIndex < 0 || skinIndex >= presentation.Skins.Length)
            {
                throw new InvalidOperationException("Skin index out of range");
            }

            _animator = GetComponent<Animator>();
            _spriteLibrary = GetComponent<SpriteLibrary>();
            _animator.speed = 0f;

            _presentation = presentation;
            _stats = presentation.Stats;
            _oldController = _animator.runtimeAnimatorController;
            _animator.runtimeAnimatorController = presentation.AnimationController;
            _spriteLibrary.spriteLibraryAsset = presentation.Skins[skinIndex].SpriteLibrary;
        }

        public virtual void Render(Frame frame, in FighterState state)
        {
            Vector3 pos = transform.position;
            pos.x = (float)state.Position.x;
            pos.y = (float)state.Position.y;

            if (state.View.HitProps.HasValue && IsHitRecipient(state.State))
            {
                _jitterFramesRemaining = state.View.HitProps.Value.HitstopTicks;
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
            HitboxData data = _stats.GetHitboxData(animState);
            if (data == null)
                return;
            // add small amount to ensure that right frame is displayed
            int animTick = data.AnimLoops
                ? (frame - state.StateStart) % data.TotalTicks
                : Mathf.Min(frame - state.StateStart, data.TotalTicks - 1);
            float normalizedTime = (float)animTick / (data.TotalTicks - 1) + 0.01f;
            _animator.Play(animState.ToString(), 0, normalizedTime);
            _animator.Update(0f); // force pose evaluation this frame while paused

            if (data.ApplyRootMotion)
            {
                int rmTick = frame - state.StateStart;
                FrameData fd = _stats.GetFrameData(animState, rmTick);
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

            if (_shadow != null)
                _shadow.Render();
        }

        public virtual void RollbackRender(
            Frame realFrame,
            in FighterState state,
            VfxManager vfxManager,
            SfxManager sfxManager,
            GlobalStats globalStats
        )
        {
            if (state.View.StateChangedThisRealFrame)
            {
                List<SfxKind> sfxKinds = _presentation?.MoveSfx?.Sfx[state.State].Kinds;
                if (sfxKinds != null)
                {
                    foreach (SfxKind sfxKind in _presentation.MoveSfx.Sfx[state.State].Kinds)
                    {
                        sfxManager.AddDesired(sfxKind, realFrame);
                    }
                }
            }

            if (state.BlockedLastRealFrame)
            {
                Vector2 center = _visualCenter.position;
                Vector2 hit = (Vector2)state.View.HitLocation.Value;
                vfxManager.AddDesired(VfxKind.Block, realFrame, position: center, direction: center - hit);
                sfxManager.AddDesired(SfxKind.Block, realFrame);
            }

            if (state.HitLastRealFrame)
            {
                VfxKind kind =
                    (float)state.View.HitProps.Value.Knockback.magnitude < _thinHitKnockbackMagnitude
                        ? VfxKind.SmallHit
                        : VfxKind.ThinHit;
                vfxManager.AddDesired(
                    kind,
                    realFrame,
                    position: (Vector2)state.View.HitLocation,
                    direction: (Vector2)state.View.HitProps.Value.Knockback
                );
            }

            if (state.View.ClankLocation.HasValue)
            {
                vfxManager.AddDesired(VfxKind.Clank, realFrame, position: (Vector2)state.View.ClankLocation.Value);
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

            if (state.View.StateChangedThisRealFrame && state.State == CharacterState.Burst)
            {
                sfxManager.AddDesired(SfxKind.Burst, realFrame);
            }

            if (state.State == CharacterState.Burst && realFrame - state.StateStart == globalStats.BurstVfxTicks)
            {
                vfxManager.AddDesired(VfxKind.Burst, realFrame, position: _visualCenter.position);
            }
        }

        private static bool IsHitRecipient(CharacterState s) =>
            s == CharacterState.Hit
            || s == CharacterState.SoftKnockdown
            || s == CharacterState.HeavyKnockdown
            || s == CharacterState.Death;

        public void DeInit()
        {
            _animator.runtimeAnimatorController = _oldController;
            _oldController = null;
            _presentation = null;
            _stats = null;
        }

        public void SetSkin(int skinIndex)
        {
            if (_presentation == null)
                return;
            if (skinIndex < 0 || skinIndex >= _presentation.Skins.Length)
                throw new ArgumentOutOfRangeException(nameof(skinIndex));
            _spriteLibrary.spriteLibraryAsset = _presentation.Skins[skinIndex].SpriteLibrary;
        }
    }
}
