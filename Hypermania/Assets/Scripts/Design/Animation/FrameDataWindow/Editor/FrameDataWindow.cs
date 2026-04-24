using System.Collections.Generic;
using System.Text;
using Design.Configs;
using Game;
using UnityEditor;
using UnityEngine;
using Utils.EnumArray;

namespace Design.Animation.FrameDataWindow.Editor
{
    public sealed class FrameDataWindow : EditorWindow
    {
        [SerializeField]
        private CharacterConfig _config;

        private Vector2 _scroll;

        private const float RowHeight = 20f;
        private const float HeaderHeight = 22f;
        private const float CellPadX = 4f;

        private static readonly Color RowBgOdd = new Color(1f, 1f, 1f, 0.04f);
        private static readonly Color HeaderBg = new Color(0f, 0f, 0f, 0.25f);
        private static readonly Color MoveHeaderBg = new Color(0.25f, 0.5f, 0.9f, 0.15f);
        private static readonly Color BorderColor = new Color(0f, 0f, 0f, 0.5f);
        private static readonly Color BlackedCell = new Color(0f, 0f, 0f, 0.6f);
        private static readonly Color ValidationWarnColor = new Color(1f, 0.35f, 0.35f, 1f);

        private static GUIStyle _centeredBoldStyle;
        private static GUIStyle CenteredBoldStyle
        {
            get
            {
                if (_centeredBoldStyle == null)
                {
                    _centeredBoldStyle = new GUIStyle(EditorStyles.boldLabel)
                    {
                        alignment = TextAnchor.MiddleCenter,
                    };
                }
                return _centeredBoldStyle;
            }
        }

        private static readonly string[] MoveHeaders =
        {
            "Valid",
            "State",
            "Attack",
            "Kind",
            "Frame",
            "Dmg",
            "Startup",
            "Active",
            "Recovery",
            "Hitstun",
            "Blockstun",
            "On Hit",
            "On Block",
            "KD",
            "Unbl",
            "Tech",
            "Gatlings",
        };

        // Last entry is flex — grows to fill remaining row width.
        private static readonly float[] MoveWidths =
        {
            45f, // Valid
            160f, // State
            55f, // Attack
            55f, // Kind
            50f, // Frame
            40f, // Dmg
            55f, // Startup
            50f, // Active
            60f, // Recovery
            55f, // Hitstun
            65f, // Blockstun
            55f, // On Hit
            65f, // On Block
            50f, // KD
            40f, // Unbl
            40f, // Tech
            260f, // Gatlings (minimum)
        };

        private static readonly string[] ProjectileHeaders =
        {
            "Trigger State",
            "Spawn",
            "Lifetime",
            "Attack",
            "Kind",
            "Frame",
            "Dmg",
            "Hitstun",
            "Blockstun",
            "KD",
            "Unbl",
        };

        private static readonly float[] ProjectileWidths =
        {
            180f,
            55f,
            65f,
            55f,
            55f,
            50f,
            40f,
            55f,
            65f,
            50f,
            40f,
        };

        [MenuItem("Hypermania/Frame Data Viewer")]
        public static void ShowWindow()
        {
            var window = GetWindow<FrameDataWindow>("Frame Data");
            window.minSize = new Vector2(1200f, 420f);
        }

        private void OnGUI()
        {
            EditorGUILayout.Space();
            _config = (CharacterConfig)EditorGUILayout.ObjectField(
                "Character Config",
                _config,
                typeof(CharacterConfig),
                false
            );

            if (_config == null)
            {
                EditorGUILayout.HelpBox("Assign a CharacterConfig asset to view its frame data.", MessageType.Info);
                return;
            }

            EditorGUILayout.Space();

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            DrawMovesTable(_config);
            EditorGUILayout.Space();
            EditorGUILayout.Space();
            DrawProjectilesTable(_config);

            EditorGUILayout.EndScrollView();
        }

        // ─────────── Moves ───────────

