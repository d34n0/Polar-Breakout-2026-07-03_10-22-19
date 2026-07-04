using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PolarBreakout
{
    /// <summary>
    /// Owns all paddle-ability state (Multiball, Autopilot, Cannon) granted by collected
    /// power-up capsules. Lives alongside PaddleController on the same GameObject.
    /// </summary>
    [RequireComponent(typeof(PaddleController))]
    public class PaddleAbilities : MonoBehaviour
    {
        [Header("References")]
        public BallManager ballManager;

        [Header("Cannon")]
        [Tooltip("How many shots (button presses) a Cannon pickup grants - each shot fires both " +
                 "barrels at once, so total bullets fired is double this.")]
        public int cannonAmmoPerPickup = 5;
        public float bulletSpeed = 12f;
        [Tooltip("Angular offset (degrees) of each cannon barrel from the paddle's center, " +
                 "mirrored left/right - also where each barrel's bullet fires from/toward. " +
                 "Keep at or below half the paddle's angularWidthDegrees so the barrels stay " +
                 "anchored on the paddle's surface instead of floating past its tips.")]
        public float cannonOffsetDegrees = 12f;
        [Tooltip("How far each cannon barrel's visual sticks out beyond the paddle's outer edge, world units.")]
        public float cannonBarrelLength = 0.18f;
        [Tooltip("Half-width of each cannon barrel's visual, world units.")]
        public float cannonBarrelWidth = 0.05f;
        [Tooltip("How long the grow-out-of-the-paddle reveal animation takes when a Cannon " +
                 "capsule is collected, seconds.")]
        public float cannonRevealDuration = 0.25f;

        [Header("Autopilot")]
        public float autopilotDuration = 5f;

        public int CannonAmmo => _cannonAmmo;
        public bool IsAutopilotActive => _autopilotTimeRemaining > 0f;

        private PaddleController _paddle;
        private int _cannonAmmo;
        private float _autopilotTimeRemaining;
        private bool _firePressed;
        private GameObject _cannonLeft;
        private GameObject _cannonRight;
        private Coroutine _cannonRevealCoroutine;

        private void Awake()
        {
            _paddle = GetComponent<PaddleController>();
            _cannonLeft = BuildCannonVisual(-cannonOffsetDegrees, "Left");
            _cannonRight = BuildCannonVisual(cannonOffsetDegrees, "Right");
        }

        private void Update()
        {
            // Same latch-in-Update/consume-in-FixedUpdate pattern BallController uses for the
            // same button, for the same reason (wasPressedThisFrame isn't reliably readable
            // from FixedUpdate directly).
            if (Gamepad.current != null && Gamepad.current.buttonSouth.wasPressedThisFrame)
                _firePressed = true;
        }

        private void FixedUpdate()
        {
            UpdateAutopilot();

            bool firePressed = _firePressed;
            _firePressed = false;

            // A launches the ball while docked (handled entirely inside BallController) and
            // fires the cannon once launched - the two never actually compete for the same
            // press since BallController only acts on it while Docked.
            bool ballInPlay = ballManager != null && ballManager.primaryBall != null
                && ballManager.primaryBall.State == BallState.Launched;
            if (firePressed && _cannonAmmo > 0 && ballInPlay)
            {
                FireCannon();
                _cannonAmmo--;
                if (_cannonAmmo <= 0) SetCannonVisualsActive(false);
            }
        }

        public void CollectPowerUp(PowerUpType type)
        {
            switch (type)
            {
                case PowerUpType.Multiball:
                    if (ballManager != null) ballManager.ActivateMultiball();
                    break;
                case PowerUpType.Autopilot:
                    _autopilotTimeRemaining = autopilotDuration;
                    break;
                case PowerUpType.Cannon:
                    _cannonAmmo = cannonAmmoPerPickup;
                    if (_cannonRevealCoroutine != null) StopCoroutine(_cannonRevealCoroutine);
                    _cannonRevealCoroutine = StartCoroutine(RevealCannons());
                    break;
            }
        }

        /// <summary>Called by BallManager the instant every ball has been lost - abilities
        /// don't survive losing the ball, and any bullets already in flight are destroyed
        /// immediately rather than being left to finish their flight.</summary>
        public void ResetAbilities()
        {
            _cannonAmmo = 0;
            _autopilotTimeRemaining = 0f;
            _paddle.AutopilotOverrideAngleDegrees = null;
            SetCannonVisualsActive(false);

            foreach (var bullet in Object.FindObjectsByType<Bullet>(FindObjectsSortMode.None))
                Destroy(bullet.gameObject);
        }

        /// <summary>Instantly hides both barrels (used when ammo runs out or the ability is
        /// reset on death) - the grow-out reveal only plays on collection, via RevealCannons.</summary>
        private void SetCannonVisualsActive(bool active)
        {
            if (_cannonRevealCoroutine != null)
            {
                StopCoroutine(_cannonRevealCoroutine);
                _cannonRevealCoroutine = null;
            }

            _cannonLeft.SetActive(active);
            _cannonRight.SetActive(active);
            if (!active)
            {
                // Reset to zero-grown so the next pickup's reveal starts from scratch instead of
                // popping to whatever scale was left over from an interrupted animation.
                _cannonLeft.transform.localScale = new Vector3(0f, 1f, 1f);
                _cannonRight.transform.localScale = new Vector3(0f, 1f, 1f);
            }
        }

        /// <summary>Animates both barrels scaling up from nothing along their own length (local
        /// +X, the same axis BuildBarrelMesh extends along from the paddle's surface outward),
        /// so they visibly grow out of the paddle rather than just popping into existence.</summary>
        private IEnumerator RevealCannons()
        {
            // Re-synced on every reveal (not just once at Awake) so the barrels always match
            // whatever material/shader is currently on the paddle - handy while iterating on the
            // paddle's look, since a freshly caught Cannon capsule always picks up the latest one.
            RefreshCannonMaterial();
            _cannonLeft.SetActive(true);
            _cannonRight.SetActive(true);

            float elapsed = 0f;
            float duration = Mathf.Max(0.0001f, cannonRevealDuration);
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                SetCannonGrowth(Mathf.Clamp01(elapsed / duration));
                yield return null;
            }
            SetCannonGrowth(1f);
            _cannonRevealCoroutine = null;
        }

        private void SetCannonGrowth(float t)
        {
            _cannonLeft.transform.localScale = new Vector3(t, 1f, 1f);
            _cannonRight.transform.localScale = new Vector3(t, 1f, 1f);
        }

        /// <summary>Copies whatever material is currently on the paddle's own MeshRenderer onto
        /// both barrels, so they always render with the same shader/look as the paddle rather
        /// than a fixed placeholder color - useful while still iterating on the paddle's final
        /// material rather than needing to keep two places in sync by hand.</summary>
        private void RefreshCannonMaterial()
        {
            var paddleMaterial = _paddle.GetComponent<MeshRenderer>().sharedMaterial;
            _cannonLeft.GetComponent<MeshRenderer>().sharedMaterial = paddleMaterial;
            _cannonRight.GetComponent<MeshRenderer>().sharedMaterial = paddleMaterial;
        }

        private void UpdateAutopilot()
        {
            if (_autopilotTimeRemaining <= 0f) return;

            _autopilotTimeRemaining -= Time.fixedDeltaTime;
            if (_autopilotTimeRemaining > 0f && ballManager != null)
                _paddle.AutopilotOverrideAngleDegrees = ballManager.GetNearestBallAngleDegrees(_paddle.CurrentAngleDegrees);
            else
                _paddle.AutopilotOverrideAngleDegrees = null;
        }

        /// <summary>Fires both cannon barrels at once - one bullet per barrel, each flying
        /// outward along its own offset angle rather than both firing straight out from the
        /// paddle's center.</summary>
        private void FireCannon()
        {
            FireBarrel(-cannonOffsetDegrees);
            FireBarrel(cannonOffsetDegrees);
        }

        private void FireBarrel(float offsetDegrees)
        {
            float fireAngleDegrees = _paddle.CurrentAngleDegrees + offsetDegrees;
            float spawnRadius = _paddle.settings.paddleOrbitRadius + _paddle.radialThickness / 2f + 0.3f;
            float rad = fireAngleDegrees * Mathf.Deg2Rad;
            Vector2 spawnPos = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * spawnRadius;

            var bulletObject = new GameObject("Bullet");
            var bullet = bulletObject.AddComponent<Bullet>();
            bullet.Launch(spawnPos, fireAngleDegrees, bulletSpeed, _paddle.settings);
        }

        /// <summary>Builds one cannon barrel as a small child quad sticking radially outward
        /// from the paddle's outer edge at a fixed angular offset from center - parented under
        /// the paddle (built in the same local, angle-0-centered space as the paddle's own mesh)
        /// so it rotates along with the paddle for free via the parent transform, matching how
        /// PaddleController's own mesh is built. Starts hidden; CollectPowerUp/ResetAbilities
        /// toggle it via SetCannonVisualsActive.</summary>
        private GameObject BuildCannonVisual(float offsetDegrees, string label)
        {
            var go = new GameObject($"CannonBarrel_{label}");
            go.transform.SetParent(transform, worldPositionStays: false);

            float radius = _paddle.settings.paddleOrbitRadius + _paddle.radialThickness / 2f;
            float rad = offsetDegrees * Mathf.Deg2Rad;
            go.transform.localPosition = new Vector3(Mathf.Cos(rad), Mathf.Sin(rad), 0f) * radius;
            go.transform.localRotation = Quaternion.Euler(0f, 0f, offsetDegrees);

            var meshFilter = go.AddComponent<MeshFilter>();
            meshFilter.mesh = BuildBarrelMesh();

            // Best-effort default - RefreshCannonMaterial() re-syncs this to whatever the paddle
            // currently has every time the barrels are actually revealed, so a stale material
            // picked up here (e.g. if this runs before PaddleController.Awake() has applied its
            // own materialOverride) never actually reaches the player.
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _paddle.GetComponent<MeshRenderer>().sharedMaterial;

            go.transform.localScale = new Vector3(0f, 1f, 1f);
            go.SetActive(false);
            return go;
        }

        private Mesh BuildBarrelMesh()
        {
            var mesh = new Mesh { name = "CannonBarrel" };
            mesh.vertices = new Vector3[]
            {
                new Vector3(0f, -cannonBarrelWidth, 0f),
                new Vector3(cannonBarrelLength, -cannonBarrelWidth, 0f),
                new Vector3(cannonBarrelLength, cannonBarrelWidth, 0f),
                new Vector3(0f, cannonBarrelWidth, 0f),
            };
            mesh.uv = new Vector2[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
            // Same -Z-facing winding convention as Bullet.cs/PolarMeshUtility.
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.RecalculateNormals();
            // Needed since the barrels can end up sharing the paddle's material (see
            // RefreshCannonMaterial) - if that's a shader reading a tangent-space normal map (e.g.
            // the gemstone brick shader), a mesh with no tangents renders that lighting as garbage
            // rather than just flat, matching why PolarMeshUtility.BuildArcSegmentMesh does the same.
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
