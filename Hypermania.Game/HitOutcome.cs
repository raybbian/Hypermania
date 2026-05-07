using Hypermania.Game.Configs;

namespace Hypermania.Game   
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
