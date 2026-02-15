using UnityEngine;

namespace Game.View.Events.Vfx
{
    public abstract class VfxEffect : MonoBehaviour
    {
        public abstract void StartEffect(ViewEvent<VfxEvent> ev);
        public abstract void EndEffect();
        public abstract bool EffectIsFinished();
    }
}