        private static void DrawMovesTable(CharacterConfig config)
        {
            EditorGUILayout.LabelField("Moves", EditorStyles.boldLabel);

            float[] widths = WithFlexLast(MoveWidths);
            DrawHeaderRow(MoveHeaders, widths);

            int rowIndex = 0;
            CharacterState[] keys = EnumIndexCache<CharacterState>.Keys;
            for (int i = 0; i < keys.Length; i++)
            {
                CharacterState state = keys[i];
                HitboxData data = config.Hitboxes != null ? config.Hitboxes[state] : null;
                if (data == null)
                    continue;

                bool isValid = TryComputeSAR(data, out int startup, out int active, out int recovery);
                List<BoxProps> uniqueBoxes = CollectUniqueHitboxes(data);
                if (!isValid && uniqueBoxes.Count == 0)
                    continue;

                int refIdx = uniqueBoxes.Count > 0 ? FindLatestUniqueBoxIndex(uniqueBoxes, data) : -1;
                int refFirstFrame = refIdx >= 0 ? FindFirstFrameOfLastContiguousRun(data, uniqueBoxes[refIdx]) : -1;
                string gatlings = GatlingsFor(config, state);
                string validationReason = ValidateRule(data);

                Color prevContent = GUI.contentColor;
                if (!isValid)
                    GUI.contentColor = new Color(1f, 1f, 1f, 0.45f);
                try
                {
                    DrawMoveHeaderRow(rowIndex, widths, state, data, gatlings, startup, active, recovery, validationReason);
                    rowIndex++;

                    for (int b = 0; b < uniqueBoxes.Count; b++)
                    {
                        BoxProps props = uniqueBoxes[b];
                        bool isReference = b == refIdx;
                        string onHitStr = "";
                        string onBlockStr = "";
                        if (isReference && refFirstFrame >= 0)
                        {
                            onHitStr = FormatOnHit(props, data.TotalTicks - refFirstFrame);
                            onBlockStr = FormatAdvantage(props.BlockstunTicks - (data.TotalTicks - refFirstFrame));
                        }

                        int firstFrame = FindFirstFrameOf(data, props);
                        DrawMoveHitboxRow(rowIndex, widths, props, firstFrame, isReference, onHitStr, onBlockStr);
                        rowIndex++;
                    }
                }
                finally
                {
                    GUI.contentColor = prevContent;
                }
            }
        }

        // Move header row: visible = Valid, State, Startup/Active/Recovery, Gatlings. Rest blacked.
        private static void DrawMoveHeaderRow(
            int rowIndex,
            float[] widths,
            CharacterState state,
            HitboxData data,
            string gatlings,
            int startup,
            int active,
            int recovery,
            string validationReason
        )
        {
            Rect row = ReserveRow(RowHeight);
            DrawRowBackground(row, MoveHeaderBg);
            DrawRowBorders(row, widths);

            int c = 0;
            DrawValidationCell(row, widths, c++, validationReason);

            if (GUI.Button(CellRect(row, widths, c), state.ToString(), EditorStyles.boldLabel))
            {
                EditorGUIUtility.PingObject(data);
                Selection.activeObject = data;
            }
            c++;

            BlackOut(row, widths, c++); // Attack
            BlackOut(row, widths, c++); // Kind
            BlackOut(row, widths, c++); // Frame
            BlackOut(row, widths, c++); // Dmg

            GUI.Label(CellRect(row, widths, c++), startup.ToString());
            GUI.Label(CellRect(row, widths, c++), active > 0 ? active.ToString() : "—");
            GUI.Label(CellRect(row, widths, c++), recovery > 0 ? recovery.ToString() : "—");

            BlackOut(row, widths, c++); // Hitstun
            BlackOut(row, widths, c++); // Blockstun
            BlackOut(row, widths, c++); // OnHit
            BlackOut(row, widths, c++); // OnBlock
            BlackOut(row, widths, c++); // KD
            BlackOut(row, widths, c++); // Unbl
            BlackOut(row, widths, c++); // Tech

            GUI.Label(CellRect(row, widths, c++), gatlings);
        }

