using UnityEngine;

namespace Game.View.Events.Vfx
{
    [RequireComponent(typeof(Animator))]
    public class BlockEffect : VfxEffect
    {
        private string _startStateName = "Block";

        public override void StartEffect(ViewEvent<VfxEvent> ev)
        {
            transform.position = new Vector3(ev.Event.Position.x, ev.Event.Position.y, transform.position.z);
            transform.rotation = Quaternion.FromToRotation(Vector3.right, -ev.Event.KnockbackVector);
            Animator animator = GetComponent<Animator>();
            animator.Play(_startStateName);
        }

        public override void EndEffect() { }

        public override bool EffectIsFinished()
        {
            return GetComponent<Animator>().GetCurrentAnimatorStateInfo(0).normalizedTime >= 1f;
        }
    }
}
