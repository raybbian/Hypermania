using System;
using System.Collections.Generic;
using UnityEngine;

namespace Design.Animation.MoveBuilder.Editors
{
    public sealed class MoveBuilderVisibilityModel
    {
        [Serializable]
        public struct VisibilityNode
        {
            public int Id;
            public int ParentId;
            public int Depth;
            public string Name;
            public string Path;
        }

        [SerializeField]
        public bool ShowVisibilityPanel;

        [SerializeField]
        private List<VisibilityNode> _visibilityNodes = new();

        [SerializeField]
        private List<bool> _visibilityStates = new();

        private MoveBuilderModel _builderModel;

        public IReadOnlyList<VisibilityNode> VisibilityNodes => _visibilityNodes;

        public MoveBuilderVisibilityModel(MoveBuilderModel builderModel)
        {
            _builderModel = builderModel;
        }

        public Action OnVisibilityCacheUpdated;

        public void RebuildVisibilityCache()
        {
            if (_builderModel.CharacterPrefab == null)
            {
                _visibilityNodes.Clear();
                _visibilityStates.Clear();
                OnVisibilityCacheUpdated.Invoke();
                return;
            }

            _visibilityNodes.Clear();
            _visibilityStates.Clear();

            Transform root = _builderModel.CharacterPrefab.transform;
            int nextId = 0;

            // Add prefab root node (so the panel never appears empty)
            int rootId = nextId++;
            _visibilityNodes.Add(
                new VisibilityNode
                {
                    Id = rootId,
                    ParentId = -1,
                    Depth = 0,
                    Name = _builderModel.CharacterPrefab.name,
                    Path = string.Empty, // special-case: root
                }
            );
            _visibilityStates.Add(true);

            void Walk(Transform t, int parentId, int depth, string parentPath)
            {
                for (int i = 0; i < t.childCount; i++)
                {
                    Transform c = t.GetChild(i);
                    string path = string.IsNullOrEmpty(parentPath) ? c.name : $"{parentPath}/{c.name}";
                    int id = nextId++;

                    _visibilityNodes.Add(
                        new VisibilityNode
                        {
                            Id = id,
                            ParentId = parentId,
                            Depth = depth,
                            Name = c.name,
                            Path = path,
                        }
                    );
                    _visibilityStates.Add(true);

                    Walk(c, id, depth + 1, path);
                }
            }

            // children of prefab root
            Walk(root, rootId, 1, string.Empty);
            OnVisibilityCacheUpdated.Invoke();
        }

        public bool GetPathVisible(string path)
        {
            int idx = FindIndexByPath(path);
            if (idx < 0 || idx >= _visibilityStates.Count)
                return true;
            return _visibilityStates[idx];
        }

        public void SetPathVisible(string path, bool visible)
        {
            int idx = FindIndexByPath(path);
            if (idx < 0 || idx >= _visibilityStates.Count)
                return;

            _visibilityStates[idx] = visible;
        }

        public bool TryGetPathById(int id, out string path)
        {
            for (int i = 0; i < _visibilityNodes.Count; i++)
            {
                if (_visibilityNodes[i].Id == id)
                {
                    path = _visibilityNodes[i].Path;
                    return true;
                }
            }
            path = null;
            return false;
        }

        private int FindIndexByPath(string path)
        {
            for (int i = 0; i < _visibilityNodes.Count; i++)
            {
                if (_visibilityNodes[i].Path == path)
                    return i;
            }
            return -1;
        }
    }
}
