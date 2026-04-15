using UnityEngine;

namespace Scenes.Menus.CharacterSelect
{
    /// <summary>
    /// One cursor per player (red/P1, blue/P2). Watches its own slot in
    /// <see cref="CharacterSelectState"/> and tweens itself to the hovered
    /// grid cell. Stays visible across all phases so the other player can
    /// always see which character their opponent has locked in.
    /// </summary>
    public class CharacterCursor : MonoBehaviour
    {
        [SerializeField]
        private RectTransform _rect;

        [SerializeField]
        private float _moveSpeed = 20f;

        private PlayerSelectionState _state;
        private CharacterGrid _grid;

        public void Bind(PlayerSelectionState state, CharacterGrid grid)
        {
            _state = state;
            _grid = grid;
        }

        private void Awake()
        {
            if (_rect == null)
                _rect = GetComponent<RectTransform>();
        }

        private void Update()
        {
            if (_state == null || _grid == null)
                return;

            // World-space lerp so the cursor can be parented anywhere in the
            // canvas hierarchy — it doesn't have to be a sibling of the icons.
            RectTransform slotRect = _grid.GetSlotRect(_state.CharacterIndex);
            if (slotRect != null)
            {
                Vector3 target = slotRect.position;
                _rect.position = Vector3.Lerp(_rect.position, target, _moveSpeed * Time.deltaTime);
            }
        }
    }
}
