using Design.Animation;
using Utils.SoftFloat;

namespace Game.Sim
{
    public enum HitKind
    {
        None,
        Blocked,
        Hit,
    }

    public struct HitOutcome
    {
        public HitKind Kind;
        public BoxProps Props;
        public int HitstopFrames;
    }
}
