using UnityEngine;

namespace Game.View.Background
{
    // Lives on a stage's background prefab and tells the floor-aware shaders
    // (fighter shadows, dust tint) where that stage's floor is.
    public class StageFloor : MonoBehaviour
    {
        // Matches _StageFloor in VFX/Stage/StageFloor.hlsl.
        private static readonly int StageFloorId = Shader.PropertyToID("_StageFloor");

        // How far the floor reaches up the screen from the ground line before
        // it meets the back wall, in world units.
        [SerializeField]
        private float _depth;

        public void Init(float groundY)
        {
            Shader.SetGlobalVector(StageFloorId, new Vector4(groundY, groundY + _depth));
        }
    }
}
