using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace PiGame.Gameplay
{
    internal struct SpawnOptions : INetworkSerializeByMemcpy
    {
        public FixedList64Bytes<Vector3> Positions;
    }

    internal static class InitialSpawnPlacement
    {
        internal const int MaxOptions = 3;
        private const float Skin = 0.025f;
        private const float ProbeAbove = 3f;
        private const float ProbeBelow = 8f;
        private const float HorizontalStep = 0.4f;
        private const int HorizontalSteps = 4;
        private static readonly int TerrainMask = LayerMask.GetMask("Ground", "Wall");
        private static readonly int PlayerMask = LayerMask.GetMask("Player");

        internal readonly struct Body
        {
            internal readonly BoxCollider2D Collider;
            internal readonly Vector2 Size;
            internal readonly Vector2 Offset;

            internal Body(BoxCollider2D collider)
            {
                Collider = collider;
                Vector3 scale = collider.transform.lossyScale;
                Size = Vector2.Scale(collider.size, new Vector2(Mathf.Abs(scale.x), Mathf.Abs(scale.y)));
                Offset = collider.transform.TransformVector(collider.offset);
            }

            internal Rect BoundsAt(Vector3 position) => new((Vector2)position + Offset - Size * 0.5f, Size);
        }

        internal static void CollectOptions(Body body, Vector3 anchor, List<Vector3> positions,
            List<Rect> reserved = null)
        {
            positions.Clear();
            for (int step = -HorizontalSteps; step <= HorizontalSteps; step++)
            {
                Vector3 probe = anchor + Vector3.right * (step * HorizontalStep);
                if (TryFindSupport(body, probe, reserved, out Vector3 position))
                    positions.Add(position);
            }

            positions.Sort((a, b) => (a - anchor).sqrMagnitude.CompareTo((b - anchor).sqrMagnitude));
            if (positions.Count > MaxOptions)
                positions.RemoveRange(MaxOptions, positions.Count - MaxOptions);
        }

        private static bool TryFindSupport(Body body, Vector3 anchor, List<Rect> reserved, out Vector3 position)
        {
            position = anchor;
            Vector2 origin = (Vector2)anchor + new Vector2(body.Offset.x, ProbeAbove);
            float nearest = float.PositiveInfinity;
            foreach (RaycastHit2D hit in Physics2D.RaycastAll(origin, Vector2.down, ProbeAbove + ProbeBelow, TerrainMask))
            {
                if (!IsSupport(hit)) continue;
                Vector3 candidate = anchor;
                candidate.y = hit.point.y + Skin + body.Size.y * 0.5f - body.Offset.y;
                float distance = Mathf.Abs(candidate.y - anchor.y);
                if (distance >= nearest || IsReserved(body, candidate, reserved) || !CanPlace(body, candidate))
                    continue;

                nearest = distance;
                position = candidate;
            }
            return nearest < float.PositiveInfinity;
        }

        private static bool IsReserved(Body body, Vector3 position, List<Rect> reserved)
        {
            if (reserved == null) return false;
            Rect bounds = body.BoundsAt(position);
            foreach (Rect area in reserved)
                if (bounds.Overlaps(area)) return true;
            return false;
        }

        internal static bool CanPlace(Body body, Vector3 position) =>
            !HasSolidOverlap(body, position)
            && !IsOccupiedByPlayer(body, position)
            && HasSupport(body, position);

        internal static bool IsSupportedAndClear(BoxCollider2D collider) =>
            CanPlace(new Body(collider), collider.transform.position);

        internal static bool HasSolidOverlap(Body body, Vector3 position)
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
