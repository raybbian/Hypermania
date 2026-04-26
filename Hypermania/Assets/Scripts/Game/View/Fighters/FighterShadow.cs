using UnityEngine;

namespace Game.View.Fighters
{
    public class FighterShadow : MonoBehaviour
    {
        [SerializeField]
        private Transform _shadow;

        public void Render()
        {
            if (_shadow == null)
                return;

            Vector3 pos = _shadow.position;
            pos.y = 0f;
            _shadow.position = pos;
        }
    }
}
