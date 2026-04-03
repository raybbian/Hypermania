using Design.Configs;
using Game.Sim;
using UnityEngine;
using UnityEngine.U2D.Animation;
using Utils;

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

        public virtual void Init(CharacterConfig characterConfig, int skinIndex)
        {
            _spriteLibrary.spriteLibraryAsset = characterConfig.Skins[skinIndex];
        }

        public abstract void Render(Frame simFrame, in ProjectileState state);

        public virtual void DeInit() { }
    }
}
