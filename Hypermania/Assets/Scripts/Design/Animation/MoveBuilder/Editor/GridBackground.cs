using UnityEditor;
using UnityEngine;

namespace Design.Animation.MoveBuilder.Editors
{
    public static class GridBackground
    {
        public static void DrawGridWithLabels(Rect rect, Camera cam)
        {
            Handles.BeginGUI();

            float ppp = EditorGUIUtility.pixelsPerPoint;
            float pxPerWorld = (rect.height * ppp) / (cam.orthographicSize * 2f);
            if (pxPerWorld <= 0.0001f)
            {
                Handles.EndGUI();
                return;
            }

            // Minor lines target spacing in pixels, major is a multiple.
            float targetMinorPx = 18f;
            float minorStep = NiceStep(targetMinorPx / pxPerWorld); // world units
            float majorStep = minorStep * 5f;

            GetVisibleWorldBounds(rect, cam, out float xMin, out float xMax, out float yMin, out float yMax);

            // Colors (subtle)
            Color minorCol = new Color(1f, 1f, 1f, 0.06f);
            Color majorCol = new Color(1f, 1f, 1f, 0.12f);
            Color axisCol = new Color(1f, 1f, 1f, 0.20f);

            // Draw minor + major
            DrawGridLinesAndLabels(rect, cam, xMin, xMax, yMin, yMax, minorStep, majorStep, minorCol, majorCol);

            // Axes at 0
            DrawAxisLines(rect, cam, xMin, xMax, yMin, yMax, axisCol);

            Handles.EndGUI();
        }

        private static void DrawGridLinesAndLabels(
            Rect rect,
            Camera cam,
            float xMin,
            float xMax,
            float yMin,
            float yMax,
            float minorStep,
            float majorStep,
            Color minorCol,
            Color majorCol
        )
        {
            // Vertical lines
            {
                int i0 = Mathf.FloorToInt(xMin / minorStep);
                int i1 = Mathf.CeilToInt(xMax / minorStep);

                for (int i = i0; i <= i1; i++)
                {
                    float x = i * minorStep;
                    bool isMajor = IsMultipleOf(x, majorStep);

                    Handles.color = isMajor ? majorCol : minorCol;

                    Vector2 g0 = Utils.WorldToGui(rect, cam, new Vector3(x, yMin, 0f));
                    Vector2 g1 = Utils.WorldToGui(rect, cam, new Vector3(x, yMax, 0f));
                    Handles.DrawLine(g0, g1);

                    if (isMajor)
                    {
                        // Unit label at bottom
                        DrawUnitLabel(new Vector2(g0.x + 2f, rect.yMax - 16f), $"{x:0.##}");
                    }
                }
            }

            // Horizontal lines
            {
                int i0 = Mathf.FloorToInt(yMin / minorStep);
                int i1 = Mathf.CeilToInt(yMax / minorStep);

                for (int i = i0; i <= i1; i++)
                {
                    float y = i * minorStep;
                    bool isMajor = IsMultipleOf(y, majorStep);

                    Handles.color = isMajor ? majorCol : minorCol;

                    Vector2 g0 = Utils.WorldToGui(rect, cam, new Vector3(xMin, y, 0f));
                    Vector2 g1 = Utils.WorldToGui(rect, cam, new Vector3(xMax, y, 0f));
                    Handles.DrawLine(g0, g1);

                    if (isMajor)
                    {
                        // Unit label at left
                        DrawUnitLabel(new Vector2(rect.xMin + 2f, g0.y - 8f), $"{y:0.##}");
                    }
                }
            }
        }

        private static void DrawAxisLines(
            Rect rect,
            Camera cam,
            float xMin,
            float xMax,
            float yMin,
            float yMax,
            Color axisCol
        )
        {
            Handles.color = axisCol;

            if (0f >= xMin && 0f <= xMax)
            {
                Vector2 g0 = Utils.WorldToGui(rect, cam, new Vector3(0f, yMin, 0f));
                Vector2 g1 = Utils.WorldToGui(rect, cam, new Vector3(0f, yMax, 0f));
                Handles.DrawLine(g0, g1);
            }

            if (0f >= yMin && 0f <= yMax)
            {
                Vector2 g0 = Utils.WorldToGui(rect, cam, new Vector3(xMin, 0f, 0f));
                Vector2 g1 = Utils.WorldToGui(rect, cam, new Vector3(xMax, 0f, 0f));
                Handles.DrawLine(g0, g1);
            }
        }

        private static void DrawUnitLabel(Vector2 pos, string text)
        {
            var prev = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 0.55f);
            GUI.Label(new Rect(pos.x, pos.y, 80f, 16f), text, EditorStyles.miniLabel);
            GUI.color = prev;
        }

        private static void GetVisibleWorldBounds(
            Rect rect,
            Camera cam,
            out float xMin,
            out float xMax,
            out float yMin,
            out float yMax
        )
        {
            float heightWorld = cam.orthographicSize * 2f;
            float aspect = rect.width / Mathf.Max(1f, rect.height);
            float widthWorld = heightWorld * aspect;

            Vector3 c = cam.transform.position;

            xMin = c.x - widthWorld * 0.5f;
            xMax = c.x + widthWorld * 0.5f;
            yMin = c.y - heightWorld * 0.5f;
            yMax = c.y + heightWorld * 0.5f;
        }

        private static float NiceStep(float raw)
        {
            if (raw <= 0f)
                return 1f;

            float exp = Mathf.Floor(Mathf.Log10(raw));
            float base10 = Mathf.Pow(10f, exp);
            float f = raw / base10;

            float nice =
                f < 1.5f ? 1f
                : f < 3.5f ? 2f
                : f < 7.5f ? 5f
                : 10f;

            return nice * base10;
        }

        private static bool IsMultipleOf(float v, float step)
        {
            if (step <= 0f)
                return false;

            float k = v / step;
            return Mathf.Abs(k - Mathf.Round(k)) < 1e-4f;
        }
    }
}
