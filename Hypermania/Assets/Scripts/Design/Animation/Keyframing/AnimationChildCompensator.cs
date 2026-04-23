using UnityEngine;

namespace Design.Animation.Keyframing
{
    public sealed class AnimationChildCompensator : MonoBehaviour
    {
        [Tooltip(
            "The parent whose direct children should stay world-locked when it is dragged. "
                + "Defaults to this transform if null."
        )]
        [SerializeField]
        private Transform _parent;

        public Transform Parent => _parent != null ? _parent : transform;
        public Transform AnimRoot => transform;
    }
}
