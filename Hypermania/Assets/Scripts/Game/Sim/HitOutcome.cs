using Design.Animation;
using Utils.SoftFloat;

namespace Game.Sim
{
    public enum HitKind
    {
        None,
        Blocked,
        Hit,
        Grabbed,
    }

    public struct HitOutcome
    {
        public HitKind Kind;
        public BoxProps Props;
    }
}
