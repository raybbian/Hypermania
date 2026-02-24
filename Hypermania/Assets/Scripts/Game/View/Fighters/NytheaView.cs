using System;
using Game.Sim;
using UnityEngine;
using UnityEngine.U2D.Animation;
using Utils;

namespace Game.View.Fighters
{
    [RequireComponent(typeof(SpriteLibrary))]
    public class NytheaView : FighterView
    {
        public void OnValidate()
        {
            if (GetComponent<SpriteLibrary>() == null)
            {
                throw new InvalidOperationException("Nythea must have a sprite library because she uses sprite swaps");
            }
        }

        public override void Render(Frame frame, in FighterState state)
        {
            base.Render(frame, state);
        }
    }
}
