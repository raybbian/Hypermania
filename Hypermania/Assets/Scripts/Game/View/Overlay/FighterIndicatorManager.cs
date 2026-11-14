using Design.Configs;
using Game.Sim;
using UnityEngine;

namespace Game.View.Overlay
{
    // Shows a pin at the edge of the screen for each fighter the camera
    // can't see, pointing to where they are.
    public class FighterIndicatorManager : MonoBehaviour
    {
        [SerializeField]
        private GameObject TrackerPrefab;

        [SerializeField]
        private Camera Camera;

        // How far in from the screen edge a pin's head sits, in world units.
        // Has to be longer than the pin's tail or its tip leaves the screen.
        [SerializeField]
        private float _edgeInset = 0.5f;

        private GameOptions _options;
        private FighterIndicator[] _indicators;

        public void Init(GameOptions options)
        {
            _options = options;
            if (_indicators != null)
            {
                foreach (FighterIndicator indicator in _indicators)
                {
                    if (indicator != null)
                        Destroy(indicator.gameObject);
                }
            }
            _indicators = new FighterIndicator[options.Players.Length];
        }

        public void Track(FighterState[] fighters)
        {
            Vector2 camPos = Camera.transform.position;
            Vector2 halfBounds = new Vector2(Camera.orthographicSize * Camera.aspect, Camera.orthographicSize);
            Vector2 min = camPos - halfBounds;
            Vector2 max = camPos + halfBounds;
            Vector2 inset = new Vector2(_edgeInset, _edgeInset);

            for (int i = 0; i < fighters.Length; i++)
            {
                Vector2 p = (Vector2)fighters[i].Position;
                bool outside = p.x < min.x || p.x > max.x || p.y < min.y || p.y > max.y;

                if (!outside)
                {
                    // It keeps its last spot while it shrinks away.
                    if (_indicators[i] != null)
                        _indicators[i].SetVisible(false);
                    continue;
                }

                if (_indicators[i] == null)
                    _indicators[i] = CreateIndicator(i);

                Vector2 head = Vector2.Max(min + inset, Vector2.Min(max - inset, p));
                _indicators[i].SetVisible(true);
                _indicators[i].PointAt(head, p - head);
            }
        }

        private FighterIndicator CreateIndicator(int player)
        {
            SkinConfig skin = _options.Players[player].Character.Skins[_options.Players[player].SkinIndex];
            FighterIndicator indicator = Instantiate(TrackerPrefab, transform).GetComponent<FighterIndicator>();
            indicator.Init(Camera, skin.Portrait, skin.MainColor, skin.AccentColor);
            return indicator;
        }
    }
}