        // Hitbox row: visible = per-box fields. Move-level fields (state, frame counts, gatlings) blacked.
        private static void DrawMoveHitboxRow(
            int rowIndex,
            float[] widths,
            BoxProps props,
            int firstFrame,
            bool isReference,
            string onHitStr,
            string onBlockStr
        )
        {
            Rect row = ReserveRow(RowHeight);
            if ((rowIndex & 1) == 1)
                DrawRowBackground(row, RowBgOdd);
            DrawRowBorders(row, widths);

            int c = 0;
            BlackOut(row, widths, c++); // Valid
            BlackOut(row, widths, c++); // State

            GUI.Label(CellRect(row, widths, c++), AttackKindLabel(props));
            GUI.Label(CellRect(row, widths, c++), KindLabel(props.Kind));
            GUI.Label(CellRect(row, widths, c++), firstFrame >= 0 ? firstFrame.ToString() : "—");
            GUI.Label(CellRect(row, widths, c++), props.Damage.ToString());

            BlackOut(row, widths, c++); // Startup
            BlackOut(row, widths, c++); // Active
            BlackOut(row, widths, c++); // Recovery

            GUI.Label(CellRect(row, widths, c++), props.HitstunTicks.ToString());
            GUI.Label(CellRect(row, widths, c++), props.BlockstunTicks.ToString());

            bool isGrab = props.Kind == HitboxKind.Grabbox;
            if (isReference && !isGrab)
            {
                GUI.Label(CellRect(row, widths, c++), onHitStr);
                GUI.Label(CellRect(row, widths, c++), onBlockStr);
            }
            else
            {
                BlackOut(row, widths, c++); // OnHit
                BlackOut(row, widths, c++); // OnBlock
            }

            GUI.Label(CellRect(row, widths, c++), KnockdownLabel(props.KnockdownKind));
            GUI.Label(CellRect(row, widths, c++), props.Unblockable ? "Y" : "");

            if (isGrab)
                GUI.Label(CellRect(row, widths, c++), props.Techable ? "Y" : "N");
            else
                BlackOut(row, widths, c++);

            BlackOut(row, widths, c++); // Gatlings
        }

        // ─────────── Projectiles ───────────

        private static void DrawProjectilesTable(CharacterConfig config)
        {
            EditorGUILayout.LabelField("Projectiles", EditorStyles.boldLabel);

            if (config.Projectiles == null || config.Projectiles.Count == 0)
            {
                EditorGUILayout.LabelField("(none)");
                return;
            }

            float[] widths = ProjectileWidths;
            DrawHeaderRow(ProjectileHeaders, widths);

            int rowIndex = 0;
            for (int i = 0; i < config.Projectiles.Count; i++)
            {
                ProjectileConfig p = config.Projectiles[i];
                if (p == null)
                    continue;

                DrawProjectileHeaderRow(rowIndex, widths, p, onDeathLabel: null);
                rowIndex++;
                rowIndex += DrawProjectileHitboxRows(rowIndex, widths, p.HitboxData);

                if (p.HasOnDeath)
                {
                    DrawProjectileHeaderRow(rowIndex, widths, p, onDeathLabel: "    ↳ on-death");
                    rowIndex++;
                    rowIndex += DrawProjectileHitboxRows(rowIndex, widths, p.OnDeathHitbox);
                }
            }
        }

