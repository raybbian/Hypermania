using System.Collections.Generic;
using UnityEngine;

namespace Design.Animation.Sorting
{
    [DisallowMultipleComponent]
    public sealed class SpriteSortGroup : MonoBehaviour
    {
        [SerializeField]
        private int _sortingLayerId;
        public int SortingLayerId => _sortingLayerId;

        [SerializeField]
        private int _baseOrder = 0;
        public int BaseOrder => _baseOrder;

        public void ApplyToRenderers()
        {
            List<SpriteSortItem> items = new List<SpriteSortItem>();
            GetComponentsInChildren(includeInactive: true, result: items);

            items.Sort(CompareItemsByRendererOrderThenSibling);

            int order = _baseOrder;

            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item == null)
                    continue;

                var r = item.Renderer;
                if (r == null)
                    continue;

                r.sortingLayerID = _sortingLayerId;
                r.sortingOrder = order;
                order++;
            }
        }

        public static int CompareItemsByRendererOrderThenSibling(SpriteSortItem a, SpriteSortItem b)
        {
            if (ReferenceEquals(a, b))
                return 0;
            if (a == null)
                return 1;
            if (b == null)
                return -1;

            var ra = a.Renderer;
            var rb = b.Renderer;

            int oa = ra ? ra.sortingOrder : int.MaxValue;
            int ob = rb ? rb.sortingOrder : int.MaxValue;

            if (oa != ob)
                return oa.CompareTo(ob);

            var ta = a.transform;
            var tb = b.transform;

            if (ta.parent == tb.parent)
                return ta.GetSiblingIndex().CompareTo(tb.GetSiblingIndex());

            return string.CompareOrdinal(ta.name, tb.name);
        }
    }
}
