using System;
using Game.Sim;
using UnityEngine;
using UnityEngine.U2D.Animation;
using Utils;

namespace Game.View.Fighters
{
    [RequireComponent(typeof(SpriteLibrary))]
    public class ViaView : FighterView
    {
        public override void Render(Frame frame, in FighterState state)
        {
            base.Render(frame, state);
        }
    }
}