        // Projectile header row: visible = Trigger State / Spawn / Lifetime (or on-death marker). Rest blacked.
        private static void DrawProjectileHeaderRow(int rowIndex, float[] widths, ProjectileConfig p, string onDeathLabel)
        {
            Rect row = ReserveRow(RowHeight);
            DrawRowBackground(row, MoveHeaderBg);
            DrawRowBorders(row, widths);

            int c = 0;
            if (onDeathLabel != null)
            {
                GUI.Label(CellRect(row, widths, c), onDeathLabel, EditorStyles.boldLabel);
                c++;
                BlackOut(row, widths, c++); // Spawn
                BlackOut(row, widths, c++); // Lifetime
            }
            else
            {
                if (GUI.Button(CellRect(row, widths, c), p.TriggerState.ToString(), EditorStyles.boldLabel))
                {
                    EditorGUIUtility.PingObject(p);
                    Selection.activeObject = p;
                }
                c++;
                GUI.Label(CellRect(row, widths, c++), p.SpawnTick.ToString());
                GUI.Label(CellRect(row, widths, c++), p.LifetimeTicks.ToString());
            }

            BlackOut(row, widths, c++); // Attack
            BlackOut(row, widths, c++); // Kind
            BlackOut(row, widths, c++); // Frame
            BlackOut(row, widths, c++); // Dmg
            BlackOut(row, widths, c++); // Hitstun
            BlackOut(row, widths, c++); // Blockstun
            BlackOut(row, widths, c++); // KD
            BlackOut(row, widths, c++); // Unbl
        }

        private static int DrawProjectileHitboxRows(int startingRowIndex, float[] widths, HitboxData hitbox)
        {
            if (hitbox == null)
            {
                DrawProjectileMissingHitboxRow(startingRowIndex, widths);
                return 1;
            }

            List<BoxProps> boxes = CollectUniqueHitboxes(hitbox);
            if (boxes.Count == 0)
            {
                DrawProjectileMissingHitboxRow(startingRowIndex, widths);
                return 1;
            }

            for (int b = 0; b < boxes.Count; b++)
            {
                int firstFrame = FindFirstFrameOf(hitbox, boxes[b]);
                DrawProjectileHitboxRow(startingRowIndex + b, widths, boxes[b], firstFrame);
            }
            return boxes.Count;
        }

        private static void DrawProjectileHitboxRow(int rowIndex, float[] widths, BoxProps props, int firstFrame)
        {
            Rect row = ReserveRow(RowHeight);
            if ((rowIndex & 1) == 1)
                DrawRowBackground(row, RowBgOdd);
            DrawRowBorders(row, widths);

            int c = 0;
            BlackOut(row, widths, c++); // Trigger State
            BlackOut(row, widths, c++); // Spawn
            BlackOut(row, widths, c++); // Lifetime

            GUI.Label(CellRect(row, widths, c++), AttackKindLabel(props));
            GUI.Label(CellRect(row, widths, c++), KindLabel(props.Kind));
            GUI.Label(CellRect(row, widths, c++), firstFrame >= 0 ? firstFrame.ToString() : "—");
            GUI.Label(CellRect(row, widths, c++), props.Damage.ToString());
            GUI.Label(CellRect(row, widths, c++), props.HitstunTicks.ToString());
            GUI.Label(CellRect(row, widths, c++), props.BlockstunTicks.ToString());

            GUI.Label(CellRect(row, widths, c++), KnockdownLabel(props.KnockdownKind));
            GUI.Label(CellRect(row, widths, c++), props.Unblockable ? "Y" : "");
        }

        private static void DrawProjectileMissingHitboxRow(int rowIndex, float[] widths)
        {
            Rect row = ReserveRow(RowHeight);
            if ((rowIndex & 1) == 1)
                DrawRowBackground(row, RowBgOdd);
            DrawRowBorders(row, widths);

            int c = 0;
            BlackOut(row, widths, c++); // Trigger State
            BlackOut(row, widths, c++); // Spawn
            BlackOut(row, widths, c++); // Lifetime
            GUI.Label(CellRect(row, widths, c++), "(no hitbox)");
            for (; c < widths.Length; c++)
                BlackOut(row, widths, c);
        }

        // ─────────── Table rendering helpers ───────────

