using System.Collections.Generic;
using System.Reflection;
using System.Text;
using MemoryPack;
using Utils;
using Utils.SoftFloat;

namespace Game.Sim
{
    // Dev-only invariant checker for the rhythm combo system. When
    // InfoOptions.VerifyComboPrediction is on, ComboGenerator clones its
    // _working state at every beat (see ComboBeatSnapshot) and
    // RhythmComboManager.StartRhythmCombo registers those clones here. When
    // the real sim reaches a snapshot's CompareFrame, CheckAtFrame diffs the
    // Fighters arrays field-by-field and throws if anything differs.
    //
    // Non-fighter state (GameMode, ManiaState, Hype, ...) is deliberately
    // excluded. The generator runs in Fighting mode with AlwaysRhythmCancel,
    // so those fields legitimately differ from the real Mania-mode sim even
    // when the beat-snap invariant holds.
    //
    // State is static so it survives rollback (statics don't go through
    // MemoryPack). On rollback re-advance, predictions get recomputed
    // deterministically and overwrite the dict entries.
    public static class ComboVerifyDebug
    {
        private struct Pending
        {
            public GameState Predicted;
            public int AttackerIndex;
        }

        private static readonly Dictionary<Frame, Pending> _pending = new Dictionary<Frame, Pending>();

        private static readonly List<Frame> _scratchRemove = new List<Frame>();

        // Fields the diff skips. These legitimately differ between the
        // generator's Fighting-mode sim and the real Mania-mode sim:
        //   InputH: in the real game, the attacker's input history records
        //     mania channel keys; the generator records the direct attack
        //     inputs it injected. Downstream fighter state ends up the same,
        //     but the raw ring buffer doesn't.
        //   Super: DoManiaStep drains the attacker's super meter every active
        //     frame; the generator skips DoManiaStep, so its meter stays put.
        // Matched by CleanFieldName so auto-property backing fields like
        // <Super>k__BackingField also count as "Super".
        private static readonly HashSet<string> IgnoredFields = new HashSet<string> { "InputH", "Super" };

        private const int MAX_DIFF_DEPTH = 10;

        // Caps how many differing fields we report. Stops total-divergence
        // mismatches from spamming the log.
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

        // Called when the real sim's mania ends early (miss, death, etc.).
        // Drops any pending snapshots for this attacker past currentFrame so
        // they don't fire spurious MISMATCHes against a sim that's already
        // out of combo mode.
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

        // Reflective diff. Walks two GameStates in lockstep and appends one
        // line per differing field to sb.
        //
        // Terminal types (primitives, enums, strings, and atomic structs with
        // a useful ToString) get compared with Equals and printed as
        // expected/actual.
        //
        // Composite types recurse into their instance fields. Anything tagged
        // [MemoryPackIgnore] is skipped so the diff matches Checksum semantics.
        //
        // Collections (arrays, List<T>, Deque<T>) are compared elementwise
        // after a Count/Length check.

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
            // sfloat, Frame, SVector2, SVector3 all have a useful ToString
            // and value-equality via Equals. Treating them as terminal makes
            // diffs read like "Position: expected=(1.5, 0) actual=(1.7, 0)"
            // instead of drilling into raw bit fields.
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

        // Strips the compiler-generated <PropertyName>k__BackingField wrapper
        // so diff paths read "State" instead of "<State>k__BackingField".
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
