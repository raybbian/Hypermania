using System.Collections.Generic;
using Design.Animation;
using Game.Sim;
using Game.View.Fighters;
using UnityEngine;

namespace Game.View.Overlay
{
    /// <summary>
    /// Runtime debug visualization for hitboxes and grabboxes. Toggled by
    /// <see cref="InfoOptions.ShowBoxes"/>. Hurtboxes are intentionally not drawn.
    /// </summary>
    public class BoxVisualizer : MonoBehaviour
    {
        private static readonly Color HitboxColor = Color.red;
        private static readonly Color GrabboxColor = new Color(0.6f, 0f, 1f);

        [SerializeField]
        private Material _lineMaterial;

        [SerializeField]
        [Tooltip("Border thickness in screen pixels.")]
        private float _borderPixels = 3f;

        private readonly List<LineRenderer> _pool = new List<LineRenderer>();
        private Camera _cam;

        public void Render(in GameState state, GameOptions options, FighterView[] fighters)
        {
            if (_cam == null)
                _cam = Camera.main;

            float widthWorld = PixelsToWorld(_borderPixels);
            float parentZ = transform.position.z;

            int active = 0;
            for (int i = 0; i < fighters.Length; i++)
            {
                var fighterView = fighters[i];
                if (fighterView == null)
                    continue;

                CharacterState anim = state.Fighters[i].State;
                int tick = state.SimFrame - state.Fighters[i].StateStart;
                FrameData frame = options.Players[i].Character.GetFrameData(anim, tick);

                Transform t = fighterView.transform;

                foreach (var box in frame.Boxes)
                {
                    HitboxKind kind = box.Props.Kind;
                    Color color;
                    if (kind == HitboxKind.Hitbox)
                        color = HitboxColor;
                    else if (kind == HitboxKind.Grabbox)
                        color = GrabboxColor;
                    else
                        continue;

                    // FighterView sets transform.localScale.x = -1 when facing left, so
                    // TransformPoint/TransformVector already mirror the x axis. Do NOT
                    // pre-flip centerLocal.x here or it cancels out.
                    Vector2 centerLocal = (Vector2)box.CenterLocal;
                    Vector2 sizeLocal = (Vector2)box.SizeLocal;

                    Vector3 centerWorld = t.TransformPoint(new Vector3(centerLocal.x, centerLocal.y, 0f));
                    Vector3 halfWorldX = t.TransformVector(new Vector3(sizeLocal.x * 0.5f, 0f, 0f));
                    Vector3 halfWorldY = t.TransformVector(new Vector3(0f, sizeLocal.y * 0.5f, 0f));

                    Vector3 p0 = centerWorld - halfWorldX - halfWorldY;
                    Vector3 p1 = centerWorld + halfWorldX - halfWorldY;
                    Vector3 p2 = centerWorld + halfWorldX + halfWorldY;
                    Vector3 p3 = centerWorld - halfWorldX + halfWorldY;
                    // Flatten to the visualizer's z so ordering is controlled by the
                    // BoxVisualizer parent, not the fighter transform's z.
                    p0.z = parentZ;
                    p1.z = parentZ;
                    p2.z = parentZ;
                    p3.z = parentZ;

                    LineRenderer lr = GetOrCreate(active++);
                    lr.enabled = true;
                    lr.startColor = color;
                    lr.endColor = color;
                    lr.startWidth = widthWorld;
                    lr.endWidth = widthWorld;
                    lr.SetPosition(0, p0);
                    lr.SetPosition(1, p1);
                    lr.SetPosition(2, p2);
                    lr.SetPosition(3, p3);
                }
            }

            for (int i = active; i < _pool.Count; i++)
            {
                _pool[i].enabled = false;
            }
        }

        private float PixelsToWorld(float pixels)
        {
            if (_cam == null || Screen.height <= 0)
                return 0.02f;
            return (_cam.orthographicSize * 2f) / Screen.height * pixels;
        }

        private LineRenderer GetOrCreate(int index)
        {
            while (_pool.Count <= index)
            {
                var go = new GameObject("BoxLine");
                go.transform.SetParent(transform, false);
                var lr = go.AddComponent<LineRenderer>();
                lr.useWorldSpace = true;
                lr.loop = true;
                lr.positionCount = 4;
                lr.alignment = LineAlignment.View;
                lr.textureMode = LineTextureMode.Stretch;
                lr.numCapVertices = 0;
                lr.numCornerVertices = 0;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows = false;
                if (_lineMaterial != null)
                    lr.sharedMaterial = _lineMaterial;
                _pool.Add(lr);
            }
            return _pool[index];
        }
    }
}
