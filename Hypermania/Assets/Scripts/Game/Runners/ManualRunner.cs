using System.Security.Cryptography;
using UnityEngine;

namespace Game.Runners
{
    public class ManualRunner : SingleplayerRunner
    {
        public override void Poll(float deltaTime)
        {
            if (!_initialized)
            {
                return;
            }
            if (Input.GetKeyDown(KeyCode.RightArrow))
            {
                GameLoop();
            }
        }
    }
}
