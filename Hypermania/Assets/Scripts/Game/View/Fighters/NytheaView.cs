using System;
using UnityEngine;
using UnityEngine.U2D.Animation;
using Utils;
using Hypermania.Game;
using Hypermania.Shared;

namespace Game.View.Fighters
{
    [RequireComponent(typeof(SpriteLibrary))]
    public class NytheaView : FighterView
    {
        public override void Render(Frame frame, in FighterState state)
        {
            base.Render(frame, state);
        }
    }
}
