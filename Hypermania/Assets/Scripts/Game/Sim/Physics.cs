using System.Collections;
using System.Collections.Generic;
using Design.Animation;
using UnityEngine;
using Utils.SoftFloat;

namespace Game.Sim
{
    public class Physics<TData>
    {
        public struct Collision
        {
            public BoxEntry BoxA;
            public BoxEntry BoxB;
            public sfloat OverlapX;
        }

        public struct BoxEntry
        {
            public int Owner;
            public Box Box;
            public TData Data;
        }

        // TODO: move this out into a utils file
        public struct Box
        {
            public SVector2 Pos;
            public SVector2 Size;

            public bool Overlaps(Box b, out sfloat overlapX)
            {
                SVector2 ah = Size * (sfloat)0.5f;
                SVector2 bh = b.Size * (sfloat)0.5f;

                sfloat aMinX = Pos.x - ah.x;
                sfloat aMaxX = Pos.x + ah.x;
                sfloat aMinY = Pos.y - ah.y;
                sfloat aMaxY = Pos.y + ah.y;

                sfloat bMinX = b.Pos.x - bh.x;
                sfloat bMaxX = b.Pos.x + bh.x;
                sfloat bMinY = b.Pos.y - bh.y;
                sfloat bMaxY = b.Pos.y + bh.y;

                sfloat ox = Mathsf.Min(aMaxX, bMaxX) - Mathsf.Max(aMinX, bMinX);
                if (ox <= sfloat.Zero)
                {
                    overlapX = sfloat.Zero;
                    return false;
                }

                sfloat oy = Mathsf.Min(aMaxY, bMaxY) - Mathsf.Max(aMinY, bMinY);
                if (oy <= sfloat.Zero)
                {
                    overlapX = sfloat.Zero;
                    return false;
                }

                overlapX = ox;
                return true;
            }

            public SVector2 ClosestPointToCenter(Box other)
            {
                SVector2 half = Size * (sfloat)0.5f;

                sfloat minX = Pos.x - half.x;
                sfloat maxX = Pos.x + half.x;
                sfloat minY = Pos.y - half.y;
                sfloat maxY = Pos.y + half.y;

                sfloat cx = Mathsf.Clamp(other.Pos.x, minX, maxX);
                sfloat cy = Mathsf.Clamp(other.Pos.y, minY, maxY);

                return new SVector2(cx, cy);
            }
        }

        private readonly Pool<BoxEntry> _boxPool;
        private readonly List<int> _boxInds;

        public Physics(int maxHitboxes)
        {
            _boxPool = new Pool<BoxEntry>(maxHitboxes);
            _boxInds = new List<int>(maxHitboxes);
        }

        public int AddBox(int handle, SVector2 boxPos, SVector2 boxSize, in TData data)
        {
            int ind = _boxPool.Spawn(
                new BoxEntry
                {
                    Owner = handle,
                    Box = new Box { Pos = boxPos, Size = boxSize },
                    Data = data,
                }
            );
            _boxInds.Add(ind);
            return ind;
        }

        // TODO: optimize?
        public void GetCollisions(List<Collision> collisions)
        {
            int n = _boxInds.Count;
            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    int boxAInd = _boxInds[i];
                    int boxBInd = _boxInds[j];
                    BoxEntry a = _boxPool[boxAInd];
                    BoxEntry b = _boxPool[boxBInd];

                    if (a.Owner == b.Owner)
                    {
                        continue;
                    }

                    if (a.Box.Overlaps(b.Box, out sfloat overlapX))
                    {
                        collisions.Add(
                            new Collision
                            {
                                BoxA = a,
                                BoxB = b,
                                OverlapX = overlapX,
                            }
                        );
                    }
                }
            }
        }

        public void Clear()
        {
            _boxPool.Clear();
            _boxInds.Clear();
        }
    }

    /// <summary>
    /// Data structure to avoid many reallocs in game state simulation (are cleared every frame)
    /// </summary>
    public class PhysicsContext<TData>
    {
        public Physics<TData> Physics;
        public List<Physics<TData>.Collision> Collisions;
        public Dictionary<(int, int), Physics<TData>.Collision> HurtHitCollisions;

        public PhysicsContext(int maxColliders)
        {
            Physics = new Physics<TData>(maxColliders);
            Collisions = new List<Physics<TData>.Collision>(maxColliders);
            HurtHitCollisions = new Dictionary<(int, int), Physics<TData>.Collision>(maxColliders);
        }

        public void Clear()
        {
            Physics.Clear();
            Collisions.Clear();
            HurtHitCollisions.Clear();
        }
    }
}
