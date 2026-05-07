using System;
using Hypermania.Shared;

namespace Hypermania.Game
{
    public sealed class Pool<T>
    {
        private readonly (T item, bool valid)[] _objects;
        private readonly Deque<int> _freeList;

        public int Capacity => _objects.Length;

        public Pool(int maxObjects)
        {
            if (maxObjects <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxObjects));
            _objects = new (T, bool)[maxObjects];
            _freeList = new Deque<int>(maxObjects); // must be pow2-safe internally
            for (int i = 0; i < maxObjects; i++)
                _freeList.PushBack(i);
        }

        public int Spawn(in T item)
        {
            if (_freeList.Count == 0)
                throw new InvalidOperationException("No more space in pool");

            int ind = _freeList.PopFront();
            if (_objects[ind].valid)
                throw new InvalidOperationException("Pool corruption: spawning an already-valid slot");

            _objects[ind].valid = true;
            _objects[ind].item = item;
            return ind;
        }

        public void Release(int ind)
        {
            if ((uint)ind >= (uint)_objects.Length)
                throw new ArgumentOutOfRangeException(nameof(ind));

            if (!_objects[ind].valid)
                throw new InvalidOperationException("Double free / invalid release");

            _objects[ind] = default!;
            _objects[ind].valid = false;
            _freeList.PushBack(ind);
        }

        public void Clear()
        {
            for (int i = 0; i < _objects.Length; i++)
            {
                if (_objects[i].valid)
                {
                    Release(i);
                }
            }
        }

        public ref T this[int index]
        {
            get => ref _objects[index].item;
        }

        public ref T Get(int ind)
        {
            if ((uint)ind >= (uint)_objects.Length)
                throw new ArgumentOutOfRangeException(nameof(ind));
            if (!_objects[ind].valid)
                throw new InvalidOperationException("Accessing invalid slot");
            return ref _objects[ind].item;
        }

        public bool IsValid(int ind) => (uint)ind < (uint)_objects.Length && _objects[ind].valid;
    }
}