        private static float[] WithFlexLast(float[] baseWidths)
        {
            float[] effective = (float[])baseWidths.Clone();
            float available = EditorGUIUtility.currentViewWidth - 30f;
            float sumFixed = 0f;
            for (int i = 0; i < effective.Length - 1; i++)
                sumFixed += effective[i];
            float lastMin = effective[effective.Length - 1];
            effective[effective.Length - 1] = Mathf.Max(lastMin, available - sumFixed);
            return effective;
        }

        private static void DrawHeaderRow(string[] headers, float[] widths)
        {
            Rect row = ReserveRow(HeaderHeight);
            DrawRowBackground(row, HeaderBg);
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(new Rect(row.x, row.y, TotalWidth(widths), 1f), BorderColor);
            }
            DrawRowBorders(row, widths);
            for (int i = 0; i < headers.Length; i++)
            {
                GUI.Label(CellRect(row, widths, i), headers[i], EditorStyles.boldLabel);
            }
        }

        private static Rect ReserveRow(float height)
        {
            return GUILayoutUtility.GetRect(0f, height, GUILayout.ExpandWidth(true));
        }

        private static void DrawRowBackground(Rect row, Color color)
        {
            if (Event.current.type != EventType.Repaint)
                return;
            EditorGUI.DrawRect(row, color);
        }

        private static void DrawRowBorders(Rect row, float[] widths)
        {
            if (Event.current.type != EventType.Repaint)
                return;

            float totalWidth = TotalWidth(widths);

            // Bottom and outer left/right borders.
            EditorGUI.DrawRect(new Rect(row.x, row.yMax - 1f, totalWidth, 1f), BorderColor);
            EditorGUI.DrawRect(new Rect(row.x, row.y, 1f, row.height), BorderColor);
            EditorGUI.DrawRect(new Rect(row.x + totalWidth - 1f, row.y, 1f, row.height), BorderColor);

            // Column dividers.
            float x = row.x;
            for (int i = 0; i < widths.Length - 1; i++)
            {
                x += widths[i];
                EditorGUI.DrawRect(new Rect(x, row.y, 1f, row.height), BorderColor);
            }
        }

        private static float TotalWidth(float[] widths)
        {
            float sum = 0f;
            for (int i = 0; i < widths.Length; i++)
                sum += widths[i];
            return sum;
        }

        private static Rect CellRect(Rect row, float[] widths, int cellIndex)
        {
            float x = row.x;
            for (int i = 0; i < cellIndex; i++)
                x += widths[i];
            return new Rect(x + CellPadX, row.y, widths[cellIndex] - CellPadX * 2f, row.height);
        }

        // Renders the validation indicator. Empty cell if valid, red "!" with tooltip if not.
        // Text color is forced full-opacity so the warning stays visible even when the surrounding
        // row is grayed out (contentColor has been faded by the caller).
        private static void DrawValidationCell(Rect row, float[] widths, int cellIndex, string reason)
        {
            if (reason == null)
                return;
            Color prev = GUI.contentColor;
            GUI.contentColor = ValidationWarnColor;
            GUI.Label(CellRect(row, widths, cellIndex), new GUIContent("!", reason), CenteredBoldStyle);
            GUI.contentColor = prev;
        }

        // Fills the interior of a cell with a dark overlay to signal "not applicable on this row".
        private static void BlackOut(Rect row, float[] widths, int cellIndex)
        {
            if (Event.current.type != EventType.Repaint)
                return;
            float x = row.x;
            for (int i = 0; i < cellIndex; i++)
                x += widths[i];
            EditorGUI.DrawRect(new Rect(x + 1f, row.y + 1f, widths[cellIndex] - 1f, row.height - 2f), BlackedCell);
        }

        // ─────────── Frame-data analysis ───────────

