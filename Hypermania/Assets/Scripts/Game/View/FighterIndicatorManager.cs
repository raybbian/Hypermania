using System.Collections.Generic;
using Game.Sim;
using Game.View.Fighters;
using UnityEngine;

namespace Game.View
{
    public class FighterIndicatorManager : MonoBehaviour
    {
        [SerializeField]
        private GameObject TrackerPrefab;

        private Dictionary<int, Vector2> NotVisibleFighters = new Dictionary<int, Vector2>();

        private Dictionary<int, GameObject> Trackers = new Dictionary<int, GameObject>();

        [SerializeField]
        private Camera Camera;

        public void Track(FighterState[] _fighters)
        {
            NotVisibleFighters.Clear();

            Vector2 camPos = Camera.transform.position;
            Vector2 halfBounds = new Vector2(Camera.orthographicSize * Camera.aspect, Camera.orthographicSize);
            Vector2 min = camPos - halfBounds;
            Vector2 max = camPos + halfBounds;

            for (int i = 0; i < _fighters.Length; i++)
            {
                Vector2 p = (Vector2)_fighters[i].Position;

                bool outside = p.x < min.x || p.x > max.x || p.y < min.y || p.y > max.y;

                if (outside)
                {
                    NotVisibleFighters.Add(i, p);
                }
            }

            //Creating/updating trackers for nonvisible fighters
            foreach (KeyValuePair<int, Vector2> f in NotVisibleFighters)
            {
                Vector3 fighterPos = f.Value;

                // Convert to viewport coordinates
                Vector3 vp = Camera.WorldToViewportPoint(fighterPos);

                //If fighter is behind the camera, flip the viewport point
                if (vp.z < 0)
                    vp *= -1;

                //Clamp viewport to screen edges
                vp.x = Mathf.Clamp(vp.x, 0.05f, 0.95f);
                vp.y = Mathf.Clamp(vp.y, 0.05f, 0.95f);
                vp.z = Mathf.Abs(vp.z);

                //Convert back to world position for tracker
                Vector3 newPos = Camera.ViewportToWorldPoint(vp);

                //Rotate tracker toward fighter
                Vector3 toFighter = fighterPos - newPos;
                float angle = Mathf.Atan2(toFighter.y, toFighter.x) * Mathf.Rad2Deg;
                Quaternion rot = Quaternion.Euler(0f, 0f, angle - 90f);

                //Apply position and rotation
                GameObject t;
                if (!Trackers.ContainsKey(f.Key))
                {
                    t = Instantiate(TrackerPrefab, newPos, rot);
                    t.transform.SetParent(transform, true);
                    Trackers.Add(f.Key, t);
                }
                else
                {
                    t = Trackers[f.Key];
                    t.transform.position = newPos;
                    t.transform.rotation = rot;
                }
            }

            //Clearing out trackers for visible fighters
            List<int> toRemove = new List<int>();

            foreach (var kvp in Trackers)
            {
                if (!NotVisibleFighters.ContainsKey(kvp.Key))
                    toRemove.Add(kvp.Key);
            }

            foreach (int key in toRemove)
            {
                Destroy(Trackers[key]);
                Trackers.Remove(key);
            }
        }
    }
}
