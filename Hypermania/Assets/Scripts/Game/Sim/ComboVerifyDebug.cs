using System.Collections.Generic;
using System.Reflection;
using System.Text;
using MemoryPack;
using UnityEngine;
using Utils;
using Utils.SoftFloat;

namespace Game.Sim
{
    /// <summary>
    /// Developer-only invariant checker for the rhythm combo system. When
    /// <see cref="InfoOptions.VerifyComboPrediction"/> is enabled,
    /// <see cref="ComboGenerator"/> captures a deep clone of its
    /// <c>_working</c> state at <c>noteTick + HitHalfRange</c> for every
    /// beat it generates (see <see cref="ComboBeatSnapshot"/>), and
    /// <see cref="RhythmComboManager.StartRhythmCombo"/> registers those
    /// snapshots here. When the real simulation reaches each snapshot's
    /// <c>CompareFrame</c>, <see cref="CheckAtFrame"/> diffs the real
    /// <see cref="GameState.Fighters"/> array against the predicted one
    /// field by field and logs MATCH, or the exact list of differing
    /// fighter fields on MISMATCH. Non-fighter state (GameMode, ManiaState,
    /// Hype, etc.) is intentionally excluded from the diff — the generator
    /// runs in Fighting mode with AlwaysRhythmCancel, so those fields
    /// legitimately diverge from the real Mania-mode sim even when the
    /// beat-snap invariant holds.
    ///
    /// State is static so it survives rollback (statics aren't included in
    /// MemoryPack serialization). During rollback re-advance the predictions
    /// are recomputed deterministically and overwrite the dict entries.
    /// </summary>
    public static class ComboVerifyDebug
    {
        private struct Pending
        {
            public GameState Predicted;
            public int AttackerIndex;
        }

        private static readonly Dictionary<Frame, Pending> _pending = new Dictionary<Frame, Pending>();

        /// <summary>Scratch list reused by <see cref="DiscardFutureSnapshots"/>.</summary>
        private static readonly List<Frame> _scratchRemove = new List<Frame>();

        /// <summary>
        /// Field names to skip during the reflective fighter diff. These fields
        /// legitimately diverge between the generator's Fighting-mode sim and
        /// the real game's Mania-mode sim:
        ///   - <c>InputH</c>: the real game's attacker receives Mania channel
        ///     inputs routed through DoManiaStep, while the generator applies
        ///     direct attack inputs with AlwaysRhythmCancel. The downstream
        ///     fighter effect is equivalent, but the raw input-history ring
        ///     buffer stores different flags.
        ///   - <c>Super</c>: DoManiaStep dissipates the attacker's super
        ///     meter every frame the mania is active; the generator never
        ///     runs DoManiaStep, so its meter stays put.
        /// Matched by <see cref="CleanFieldName"/>-cleaned name (so
        /// auto-property backing fields like <c>&lt;Super&gt;k__BackingField</c>
        /// are matched as <c>Super</c>).
        /// </summary>
        private static readonly HashSet<string> IgnoredFields = new HashSet<string> { "InputH", "Super" };

        /// <summary>Max recursion depth for the field-by-field diff.</summary>
        private const int MAX_DIFF_DEPTH = 10;

        /// <summary>
        /// Max number of differing fields to report per mismatch; protects
        /// against log spam when everything desyncs (e.g. total divergence).
        /// </summary>
        private const int MAX_DIFF_LINES = 64;

        public static void StorePrediction(Frame frame, GameState predicted, int attackerIndex)
        {
            _pending[frame] = new Pending { Predicted = predicted, AttackerIndex = attackerIndex };
        }

        public static void CheckAtFrame(Frame frame, GameState actual)
        {
            if (!_pending.TryGetValue(frame, out Pending p))
                return;
            _pending.Remove(frame);

            StringBuilder diff = new StringBuilder();
            int lines = 0;
            DiffValue(p.Predicted.Fighters, actual.Fighters, "Fighters", diff, 0, ref lines);

            if (diff.Length > 0)
            {
                throw new System.InvalidOperationException(
                    $"[ComboVerify] MISMATCH  attacker={p.AttackerIndex} frame={frame.No}\n{diff}"
                );
            }
        }

