using UnityEngine;

namespace Game.View.Events.Vfx
{
    [RequireComponent(typeof(Animator))]
    public class AnimatorVfxEffect : VfxEffect
    {
        [SerializeField]
        private string _stateName = "Block";

        [SerializeField]
        private bool _rotateWithDirection = true;

        public override void StartEffect(ViewEvent<VfxEvent> ev)
        {
            transform.position = new Vector3(ev.Event.Position.x, ev.Event.Position.y, transform.position.z);

            if (_rotateWithDirection)
            {
                transform.rotation = Quaternion.FromToRotation(Vector3.right, -ev.Event.Direction);
            }

            GetComponent<Animator>().Play(_stateName);
        }

        public override void EndEffect() { }

        public override bool EffectIsFinished()
        {
            return GetComponent<Animator>().GetCurrentAnimatorStateInfo(0).normalizedTime >= 1f;
        }
    }
}
