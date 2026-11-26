using UnityEngine;

namespace PiGame.Gameplay
{
    public static class InitialSpawnPlacement
    {
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

        public static bool CanPlace(Body body, Vector3 position) =>
            !HasSolidOverlap(body, position)
            && !IsOccupiedByPlayer(body, position)
            && HasSupport(body, position);

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
