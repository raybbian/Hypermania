using UnityEngine;
using UnityEngine.UI;

namespace Game.View.Overlay
{
    // One slot in a player's row of round wins. Shows the winner's portrait
    // in a ring once the round is won, and an empty ring until then.
    public class VictoryMark : MonoBehaviour
    {
        [SerializeField]
        private GameObject _empty;

        // Drawn with the VictoryMark shader, which takes its ring colour
        // from the graphic's colour.
        [SerializeField]
        private RawImage _portrait;

        [SerializeField]
        private Color _perfectOutline = new Color(1f, 0.78f, 0.2f);

        private Color _outline;

        // The outline is what a normal win gets. Perfect wins swap it for gold.
        public void Init(Texture portrait, Color outline)
        {
            _portrait.texture = portrait;
            _outline = outline;
        }

        public void Show(VictoryKind kind)
        {
            bool won = kind != VictoryKind.Empty;
            _empty.SetActive(!won);
            _portrait.gameObject.SetActive(won);
            _portrait.color = kind == VictoryKind.Perfect ? _perfectOutline : _outline;
        }
    }
}