        // Returns null if the frame tags obey:
        //   - Startup = exactly the frames before the first hitbox
        //   - Active  = exactly [firstHit..lastHit] (smallest window containing every hitbox)
        //   - Recovery = exactly the frames after the last hitbox
        // Otherwise returns a short human-readable reason (used as cell tooltip).
        private static string ValidateRule(HitboxData data)
        {
            if (data == null || data.Frames == null || data.Frames.Count == 0)
                return "no frames";

            int firstHit = -1;
            int lastHit = -1;
            for (int i = 0; i < data.Frames.Count; i++)
            {
                FrameData frame = data.Frames[i];
                if (frame != null && frame.HasHitbox(out _))
                {
                    if (firstHit < 0)
                        firstHit = i;
                    lastHit = i;
                }
            }
            if (firstHit < 0)
                return "no hitbox frames";

            for (int i = 0; i < data.Frames.Count; i++)
            {
                FrameData frame = data.Frames[i];
                if (frame == null)
                    return $"frame {i} missing";
                FrameType expected;
                if (i < firstHit)
                    expected = FrameType.Startup;
                else if (i <= lastHit)
                    expected = FrameType.Active;
                else
                    expected = FrameType.Recovery;
                if (frame.FrameType != expected)
                    return $"frame {i}: expected {expected}, got {frame.FrameType}";
            }
            return null;
        }

        // Counts Startup/Active/Recovery-tagged frames. Mirrors HitboxData.IsValidAttack but
        // doesn't require a hitbox — we want to render moves that have the SAR sequence tagged
        // even if they happen to have no hitbox frames.
        // Returns true when the full Startup→Active→Recovery sequence is present with non-zero counts.
        private static bool TryComputeSAR(HitboxData data, out int startup, out int active, out int recovery)
        {
            startup = 0;
            active = 0;
            recovery = 0;
            if (data == null || data.Frames == null || data.Frames.Count == 0)
                return false;

            FrameType[] order = { FrameType.Startup, FrameType.Active, FrameType.Recovery };
            int[] counts = new int[3];
            int phase = 0;
            foreach (FrameData frame in data.Frames)
            {
                if (frame == null)
                    continue;
                if (frame.FrameType != order[phase])
                {
                    if (phase + 1 >= order.Length)
                        return false;
                    if (frame.FrameType != order[phase + 1])
                        return false;
                    phase++;
                }
                counts[phase]++;
            }

            startup = counts[0];
            active = counts[1];
            recovery = counts[2];
            // Startup can legitimately be 0 (move begins on an Active frame).
            return active > 0 && recovery > 0;
        }

        private static List<BoxProps> CollectUniqueHitboxes(HitboxData data)
        {
            var result = new List<BoxProps>();
            if (data == null || data.Frames == null)
                return result;

            for (int f = 0; f < data.Frames.Count; f++)
            {
                FrameData frame = data.Frames[f];
                if (frame == null || frame.Boxes == null)
                    continue;
                for (int b = 0; b < frame.Boxes.Count; b++)
                {
                    BoxProps props = frame.Boxes[b].Props;
                    if (props.Kind == HitboxKind.Hurtbox)
                        continue;
                    if (!result.Contains(props))
                        result.Add(props);
                }
            }
            return result;
        }

        private static int FindFirstFrameOf(HitboxData data, BoxProps target)
        {
            if (data == null || data.Frames == null)
                return -1;
            for (int f = 0; f < data.Frames.Count; f++)
            {
                FrameData frame = data.Frames[f];
                if (frame == null || frame.Boxes == null)
                    continue;
                for (int b = 0; b < frame.Boxes.Count; b++)
                {
                    BoxProps props = frame.Boxes[b].Props;
                    if (props.Kind == HitboxKind.Hurtbox)
                        continue;
                    if (props.Equals(target))
                        return f;
                }
            }
            return -1;
        }

