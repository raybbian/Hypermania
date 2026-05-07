using System;
using System.Collections.Generic;
using Game;
using Game.View.Events;
using Game.View.Fighters;
using UnityEngine;
using UnityEngine.U2D.Animation;
using Utils.EnumArray;
using Hypermania.Shared;
using Hypermania.Game;
using Hypermania.Game.Configs;
using Hypermania.Shared.SoftFloat;

namespace Game.View.Configs
{
    [Serializable]
    public struct ManiaArrowSpritePair
    {
        public Sprite Active;
        public Sprite Inactive;
    }

    [Serializable]
    public struct SkinConfig
    {
        public Color MainColor;
        public Color LightColor;
        public Color AccentColor;
        public Color[] HypeBarColors;
        public SpriteLibraryAsset SpriteLibrary;
        public Texture2D Portrait;
        public Texture2D Splash;
        public ManiaArrowSpritePair ManiaInnerArrow;
        public ManiaArrowSpritePair ManiaOuterArrow;
    }

    [Serializable]
    public struct SuperDisplayConfig
    {
        public CharacterState AnimState;
        public int StartFrame;
        public Vector3 CameraLocalPosition;
        public sfloat CameraOrthoSize;
    }

    // View-side counterpart of CharacterStats. Holds the prefab, skin set,
    // animation override, sfx mapping, and super-display camera layout. Pulled
    // by the runner into PresentationOptions; sim never sees these fields.
    [CreateAssetMenu(menuName = "Hypermania/View/Character Presentation")]
    public class CharacterPresentation : ScriptableObject
    {
        public Character Character;
        public CharacterStats Stats;
        public EnumArray<CharacterState, HitboxData> Hitboxes;
        public FighterView Prefab;
        public FighterMoveSfx MoveSfx;
        public AnimatorOverrideController AnimationController;
        public SkinConfig[] Skins;
        public SuperDisplayConfig SuperDisplay;

        // Per-projectile view assets, parallel to Stats.Projectiles. Sim
        // resolves projectile data via Stats; the runner walks this list to
        // spawn the correct ProjectileView prefab per projectile config.
        public List<ProjectilePresentation> Projectiles;
    }
}
