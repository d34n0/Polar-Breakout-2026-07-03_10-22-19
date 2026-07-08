using UnityEngine;

namespace PolarBreakout
{
    /// <summary>
    /// Spawned by BrickTypeSO.TryDropPowerUp at a destroyed brick's position (any brick tier can
    /// be configured to drop one via its powerUpDropChance/powerUpType fields). Falls toward the arena
    /// center (same "gravitate inward" idea as everything else in this polar arena) while
    /// swaying side to side in a slow sine wave, and grants its ability if the paddle catches
    /// it along the way. The wobble means a straight radial line from spawn point to center no
    /// longer guarantees a catch - the player has to actually track it, and can miss it. Uses
    /// plain distance/angle math against the paddle rather than a physics collider, matching
    /// how BallController already checks the outer wall/death zone manually instead of via
    /// physics triggers.
    /// </summary>
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    public class PowerUpCapsule : MonoBehaviour
    {
        [Tooltip("How fast the capsule falls toward the arena center, world units/second.")]
        public float fallSpeed = 2f;

        [Header("Capsule Shape")]
        [Tooltip("Length of the capsule's straight middle section, world units - excludes the " +
                 "two rounded end caps. Set to 0 for a plain circular pill.")]
        public float capsuleLength = 0.22f;
        [Tooltip("Radius of the capsule's rounded ends, world units - this is also half its width.")]
        public float capsuleRadius = 0.12f;
        [Tooltip("How many segments approximate each rounded end - higher looks smoother.")]
        public int capEndSegments = 8;

        [Header("Wobble")]
        [Tooltip("How far the capsule sways either side of its straight-line path, in degrees.")]
        public float wobbleAmplitudeDegrees = 12f;
        [Tooltip("How fast the sway oscillates, radians/second. Lower = slower, lazier drift.")]
        public float wobbleSpeed = 1.5f;

        public PowerUpType Type { get; private set; }

        /// <summary>Fired the instant any capsule (of any type) is actually caught by the paddle -
        /// ScoreManager listens for this to award its capsule bonus, decoupled from needing a
        /// direct reference to every capsule instance (they're spawned dynamically at runtime).</summary>
        public static event System.Action OnAnyCapsuleCollected;

        private PaddleController _paddle;
        private AudioManager _audioManager;
        private float _spawnAngleDegrees;
        private float _currentAngleDegrees;
        private float _radius;
        private float _elapsedTime;

        private void Awake()
        {
            _paddle = FindFirstObjectByType<PaddleController>();
            _audioManager = FindFirstObjectByType<AudioManager>();
        }

        public void Initialize(Vector2 spawnWorldPosition, PowerUpType type)
        {
            Type = type;
            _radius = spawnWorldPosition.magnitude;
            _spawnAngleDegrees = Mathf.Atan2(spawnWorldPosition.y, spawnWorldPosition.x) * Mathf.Rad2Deg;

            BuildVisual();
            UpdateTransform();
        }

        private void BuildVisual()
        {
            GetComponent<MeshFilter>().mesh = BuildCapsuleMesh(capsuleLength, capsuleRadius, capEndSegments);

            var renderer = GetComponent<MeshRenderer>();
            var materialOverride = GetMaterialOverride(Type);
            if (materialOverride != null)
            {
                // A custom material presumably already has its own authored look, so it's used
                // as-is rather than also having the hardcoded per-type property-block tint below
                // forced on top of it.
                renderer.sharedMaterial = materialOverride;
                return;
            }

            // Bricks get away without ever assigning a Material because Brick.prefab already has
            // one serialized on its MeshRenderer from being set up in the Editor. This capsule is
            // built entirely at runtime via AddComponent<MeshRenderer>(), which - unlike adding
            // the component through the Inspector - never auto-assigns a material, so without
            // this line the renderer has nothing to draw at all.
            renderer.sharedMaterial = PolarMeshUtility.GetProceduralUnlitMaterial();

            var propBlock = new MaterialPropertyBlock();
            Color color = GetDefaultColor(Type);
            propBlock.SetColor("_Color", color);
            propBlock.SetColor("_BaseColor", color);
            renderer.SetPropertyBlock(propBlock);
        }

        /// <summary>Reads the matching per-type material override off PaddleAbilities (the
        /// already-existing central home for power-up configuration), if the paddle and that
        /// component can be found and a material was actually assigned there.</summary>
        private Material GetMaterialOverride(PowerUpType type)
        {
            var abilities = _paddle != null ? _paddle.GetComponent<PaddleAbilities>() : null;
            if (abilities == null) return null;

            switch (type)
            {
                case PowerUpType.Multiball: return abilities.multiballCapsuleMaterial;
                case PowerUpType.Autopilot: return abilities.autopilotCapsuleMaterial;
                case PowerUpType.Cannon: return abilities.cannonCapsuleMaterial;
                default: return null;
            }
        }

        /// <summary>Builds a "stadium" shape (two rounded end caps joined by a straight middle
        /// section) lying along local +X, matching the same angle-0-is-+X convention Bullet.cs
        /// uses so this can be rotated to face its direction of travel the same way. Triangulated
        /// as a fan from the origin, which is valid since a stadium is always convex.</summary>
        private static Mesh BuildCapsuleMesh(float length, float radius, int segmentsPerCap)
        {
            segmentsPerCap = Mathf.Max(2, segmentsPerCap);
            radius = Mathf.Max(0.01f, radius);
            float halfBodyLength = Mathf.Max(0f, length) / 2f;

            int perimeterCount = (segmentsPerCap + 1) * 2;
            var vertices = new Vector3[perimeterCount + 1];
            var uvs = new Vector2[vertices.Length];
            int centerIndex = perimeterCount;
            vertices[centerIndex] = Vector3.zero;
            uvs[centerIndex] = new Vector2(0.5f, 0.5f);

            float halfExtent = halfBodyLength + radius;
            int vi = 0;

            // Right cap: -90 -> +90 degrees around (halfBodyLength, 0).
            for (int i = 0; i <= segmentsPerCap; i++)
            {
                float angle = Mathf.Lerp(-90f, 90f, (float)i / segmentsPerCap) * Mathf.Deg2Rad;
                float x = halfBodyLength + radius * Mathf.Cos(angle);
                float y = radius * Mathf.Sin(angle);
                vertices[vi] = new Vector3(x, y, 0f);
                uvs[vi] = new Vector2(x / halfExtent * 0.5f + 0.5f, y / radius * 0.5f + 0.5f);
                vi++;
            }

            // Left cap: +90 -> +270 degrees around (-halfBodyLength, 0) - continues the same
            // counter-clockwise sweep around the perimeter.
            for (int i = 0; i <= segmentsPerCap; i++)
            {
                float angle = Mathf.Lerp(90f, 270f, (float)i / segmentsPerCap) * Mathf.Deg2Rad;
                float x = -halfBodyLength + radius * Mathf.Cos(angle);
                float y = radius * Mathf.Sin(angle);
                vertices[vi] = new Vector3(x, y, 0f);
                uvs[vi] = new Vector2(x / halfExtent * 0.5f + 0.5f, y / radius * 0.5f + 0.5f);
                vi++;
            }

            var triangles = new int[perimeterCount * 3];
            int ti = 0;
            for (int i = 0; i < perimeterCount; i++)
            {
                int next = (i + 1) % perimeterCount;
                // (center, next, i) rather than (center, i, next) - matches the -Z-facing
                // winding PolarMeshUtility uses elsewhere in this project for a CCW perimeter.
                triangles[ti++] = centerIndex;
                triangles[ti++] = next;
                triangles[ti++] = i;
            }

            var mesh = new Mesh { name = "PowerUpCapsule" };
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Color GetDefaultColor(PowerUpType type)
        {
            switch (type)
            {
                case PowerUpType.Multiball: return new Color(0.2f, 0.8f, 1f);
                case PowerUpType.Autopilot: return new Color(1f, 0.85f, 0.1f);
                case PowerUpType.Cannon: return new Color(1f, 0.3f, 0.2f);
                default: return Color.white;
            }
        }

        private void Update()
        {
            if (_paddle == null)
            {
                Destroy(gameObject);
                return;
            }

            _elapsedTime += Time.deltaTime;
            _radius -= fallSpeed * Time.deltaTime;
            UpdateTransform();

            if (_radius <= _paddle.settings.deathZoneRadius)
            {
                Destroy(gameObject);
                return;
            }

            if (IsCaughtByPaddle())
            {
                var abilities = _paddle.GetComponent<PaddleAbilities>();
                if (abilities != null) abilities.CollectPowerUp(Type);
                _audioManager?.PlayCapsulePickup();
                OnAnyCapsuleCollected?.Invoke();
                Destroy(gameObject);
            }
        }

        private void UpdateTransform()
        {
            // Sine wave starts at zero offset (elapsedTime == 0 at spawn), so a capsule spawned
            // directly on the paddle is still caught immediately rather than the wobble
            // spuriously nudging it out of catch range on the very first frame.
            float wobbleDegrees = Mathf.Sin(_elapsedTime * wobbleSpeed) * wobbleAmplitudeDegrees;
            _currentAngleDegrees = _spawnAngleDegrees + wobbleDegrees;

            float rad = _currentAngleDegrees * Mathf.Deg2Rad;
            transform.position = new Vector3(Mathf.Cos(rad) * _radius, Mathf.Sin(rad) * _radius, 0f);
            // Orients the capsule's long axis along its current radial line, so it reads as
            // falling in along its own length rather than always facing a fixed direction.
            transform.rotation = Quaternion.Euler(0f, 0f, _currentAngleDegrees -90f);
        }

        private bool IsCaughtByPaddle()
        {
            float paddleOuterRadius = _paddle.settings.paddleOrbitRadius + _paddle.radialThickness / 2f;
            float paddleInnerRadius = _paddle.settings.paddleOrbitRadius - _paddle.radialThickness / 2f;
            if (_radius > paddleOuterRadius || _radius < paddleInnerRadius) return false;

            // Checked against the wobbled angle, not the original spawn angle - what the player
            // sees on screen is what has to line up with the paddle.
            float angleDelta = Mathf.Abs(Mathf.DeltaAngle(_currentAngleDegrees, _paddle.CurrentAngleDegrees));
            return angleDelta <= _paddle.angularWidthDegrees / 2f;
        }
    }
}