        // Unique hitbox whose latest-occurring frame is the largest — the "last timing-wise" hit.
        private static int FindLatestUniqueBoxIndex(List<BoxProps> uniqueBoxes, HitboxData data)
        {
            if (uniqueBoxes.Count == 0 || data == null || data.Frames == null)
                return -1;

            for (int f = data.Frames.Count - 1; f >= 0; f--)
            {
                FrameData frame = data.Frames[f];
                if (frame == null || frame.Boxes == null)
                    continue;
                for (int b = 0; b < frame.Boxes.Count; b++)
                {
                    BoxProps props = frame.Boxes[b].Props;
                    if (props.Kind == HitboxKind.Hurtbox)
                        continue;
                    int idx = uniqueBoxes.IndexOf(props);
                    if (idx >= 0)
                        return idx;
                }
            }
            return -1;
        }

        // Start of the last contiguous run of frames that contain a hitbox with these exact props.
        private static int FindFirstFrameOfLastContiguousRun(HitboxData data, BoxProps refProps)
        {
            if (data == null || data.Frames == null)
                return -1;

            int lastRunStart = -1;
            bool inRun = false;
            for (int f = 0; f < data.Frames.Count; f++)
            {
                FrameData frame = data.Frames[f];
                bool has = false;
                if (frame != null && frame.Boxes != null)
                {
                    for (int b = 0; b < frame.Boxes.Count; b++)
                    {
                        BoxProps props = frame.Boxes[b].Props;
                        if (props.Kind == HitboxKind.Hurtbox)
                            continue;
                        if (props.Equals(refProps))
                        {
                            has = true;
                            break;
                        }
                    }
                }
                if (has && !inRun)
                {
                    lastRunStart = f;
                    inRun = true;
                }
                else if (!has)
                {
                    inRun = false;
                }
            }
            return lastRunStart;
        }

        private static string GatlingsFor(CharacterConfig config, CharacterState from)
        {
            if (config.Gatlings == null || config.Gatlings.Count == 0)
                return "";
            var sb = new StringBuilder();
            for (int i = 0; i < config.Gatlings.Count; i++)
            {
                GatlingEntry entry = config.Gatlings[i];
                if (entry.From != from)
                    continue;
                if (sb.Length > 0)
                    sb.Append(", ");
                sb.Append(entry.To.ToString());
            }
            return sb.ToString();
        }

        // ─────────── Label formatting ───────────

        private static string FormatOnHit(BoxProps props, int ticksFromReferenceFrame)
        {
            // Hard/soft knockdown overrides numeric advantage — the defender is on the ground,
            // so frame advantage isn't meaningful in the normal sense.
            switch (props.KnockdownKind)
            {
                case KnockdownKind.Heavy:
                    return "HKD";
                case KnockdownKind.Light:
                    return "SKD";
                default:
                    return FormatAdvantage(props.HitstunTicks - ticksFromReferenceFrame);
            }
        }

        private static string FormatAdvantage(int adv)
        {
            return adv > 0 ? "+" + adv : adv.ToString();
        }

        private static string AttackKindLabel(BoxProps props)
        {
            if (props.Kind == HitboxKind.Grabbox)
                return "—";
            switch (props.AttackKind)
            {
                case AttackKind.Medium:
                    return "Mid";
                case AttackKind.Overhead:
                    return "High";
                case AttackKind.Low:
                    return "Low";
                default:
                    return props.AttackKind.ToString();
            }
        }

        private static string KindLabel(HitboxKind kind)
        {
            switch (kind)
            {
                case HitboxKind.Hitbox:
                    return "Hit";
                case HitboxKind.Grabbox:
                    return "Grab";
                case HitboxKind.Hurtbox:
                    return "Hurt";
                default:
                    return kind.ToString();
            }
        }

        private static string KnockdownLabel(KnockdownKind kind)
        {
            switch (kind)
            {
                case KnockdownKind.Heavy:
                    return "Hard";
                case KnockdownKind.Light:
                    return "Soft";
                case KnockdownKind.None:
                    return "";
                default:
                    return kind.ToString();
            }
        }
    }
}
