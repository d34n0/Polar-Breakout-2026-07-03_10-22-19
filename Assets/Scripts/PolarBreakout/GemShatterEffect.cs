using System.Collections;
using UnityEngine;

namespace PolarBreakout
{
    /// <summary>
    /// Drives a pre-fractured gem model (see GemBroken.fbx - one child mesh per shard, each
    /// authored around the model's own center) exploding outward and shrinking away. Each piece's
    /// authored local position doubles as its outward direction, since the source model was
    /// fractured around its own origin - no separate direction data needed. Spawned by
    /// BrickBreakEffects alongside its existing particle burst/ripple, same spirit as those:
    /// self-contained, plays once, destroys itself when done.
    /// </summary>
    public class GemShatterEffect : MonoBehaviour
    {
        [Tooltip("How far a piece travels outward (along its own direction from center) over the full duration, world units.")]
        public float explodeDistance = 1.5f;
        [Tooltip("How long the explode/shrink-out takes, seconds.")]
        public float duration = 0.6f;
        [Tooltip("Full turns each piece spins over the duration, for a bit of tumble as it flies out.")]
        public float spinTurns = 1.5f;

        private struct Piece
        {
            public Transform t;
            public Vector3 startLocalPos;
            public Vector3 startLocalScale;
            public Vector3 direction;
            public Vector3 spinAxis;
        }

        private Piece[] _pieces;

        /// <summary>Tints every shard to match the destroyed brick's color (same
        /// MaterialPropertyBlock approach as Brick.SetRenderColor, so no per-piece material
        /// instances get created), then starts the explode/shrink animation.</summary>
        public void Play(Color tint)
        {
            var filters = GetComponentsInChildren<MeshFilter>();
            _pieces = new Piece[filters.Length];

            MaterialPropertyBlock block = null;
            for (int i = 0; i < filters.Length; i++)
            {
                Transform t = filters[i].transform;

                var renderer = filters[i].GetComponent<MeshRenderer>();
                if (renderer != null)
                {
                    block ??= new MaterialPropertyBlock();
                    renderer.GetPropertyBlock(block);
                    block.SetColor("_Color", tint);      // Built-in RP / Unlit
                    block.SetColor("_BaseColor", tint);  // URP Lit/Unlit
                    renderer.SetPropertyBlock(block);
                }

                // Pieces are fractured around the model's own origin, so a piece's own offset
                // from that origin already points "outward" - falls back to a random direction
                // only for the unlikely case of a piece sitting exactly on the origin.
                Vector3 direction = t.localPosition.sqrMagnitude > 0.0001f
                    ? t.localPosition.normalized
                    : Random.onUnitSphere;

                _pieces[i] = new Piece
                {
                    t = t,
                    startLocalPos = t.localPosition,
                    startLocalScale = t.localScale,
                    direction = direction,
                    spinAxis = Random.onUnitSphere
                };
            }

            StartCoroutine(Explode());
        }

        private IEnumerator Explode()
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                // Eases out - a fast initial burst that settles as pieces finish flying/shrinking,
                // rather than a constant-speed drift.
                float eased = 1f - (1f - t) * (1f - t);

                foreach (var piece in _pieces)
                {
                    if (piece.t == null) continue; // piece could've been destroyed some other way
                    piece.t.localPosition = piece.startLocalPos + piece.direction * (explodeDistance * eased);
                    piece.t.localRotation = Quaternion.AngleAxis(spinTurns * 360f * eased, piece.spinAxis) * piece.t.localRotation;
                    piece.t.localScale = piece.startLocalScale * (1f - eased);
                }

                yield return null;
            }

            Destroy(gameObject);
        }
    }
}
