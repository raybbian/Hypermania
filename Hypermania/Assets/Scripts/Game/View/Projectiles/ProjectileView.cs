using Game.View.Configs;
using UnityEngine;
using UnityEngine.U2D.Animation;
using Utils;
using Hypermania.Game;
using Hypermania.Game.Configs;
using Hypermania.Shared;

namespace Game.View.Projectiles
{
    [RequireComponent(typeof(Animator))]
    [RequireComponent(typeof(SpriteLibrary))]
    public abstract class ProjectileView : EntityView
    {
        protected Animator _animator;
        private SpriteLibrary _spriteLibrary;

        public virtual void Awake()
        {
            _animator = GetComponent<Animator>();
            _spriteLibrary = GetComponent<SpriteLibrary>();
            _animator.speed = 0f;
        }

        public virtual void Init(CharacterPresentation presentation, int skinIndex)
        {
            _spriteLibrary.spriteLibraryAsset = presentation.Skins[skinIndex].SpriteLibrary;
        }

        public abstract void Render(Frame simFrame, in ProjectileState state, ProjectileStats stats);

        public virtual void DeInit() { }
    }
}