        /// <summary>
        /// Drop every remaining pending snapshot for <paramref name="attackerIndex"/>
        /// whose <c>CompareFrame</c> is strictly after <paramref name="currentFrame"/>.
        /// Called when the real simulation's mania terminates before its natural
        /// <c>EndFrame</c> (miss, death, etc.) so the remaining snapshots — which
        /// belong to beats that will never be reached — don't log spurious
        /// MISMATCHes against a sim that has already left combo mode.
        /// </summary>
        public static void DiscardFutureSnapshots(int attackerIndex, Frame currentFrame)
        {
            _scratchRemove.Clear();
            foreach (var kvp in _pending)
            {
                if (kvp.Value.AttackerIndex == attackerIndex && kvp.Key > currentFrame)
                    _scratchRemove.Add(kvp.Key);
            }
            for (int i = 0; i < _scratchRemove.Count; i++)
                _pending.Remove(_scratchRemove[i]);
            _scratchRemove.Clear();
        }

        public static void Clear()
        {
            _pending.Clear();
        }

        // ------------------------------------------------------------------
        // Reflective diff. Walks two GameStates in lockstep and appends one
        // line per differing field to <paramref name="sb"/>.
        //
        // Terminal types (primitives, enums, strings, and known atomic
        // structs with meaningful ToString) are compared with Equals and
        // printed as expected/actual values.
        //
        // Composite types (other structs and classes) have their instance
        // fields enumerated and recursed into; [MemoryPackIgnore]-decorated
        // fields are skipped so the diff matches Checksum semantics.
        //
        // Collections (arrays, List<T>, Deque<T>) are compared elementwise
        // after a Count/Length check.
        // ------------------------------------------------------------------

        private static void DiffValue(
            object expected,
            object actual,
            string path,
            StringBuilder sb,
            int depth,
            ref int lineCount
        )
        {
            if (lineCount >= MAX_DIFF_LINES)
                return;
            if (depth > MAX_DIFF_DEPTH)
                return;

            if (expected == null && actual == null)
                return;
            if (expected == null || actual == null)
            {
                AppendDiffLine(sb, path, expected, actual, ref lineCount);
                return;
            }

            System.Type t = expected.GetType();
            System.Type tActual = actual.GetType();
            if (t != tActual)
            {
                AppendDiffLine(sb, path + "[Type]", t.Name, tActual.Name, ref lineCount);
                return;
            }

            if (IsTerminalType(t))
            {
                if (!expected.Equals(actual))
                    AppendDiffLine(sb, path, expected, actual, ref lineCount);
                return;
            }

            if (t.IsArray)
            {
                System.Array eArr = (System.Array)expected;
                System.Array aArr = (System.Array)actual;
                if (eArr.Length != aArr.Length)
                {
                    AppendDiffLine(sb, path + ".Length", eArr.Length, aArr.Length, ref lineCount);
                    return;
                }
                for (int i = 0; i < eArr.Length; i++)
                {
                    DiffValue(eArr.GetValue(i), aArr.GetValue(i), $"{path}[{i}]", sb, depth + 1, ref lineCount);
                    if (lineCount >= MAX_DIFF_LINES)
                        return;
                }
                return;
            }

            if (t.IsGenericType)
            {
                System.Type gdef = t.GetGenericTypeDefinition();
                if (gdef == typeof(List<>) || gdef == typeof(Deque<>))
                {
                    DiffIndexedCollection(expected, actual, path, sb, depth, ref lineCount);
                    return;
                }
                if (gdef == typeof(System.Nullable<>))
                {
                    // Nullable<T>: compare HasValue, then Value.
                    bool eHas = (bool)t.GetProperty("HasValue").GetValue(expected);
                    bool aHas = (bool)t.GetProperty("HasValue").GetValue(actual);
                    if (eHas != aHas)
                    {
                        AppendDiffLine(sb, path + ".HasValue", eHas, aHas, ref lineCount);
                        return;
                    }
                    if (!eHas)
                        return;
                    object eVal = t.GetProperty("Value").GetValue(expected);
                    object aVal = t.GetProperty("Value").GetValue(actual);
                    DiffValue(eVal, aVal, path + ".Value", sb, depth + 1, ref lineCount);
                    return;
                }
            }

            // Fall through: walk instance fields (including backing fields
            // for auto-properties).
            FieldInfo[] fields = t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            for (int i = 0; i < fields.Length; i++)
            {
                FieldInfo f = fields[i];
                if (f.IsDefined(typeof(MemoryPackIgnoreAttribute), false))
                    continue;
                string name = CleanFieldName(f.Name);
                if (IgnoredFields.Contains(name))
                    continue;
                object ev = f.GetValue(expected);
                object av = f.GetValue(actual);
                DiffValue(ev, av, $"{path}.{name}", sb, depth + 1, ref lineCount);
                if (lineCount >= MAX_DIFF_LINES)
                    return;
            }
        }

