using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace PiGame.Gameplay
{
    public struct SpawnOptions : INetworkSerializeByMemcpy
    {
        public FixedList64Bytes<Vector3> Positions;
    }

    public static class InitialSpawnPlacement
    {
        public const int MaxOptions = 3;
        private const float Skin = 0.025f;
        private const float ProbeAbove = 3f;
        private const float ProbeBelow = 8f;
        private const float HorizontalStep = 0.4f;
        private const int HorizontalSteps = 4;
        private const int NearbyFallbackSteps = 12;
        private static readonly int TerrainMask = LayerMask.GetMask("Ground", "Wall");
        private static readonly int PlayerMask = LayerMask.GetMask("Player");

        public readonly struct Body
        {
            public readonly BoxCollider2D Collider;
            public readonly Vector2 Size;
            public readonly Vector2 Offset;

            public Body(BoxCollider2D collider)
            {
                Collider = collider;
                Vector3 scale = collider.transform.lossyScale;
                Size = Vector2.Scale(collider.size, new Vector2(Mathf.Abs(scale.x), Mathf.Abs(scale.y)));
                Offset = collider.transform.TransformVector(collider.offset);
            }

            public Rect BoundsAt(Vector3 position) => new((Vector2)position + Offset - Size * 0.5f, Size);
        }

        public static void CollectOptions(Body body, Vector3 anchor, List<Vector3> positions,
            List<Rect> reserved = null, Rect? arena = null, bool allowNearbyFallback = false)
        {
            positions.Clear();
            for (int step = -HorizontalSteps; step <= HorizontalSteps; step++)
            {
                Vector3 probe = anchor + Vector3.right * (step * HorizontalStep);
                if (TryFindSupport(body, probe, reserved, arena, out Vector3 position))
                    positions.Add(position);
            }

            if (allowNearbyFallback)
            {
                for (int step = HorizontalSteps + 1; step <= NearbyFallbackSteps && positions.Count < MaxOptions; step++)
                {
                    if (TryFindSupport(body, anchor - Vector3.right * (step * HorizontalStep), reserved, arena, out Vector3 left))
                        positions.Add(left);
                    if (TryFindSupport(body, anchor + Vector3.right * (step * HorizontalStep), reserved, arena, out Vector3 right))
                        positions.Add(right);
                }
            }

            positions.Sort((a, b) => (a - anchor).sqrMagnitude.CompareTo((b - anchor).sqrMagnitude));
            if (positions.Count > MaxOptions)
                positions.RemoveRange(MaxOptions, positions.Count - MaxOptions);
        }

        private static bool TryFindSupport(Body body, Vector3 anchor, List<Rect> reserved, Rect? arena,
            out Vector3 position)
        {
            position = anchor;
            float nearest = float.PositiveInfinity;
            // Start below low ceilings that can hide the floor on the same tilemap collider.
            Vector2 origin = (Vector2)anchor + new Vector2(body.Offset.x, 0f);
            ConsiderSupport(body, anchor, reserved, arena, origin, ProbeBelow, ref nearest, ref position);
            ConsiderSupport(body, anchor, reserved, arena, origin + Vector2.up * ProbeAbove,
                ProbeAbove + ProbeBelow, ref nearest, ref position);
            return nearest < float.PositiveInfinity;
        }

        private static void ConsiderSupport(Body body, Vector3 anchor, List<Rect> reserved, Rect? arena,
            Vector2 origin, float probeDistance, ref float nearest, ref Vector3 position)
        {
            foreach (RaycastHit2D hit in Physics2D.RaycastAll(origin, Vector2.down, probeDistance, TerrainMask))
            {
                if (!IsSupport(hit)) continue;
                Vector3 candidate = anchor;
                candidate.y = hit.point.y + Skin + body.Size.y * 0.5f - body.Offset.y;
                float distance = Mathf.Abs(candidate.y - anchor.y);
                if (distance >= nearest || IsReserved(body, candidate, reserved)
                    || (arena.HasValue && !IsInsideArena(body.BoundsAt(candidate), arena.Value))
                    || !CanPlace(body, candidate))
                    continue;

                nearest = distance;
                position = candidate;
            }
        }

        private static bool IsInsideArena(Rect body, Rect arena) =>
            body.xMin >= arena.xMin && body.xMax <= arena.xMax
            && body.yMin >= arena.yMin && body.yMax <= arena.yMax;

        private static bool IsReserved(Body body, Vector3 position, List<Rect> reserved)
        {
            if (reserved == null) return false;
            Rect bounds = body.BoundsAt(position);
            foreach (Rect area in reserved)
                if (bounds.Overlaps(area)) return true;
            return false;
        }

        public static bool CanPlace(Body body, Vector3 position) =>
            !HasSolidOverlap(body, position)
            && !IsOccupiedByPlayer(body, position)
            && HasSupport(body, position);

        public static bool IsSupportedAndClear(BoxCollider2D collider) =>
            CanPlace(new Body(collider), collider.transform.position);

        private static bool HasSolidOverlap(Body body, Vector3 position)
        {
            Vector2 center = (Vector2)position + body.Offset;
            Vector2 size = body.Size - Vector2.one * 0.02f;
            int collisionMask = TerrainMask | (Physics2D.GetLayerCollisionMask(body.Collider.gameObject.layer) & ~PlayerMask);
            foreach (Collider2D other in Physics2D.OverlapBoxAll(center, size, 0f, collisionMask))
                if (!other.isTrigger && !other.transform.IsChildOf(body.Collider.transform)) return true;
            return false;
        }

        private static bool IsOccupiedByPlayer(Body body, Vector3 position)
        {
            Vector2 center = (Vector2)position + body.Offset;
            foreach (Collider2D other in Physics2D.OverlapBoxAll(center, body.Size, 0f, PlayerMask))
                if (!other.isTrigger && !other.transform.IsChildOf(body.Collider.transform)
                    && other.GetComponentInParent<NetworkPlayerState>() is { IsAlive: true }) return true;
            return false;
        }

        private static bool HasSupport(Body body, Vector3 position)
        {
            Vector2 center = (Vector2)position + body.Offset;
            Vector2 origin = center + Vector2.down * (body.Size.y * 0.5f - 0.02f);
            foreach (RaycastHit2D hit in Physics2D.BoxCastAll(origin,
                         new Vector2(body.Size.x * 0.8f, 0.01f), 0f, Vector2.down, 0.1f, TerrainMask))
                if (IsSupport(hit)) return true;
            return false;
        }

        private static bool IsSupport(RaycastHit2D hit)
        {
            // Uma consulta que já começa dentro do collider não comprova apoio.
            return hit.collider != null && !hit.collider.isTrigger
                && hit.distance > 0.0001f && hit.normal.y >= 0.8f;
        }
    }
}
