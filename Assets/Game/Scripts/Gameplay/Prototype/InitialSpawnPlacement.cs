using UnityEngine;

namespace PiGame.Gameplay
{
    // Consultas locais: a cena e seus colliders precisam existir na máquina do proprietário.
    internal static class InitialSpawnPlacement
    {
        private const float Skin = 0.025f;
        private static readonly int TerrainMask = LayerMask.GetMask("Ground", "Wall");

        internal static bool TryResolve(BoxCollider2D body, Vector3 anchor, out Vector3 position)
        {
            position = anchor;
            Vector2 size = WorldSize(body);
            Vector2 offset = body.transform.TransformVector(body.offset);
            Vector2 origin = (Vector2)anchor + new Vector2(offset.x, 2f);
            float nearest = float.PositiveInfinity;
            foreach (RaycastHit2D hit in Physics2D.RaycastAll(origin, Vector2.down, 10f, TerrainMask))
            {
                if (!IsSupport(hit)) continue;
                Vector3 candidate = anchor;
                candidate.y = hit.point.y + Skin + size.y * 0.5f - offset.y;
                float distance = Mathf.Abs(candidate.y - anchor.y);
                if (distance >= nearest || HasSolidOverlap(body, candidate)) continue;
                if (!HasSupport(body, candidate)) continue;
                nearest = distance;
                position = candidate;
            }
            return nearest < float.PositiveInfinity;
        }

        internal static bool IsSupportedAndClear(BoxCollider2D body)
        {
            return !HasSolidOverlap(body, body.transform.position)
                && HasSupport(body, body.transform.position);
        }

        private static Vector2 WorldSize(BoxCollider2D body)
        {
            Vector3 scale = body.transform.lossyScale;
            return Vector2.Scale(body.size, new Vector2(Mathf.Abs(scale.x), Mathf.Abs(scale.y)));
        }

        private static bool HasSolidOverlap(BoxCollider2D body, Vector3 position)
        {
            Vector2 center = (Vector2)position + (Vector2)body.transform.TransformVector(body.offset);
            Vector2 size = WorldSize(body) - Vector2.one * 0.02f;
            foreach (Collider2D other in Physics2D.OverlapBoxAll(center, size, 0f, TerrainMask))
                if (other != body && !other.isTrigger && !other.usedByEffector) return true;
            return false;
        }

        private static bool HasSupport(BoxCollider2D body, Vector3 position)
        {
            Vector2 size = WorldSize(body);
            Vector2 center = (Vector2)position + (Vector2)body.transform.TransformVector(body.offset);
            Vector2 origin = center + Vector2.down * (size.y * 0.5f - 0.02f);
            foreach (RaycastHit2D hit in Physics2D.BoxCastAll(origin,
                         new Vector2(size.x * 0.8f, 0.01f), 0f, Vector2.down, 0.1f, TerrainMask))
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