        private static void DiffIndexedCollection(
            object expected,
            object actual,
            string path,
            StringBuilder sb,
            int depth,
            ref int lineCount
        )
        {
            System.Type t = expected.GetType();
            PropertyInfo countProp = t.GetProperty("Count");
            int eCount = (int)countProp.GetValue(expected);
            int aCount = (int)countProp.GetValue(actual);
            if (eCount != aCount)
            {
                AppendDiffLine(sb, path + ".Count", eCount, aCount, ref lineCount);
                return;
            }
            PropertyInfo indexer = t.GetProperty("Item");
            object[] idxArgs = new object[1];
            for (int i = 0; i < eCount; i++)
            {
                idxArgs[0] = i;
                object ev = indexer.GetValue(expected, idxArgs);
                object av = indexer.GetValue(actual, idxArgs);
                DiffValue(ev, av, $"{path}[{i}]", sb, depth + 1, ref lineCount);
                if (lineCount >= MAX_DIFF_LINES)
                    return;
            }
        }

        private static bool IsTerminalType(System.Type t)
        {
            if (t.IsPrimitive || t.IsEnum)
                return true;
            if (t == typeof(string))
                return true;
            // sfloat, Frame, SVector2, SVector3 all have meaningful ToString
            // and implement value-equality via Equals; treat as terminal so
            // diffs read "Position: expected=(1.5, 0) actual=(1.7, 0)" rather
            // than drilling into the raw bit fields.
            if (t == typeof(sfloat) || t == typeof(Frame))
                return true;
            if (t == typeof(SVector2) || t == typeof(SVector3))
                return true;
            return false;
        }

        private static void AppendDiffLine(
            StringBuilder sb,
            string path,
            object expected,
            object actual,
            ref int lineCount
        )
        {
            sb.Append("  ");
            sb.Append(path);
            sb.Append(": expected=");
            sb.Append(expected == null ? "null" : expected.ToString());
            sb.Append(" actual=");
            sb.Append(actual == null ? "null" : actual.ToString());
            sb.Append('\n');
            lineCount++;
            if (lineCount == MAX_DIFF_LINES)
            {
                sb.Append("  ... (diff truncated at ");
                sb.Append(MAX_DIFF_LINES);
                sb.Append(" lines)\n");
            }
        }

        /// <summary>
        /// Strips the compiler-generated &lt;PropertyName&gt;k__BackingField
        /// decoration from auto-property backing field names so diff paths
        /// read like source (e.g. "State" not "&lt;State&gt;k__BackingField").
        /// </summary>
        private static string CleanFieldName(string name)
        {
            if (name.Length > 0 && name[0] == '<')
            {
                int end = name.IndexOf('>');
                if (end > 1)
                    return name.Substring(1, end - 1);
            }
            return name;
        }
    }
}
