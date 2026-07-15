using System.Collections;
using System.Collections.Generic;
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
        [Tooltip("Optional. Plays AudioManager.bulletSound once per barrel fired and " +
                 "AudioManager.laserSound once per beam fired. Leave unset for silence.")]
        public AudioManager audioManager;

        [Header("Cannon")]
        [Tooltip("How many shots (button presses) a Cannon pickup grants - each shot fires both " +
                 "barrels at once, so total bullets fired is double this.")]
        public int cannonAmmoPerPickup = 5;
        public float bulletSpeed = 12f;
        [Tooltip("How far apart (world units) the two cannon barrels sit, offset sideways from " +
                 "the paddle's center - each barrel's bullet also spawns from that same offset " +
                 "position. Both barrels point straight out, parallel to each other and to the " +
                 "paddle's own facing direction, rather than fanning outward at an angle.")]
        public float turretSpacing = 0.5f;
        [Tooltip("How long the grow-out-of-the-paddle reveal animation takes when a Cannon " +
                 "capsule is collected, seconds.")]
        public float cannonRevealDuration = 0.25f;
        [Tooltip("Optional. Overrides a fired bullet's default procedural material. Leave unset " +
                 "to use the shared default.")]
        public Material bulletMaterial;
        [Tooltip("Angle (degrees) between adjacent bullets when a barrel fires more than one per " +
                 "shot (see ModifierType.ExtraBulletsPerBarrel) - extras fan out symmetrically " +
                 "around the barrel's own aim direction (averaged/centered on it) rather than " +
                 "all firing dead ahead.")]
        public float bulletSpreadDegrees = 10f;

        [Header("Laser Beam (Laser Cannon legendary)")]
        [Tooltip("Full width (world units) of the beam each turret fires while the Laser Cannon " +
                 "card is active, before any Split the Atom widening below.")]
        public float laserBeamBaseWidth = 0.3f;
        [Tooltip("Extra width (world units) added per stack of ExtraBulletsPerBarrel (i.e. per " +
                 "Split the Atom card also equipped) - the combined firepower widens the single " +
                 "beam instead of also fanning out separate bullets.")]
        public float laserBeamWidthPerExtraBullet = 0.25f;
        [Tooltip("How long each beam stays active (visible and damaging) per shot, seconds.")]
        public float laserBeamDuration = 0.15f;
        [Tooltip("Optional. Overrides the beam's default procedural material/color - the hook " +
                 "for a future \"laser skin\" to give it a custom shader/texture. Leave unset to " +
                 "use the plain default beam color.")]
        public Material laserBeamMaterial;

        [Header("Turret Skin")]
        [Tooltip("Selectable turret sprites - the currently chosen one (CosmeticsManager." +
                 "TurretSkinIndex) is applied to both barrels and re-applied live whenever the " +
                 "player changes turret skin. Turrets are purely cosmetic (no collider), so " +
                 "unlike the paddle itself they render as sprites rather than a procedural mesh.")]
        public TurretSkin[] availableTurretSkins;

        [Header("Autopilot")]
        public float autopilotDuration = 5f;

        [Header("Drone")]
        [Tooltip("How long a Drone power-up lasts before its drone(s) phase out, seconds. " +
                 "Catching another Drone capsule while one is already active refreshes this back " +
                 "to full rather than adding to it (same convention as Autopilot).")]
        public float droneDuration = 10f;
        [Tooltip("Optional. The drone's own sprite. Leave unset to use a small generated circle " +
                 "sprite instead, so the drone is always visible even before real art exists.")]
        public Sprite droneSprite;
        [Tooltip("Tint applied to droneSprite (or the generated fallback circle).")]
        public Color droneTintColor = new Color(0.6f, 1f, 0.7f);
        [Tooltip("Rendered radius of the drone, world units.")]
        public float droneVisualRadius = 0.15f;
        [Tooltip("How far outside the paddle's own orbit radius the drone hovers, world units - " +
                 "always added (never subtracted), so the drone can never drift inward toward " +
                 "the black hole regardless of its wobble below.")]
        public float droneHoverRadiusOffset = 0.5f;
        [Tooltip("How far either side of its resting angle (degrees) the drone's hover wobbles.")]
        public float droneHoverWobbleAmplitudeDegrees = 20f;
        [Tooltip("How fast the hover wobble oscillates, radians/second.")]
        public float droneHoverWobbleSpeed = 2f;
        [Tooltip("How fast (world units/second) the drone chases its current target hover spot - " +
                 "higher reads as more tightly leashed to the paddle, lower as lazier/floatier.")]
        public float droneHoverFollowSpeed = 6f;
        [Tooltip("Seconds between the drone's shots at the nearest brick (or boss, if no bricks " +
                 "remain) - see ModifierType.DroneFireRateMultiplier for a Card that speeds this up.")]
        public float droneFireInterval = 0.7f;
        public float droneBulletSpeed = 9f;
        [Tooltip("Optional. Overrides a drone-fired bullet's default procedural material.")]
        public Material droneBulletMaterial;
        [Tooltip("How long the drone's dissolve-out phase-out takes once its duration runs out, " +
                 "seconds.")]
        public float dronePhaseOutDuration = 0.4f;
        [Tooltip("The Dissolve shader graph material (e.g. Assets/Shaders/Dissolve.mat) used for " +
                 "the drone's phase-out - see DissolveEffect.dissolveMaterial. Leave unset and " +
                 "the drone just sits still for dronePhaseOutDuration with no fade before " +
                 "vanishing, rather than visibly dissolving away.")]
        public Material droneDissolveMaterial;

        [Header("Twin Paddle")]
        [Tooltip("Optional. A second PaddleController instance, pre-built in the scene (inactive " +
                 "by default) with its mirrorSource already pointed at this paddle - activated/" +
                 "deactivated automatically to match ModifierType.TwinPaddleEnabled (see " +
                 "RefreshTwinPaddle). Leave unset to omit the mechanic entirely.")]
        public PaddleController twinPaddle;

        [Header("Run Modifiers")]
        [Tooltip("Optional. When set, cannon ammo/bullet speed/turret spacing/autopilot duration " +
                 "are boosted by any Cards acquired this run. Leave unset to use all four exactly " +
                 "as configured, unaffected by the card system.")]
        public RunModifiers runModifiers;

        [Header("Power-Up Capsule Materials")]
        [Tooltip("Optional per-type material overrides for power-up capsules, read by " +
                 "PowerUpCapsule at spawn time - leave any of these unset to use the shared " +
                 "default procedural material (with that type's default color) instead.")]
        public Material multiballCapsuleMaterial;
        public Material autopilotCapsuleMaterial;
        public Material cannonCapsuleMaterial;
        public Material droneCapsuleMaterial;

        [Header("Power-Up Focus Effect")]
        [Tooltip("Optional. The main arena camera - when set, catching a Cannon or Drone capsule " +
                 "pauses the game and zooms this camera in on the paddle (see " +
                 "PlayPowerUpFocusSequence) before applying the power-up, holds there until " +
                 "RevealCannons (Cannon) or focusHoldDuration (Drone, which has no bespoke " +
                 "reveal of its own yet) finishes, then zooms back out and resumes. A Cannon/" +
                 "Drone capsule caught while that same ability is already active just refreshes " +
                 "it in place with no camera effect - only the very first activation gets the " +
                 "cinematic. Multiball and Autopilot always apply instantly with no camera " +
                 "effect, regardless of this setting. Leave unset to apply every type instantly, " +
                 "with no camera effect at all, exactly as before - every existing isolated test " +
                 "relies on this default.")]
        public Camera focusCamera;
        [Tooltip("Optional. Disabled for the duration of the focus effect so it doesn't fight " +
                 "the zoom's own orthographicSize/position animation, then re-enabled once the " +
                 "camera is back to its original framing. Leave unset if the scene doesn't use " +
                 "CustomCam.")]
        public CustomCam customCam;
        [Tooltip("Orthographic size the camera zooms in to for the reveal - smaller is tighter. Only used when focusCamera is orthographic (see focusFieldOfView for a perspective focusCamera).")]
        public float focusOrthographicSize = 3f;
        [Tooltip("Field of view the camera zooms in to for the reveal when focusCamera is perspective - smaller is tighter. Ignored for an orthographic focusCamera.")]
        public float focusFieldOfView = 20f;
        [Tooltip("How long the zoom in on the paddle takes, seconds.")]
        public float focusZoomInDuration = 0.3f;
        [Tooltip("How long the zoom back out to the camera's original framing takes, seconds.")]
        public float focusZoomOutDuration = 0.3f;
        [Tooltip("Fallback hold time on the zoomed-in paddle if RevealCannons somehow isn't " +
                 "running when the cinematic reaches this point - in practice Cannon (the only " +
                 "type that triggers this cinematic) always waits for RevealCannons to finish " +
                 "instead.")]
        public float focusHoldDuration = 0.6f;

        public int CannonAmmo => _cannonAmmo;
        public bool IsAutopilotActive => _autopilotTimeRemaining > 0f;
        public bool IsDroneActive => _droneTimeRemaining > 0f;

        /// <summary>Fired whenever CannonAmmo changes (a fresh pickup, a stacked top-up, a shot
        /// fired, or a reset to 0) - drives a HUD element that only shows while the cannon is
        /// active (see CannonAmmoUIController).</summary>
        public event System.Action<int> OnCannonAmmoChanged;

        private PaddleController _paddle;
        private int _cannonAmmo;
        private float _autopilotTimeRemaining;
        private float _droneTimeRemaining;
        private readonly List<DroneController> _drones = new List<DroneController>();
        private bool _firePressed;
        private GameObject _cannonLeft;
        private GameObject _cannonRight;
        private GameObject _twinCannonLeft;
        private GameObject _twinCannonRight;
        private Coroutine _cannonRevealCoroutine;
        private InputAction _fireAction;
        private bool _focusSequenceActive;
        // The running focus cinematic plus everything it needs restored if it has to be torn down
        // early (see AbortFocusSequence) - kept in fields rather than coroutine locals precisely
        // so an abort from outside the coroutine can still put the camera/time/input back.
        private Coroutine _focusSequenceCoroutine;
        private Vector3 _focusOriginalCameraPosition;
        private float _focusOriginalCameraSize;
        private CameraShake _focusSuspendedCameraShake;

        /// <summary>turretSpacing plus any TurretSpacingBonus from acquired Cards - both the
        /// barrels' own built position and each shot's spawn/travel offset read this instead of
        /// the raw field, so a "Split the Atom" card widens the spread live.</summary>
        private float EffectiveTurretSpacing => turretSpacing
            + (runModifiers != null ? runModifiers.GetBonus(ModifierType.TurretSpacingBonus) : 0f);

        private void Awake()
        {
            _paddle = GetComponent<PaddleController>();
            _cannonLeft = BuildCannonVisual(_paddle, -EffectiveTurretSpacing / 2f, "Left");
            _cannonRight = BuildCannonVisual(_paddle, EffectiveTurretSpacing / 2f, "Right");

            // Built (hidden) regardless of whether the Quantum Mirror card is active yet, same as
            // the main barrels above - RefreshTwinPaddle/SetCannonVisualsActive toggle them, not
            // their construction.
            if (twinPaddle != null)
            {
                _twinCannonLeft = BuildCannonVisual(twinPaddle, -EffectiveTurretSpacing / 2f, "TwinLeft");
                _twinCannonRight = BuildCannonVisual(twinPaddle, EffectiveTurretSpacing / 2f, "TwinRight");
            }

            // Reuses the same actions asset PaddleController already holds (same GameObject) -
            // leave unset, as every existing isolated test does, to fall back to the original
            // raw Gamepad.current poll below, unchanged.
            if (_paddle.actions != null)
            {
                _fireAction = _paddle.actions.FindActionMap("Player").FindAction("Fire");
                _fireAction.performed += OnFirePerformed;
                _fireAction.Enable();
            }

            RefreshTurretSkin();
            CosmeticsManager.OnTurretSkinChanged += RefreshTurretSkin;
            if (runModifiers != null)
            {
                runModifiers.OnModifiersChanged += RefreshTurretSpacing;
                runModifiers.OnModifiersChanged += RefreshTwinPaddle;
            }
            RefreshTwinPaddle();
        }

        private void OnFirePerformed(InputAction.CallbackContext context)
        {
            _firePressed = true;
        }

        private void OnDestroy()
        {
            if (_fireAction != null) _fireAction.performed -= OnFirePerformed;
            CosmeticsManager.OnTurretSkinChanged -= RefreshTurretSkin;
            if (runModifiers != null)
            {
                runModifiers.OnModifiersChanged -= RefreshTurretSpacing;
                runModifiers.OnModifiersChanged -= RefreshTwinPaddle;
            }
        }

        /// <summary>Repositions every already-built barrel (main and twin) to the current
        /// EffectiveTurretSpacing - called whenever a Card changes TurretSpacingBonus, since
        /// BuildCannonVisual only ever positions them once, at Awake.</summary>
        private void RefreshTurretSpacing()
        {
            float half = EffectiveTurretSpacing / 2f;
            RepositionCannon(_cannonLeft, -half);
            RepositionCannon(_cannonRight, half);
            RepositionCannon(_twinCannonLeft, -half);
            RepositionCannon(_twinCannonRight, half);
        }

        private static void RepositionCannon(GameObject cannon, float lateralOffset)
        {
            if (cannon == null) return;
            var pos = cannon.transform.localPosition;
            pos.y = lateralOffset;
            cannon.transform.localPosition = pos;
        }

        /// <summary>Activates/deactivates twinPaddle to match ModifierType.TwinPaddleEnabled -
        /// called once at Awake (so a Quantum Mirror card already acquired earlier this run - e.g.
        /// on a fresh stage's rebuilt paddle - takes effect immediately) and again every time a
        /// Card is added. Snaps the twin to its mirrored angle on every refresh (not just the
        /// activating one) rather than tracking a separate "just turned on" flag - a snap while
        /// already active and roughly in sync is an imperceptible no-op, so it isn't worth the
        /// extra state.</summary>
        private void RefreshTwinPaddle()
        {
            if (twinPaddle == null) return;

            bool twinPaddleEnabled = runModifiers != null && runModifiers.GetBonus(ModifierType.TwinPaddleEnabled) > 0f;
            twinPaddle.gameObject.SetActive(twinPaddleEnabled);
            if (twinPaddleEnabled)
            {
                twinPaddle.SnapToMirrorAngle();
                SyncTwinCannonVisuals();
            }
        }

        /// <summary>Matches the twin cannons' active/grown state to whatever the main cannons are
        /// currently showing - needed because the main cannons might already be revealed (ammo
        /// already in hand) by the time the Quantum Mirror card activates mid-run, in which case the
        /// twin should appear already-grown immediately rather than staying hidden until the next
        /// Cannon pickup.</summary>
        private void SyncTwinCannonVisuals()
        {
            if (_twinCannonLeft == null || _twinCannonRight == null) return;

            bool cannonsVisible = _cannonLeft.activeSelf;
            _twinCannonLeft.SetActive(cannonsVisible);
            _twinCannonRight.SetActive(cannonsVisible);
            if (cannonsVisible)
            {
                _twinCannonLeft.transform.localScale = _cannonLeft.transform.localScale;
                _twinCannonRight.transform.localScale = _cannonRight.transform.localScale;
            }
        }

        private void Update()
        {
            // Fallback path only - see OnFirePerformed/OnDestroy above for the actions-driven path.
            if (_fireAction == null && Gamepad.current != null && Gamepad.current.buttonSouth.wasPressedThisFrame)
                _firePressed = true;
        }

        private void FixedUpdate()
        {
            UpdateAutopilot();
            UpdateDrones();

            bool firePressed = _firePressed;
            _firePressed = false;

            // A launches the ball while docked (handled entirely inside BallController) and
            // fires the cannon once launched - the two never actually compete for the same
            // press since BallController only acts on it while Docked. Checked via
            // IsAnyBallInPlay (any active launched ball, primary or clone) rather than the
            // primary's own State - during multiball the primary can be dead/Dying while clones
            // are still very much in play, and the cannon should keep firing for them.
            bool ballInPlay = ballManager != null && ballManager.IsAnyBallInPlay();
            if (firePressed && _cannonAmmo > 0 && ballInPlay)
            {
                // Laser Cannon spends the whole pickup on a single shot - a Cannon capsule
                // should only ever fire one beam, regardless of how much ammo bonus other
                // acquired cards granted, rather than letting it be re-fired repeatedly.
                bool laserEnabled = runModifiers != null && runModifiers.GetBonus(ModifierType.LaserBeamEnabled) > 0f;
                FireCannon();
                SetCannonAmmo(laserEnabled ? 0 : _cannonAmmo - 1);
                if (_cannonAmmo <= 0) SetCannonVisualsActive(false);
            }
        }

        /// <summary>Sets CannonAmmo and raises OnCannonAmmoChanged - every place that touches
        /// _cannonAmmo goes through here instead of assigning it directly, so the HUD (see
        /// CannonAmmoUIController) always stays in sync.</summary>
        private void SetCannonAmmo(int value)
        {
            _cannonAmmo = Mathf.Max(0, value);
            OnCannonAmmoChanged?.Invoke(_cannonAmmo);
        }

        /// <summary>Applies type's effect - via the full pause/zoom/reveal/resume cinematic (see
        /// PlayPowerUpFocusSequence) when focusCamera is wired up, the type is Cannon or Drone,
        /// no such sequence is already running, and that same ability isn't already active,
        /// otherwise instantly. The zoom-in cinematic only exists to frame the very first reveal
        /// of the barrels/drone(s) - a Cannon pickup caught while the cannon is already active
        /// (ammo still in hand) skips it entirely and just tops up its ammo in place, and a Drone
        /// pickup caught while a drone is already active just refreshes its remaining duration in
        /// place (see ApplyPowerUpEffect for both), since the barrels/drone are already visibly
        /// out. Multiball and Autopilot always apply instantly with no camera effect.</summary>
        public void CollectPowerUp(PowerUpType type)
        {
            // The cinematic only plays while gameplay is genuinely live - a capsule caught in the
            // window where the last ball is already dying (see BallState.Dying) must not start a
            // pause/zoom that the imminent death sequence would tear down mid-flight (deactivating
            // the paddle kills every coroutine on this component, which once left Time.timeScale
            // stuck at 0 and froze the whole game).
            bool gameplayLive = ballManager == null || ballManager.IsAnyBallInPlay();
            bool cannonAlreadyActive = type == PowerUpType.Cannon && _cannonAmmo > 0;
            bool droneAlreadyActive = type == PowerUpType.Drone && _droneTimeRemaining > 0f;
            bool zoomEligibleType = type == PowerUpType.Cannon || type == PowerUpType.Drone;
            if (zoomEligibleType && focusCamera != null && !_focusSequenceActive && gameplayLive
                && !cannonAlreadyActive && !droneAlreadyActive)
                _focusSequenceCoroutine = StartCoroutine(PlayPowerUpFocusSequence(type));
            else
                ApplyPowerUpEffect(type);
        }

        private void ApplyPowerUpEffect(PowerUpType type)
        {
            switch (type)
            {
                case PowerUpType.Multiball:
                    if (ballManager != null) ballManager.ActivateMultiball();
                    break;
                case PowerUpType.Autopilot:
                    float autopilotBonus = runModifiers != null ? runModifiers.GetBonus(ModifierType.AutopilotDurationBonus) : 0f;
                    _autopilotTimeRemaining = autopilotDuration + autopilotBonus;
                    break;
                case PowerUpType.Cannon:
                    float ammoBonus = runModifiers != null ? runModifiers.GetBonus(ModifierType.CannonAmmoBonus) : 0f;
                    int grantedAmmo = cannonAmmoPerPickup + Mathf.RoundToInt(ammoBonus);
                    if (_cannonAmmo > 0)
                    {
                        // Already active - stack ammo on top instead of replaying the reveal (the
                        // barrels are already out) or resetting the count back down.
                        SetCannonAmmo(_cannonAmmo + grantedAmmo);
                    }
                    else
                    {
                        SetCannonAmmo(grantedAmmo);
                        if (_cannonRevealCoroutine != null) StopCoroutine(_cannonRevealCoroutine);
                        _cannonRevealCoroutine = StartCoroutine(RevealCannons());
                    }
                    break;
                case PowerUpType.Drone:
                    float droneDurationBonus = runModifiers != null ? runModifiers.GetBonus(ModifierType.DroneDurationBonus) : 0f;
                    _droneTimeRemaining = droneDuration + droneDurationBonus;
                    // Already active - just refreshing the timer above is enough, the drone(s)
                    // are already visibly out there. Only spawn fresh ones the first time.
                    if (_drones.Count == 0) SpawnDrones();
                    break;
            }
        }

        /// <summary>Pauses the game, zooms focusCamera in on the paddle, applies type's effect
        /// framed by that zoom, holds on it for however long that type's own reveal takes -
        /// Cannon waits for RevealCannons to actually finish; Multiball/Autopilot (no bespoke
        /// reveal yet) just hold for focusHoldDuration as a placeholder until real
        /// animations/sounds exist for them too - then zooms back out and resumes. Runs entirely
        /// on unscaled time/WaitForSecondsRealtime, matching every other pause-safe effect in this
        /// project (CardOfferController, DissolveEffect, ScaleInOvershoot), since Time.timeScale
        /// is 0 for the whole thing. Guarded by _focusSequenceActive - CollectPowerUp only starts
        /// this when nothing is currently running, so a second capsule caught mid-sequence (e.g.
        /// right after a Multiball split lands two at once) just applies its effect immediately
        /// instead of stacking another pause on top.</summary>
        private IEnumerator PlayPowerUpFocusSequence(PowerUpType type)
        {
            _focusSequenceActive = true;

            _focusOriginalCameraPosition = focusCamera.transform.position;
            _focusOriginalCameraSize = GetCameraZoom();
            // CameraShake's LateUpdate snaps the camera back to its captured rest position on
            // every zero-trauma frame, which would override this sequence's own position
            // animation the moment each frame's coroutine step finished - suspended for the
            // duration, same as CustomCam.
            _focusSuspendedCameraShake = focusCamera.GetComponent<CameraShake>();
            if (_focusSuspendedCameraShake != null) _focusSuspendedCameraShake.enabled = false;
            if (customCam != null) customCam.enabled = false;

            Time.timeScale = 0f;
            SetGameplayActionsEnabled(false);

            Vector3 targetPosition = GetPaddleWorldPosition();
            targetPosition.z = _focusOriginalCameraPosition.z;
            float focusZoom = focusCamera.orthographic ? focusOrthographicSize : focusFieldOfView;
            yield return AnimateCamera(_focusOriginalCameraPosition, targetPosition,
                _focusOriginalCameraSize, focusZoom, focusZoomInDuration);

            ApplyPowerUpEffect(type);

            if (_cannonRevealCoroutine != null)
                yield return _cannonRevealCoroutine;
            else
                yield return new WaitForSecondsRealtime(focusHoldDuration);

            yield return AnimateCamera(focusCamera.transform.position, _focusOriginalCameraPosition,
                GetCameraZoom(), _focusOriginalCameraSize, focusZoomOutDuration);

            RestoreFocusSequenceState();
        }

        /// <summary>Puts back everything PlayPowerUpFocusSequence changed - the shared tail of
        /// both the normal end-of-sequence path and an early abort.</summary>
        private void RestoreFocusSequenceState()
        {
            SetGameplayActionsEnabled(true);
            Time.timeScale = 1f;
            if (_focusSuspendedCameraShake != null) _focusSuspendedCameraShake.enabled = true;
            if (customCam != null) customCam.enabled = true;

            _focusSequenceActive = false;
            _focusSequenceCoroutine = null;
            _focusSuspendedCameraShake = null;
        }

        /// <summary>Tears down a focus cinematic that can't be allowed to finish - called from
        /// ResetAbilities, i.e. on death and on stage transitions. Without this, the death
        /// sequence deactivating the paddle GameObject would silently kill the sequence's
        /// coroutine partway through, leaving Time.timeScale stuck at 0 (a fully frozen game),
        /// input disabled, and the camera stranded zoomed-in on the paddle. Snaps the camera
        /// straight back to its original framing rather than animating - the death/transition
        /// taking over has its own visual language, and a leisurely zoom-out would fight it.</summary>
        private void AbortFocusSequence()
        {
            if (!_focusSequenceActive) return;

            if (_focusSequenceCoroutine != null) StopCoroutine(_focusSequenceCoroutine);
            focusCamera.transform.position = _focusOriginalCameraPosition;
            SetCameraZoom(_focusOriginalCameraSize);
            RestoreFocusSequenceState();
        }

        /// <summary>The paddle's actual on-screen position - unlike most objects, PaddleController
        /// itself never moves (it sits fixed at the arena center and only rotates, see its own
        /// class doc comment), so its visual position has to be derived from CurrentAngleDegrees
        /// and paddleOrbitRadius the same way BallController.DockToPaddle does, rather than just
        /// reading transform.position.</summary>
        private Vector3 GetPaddleWorldPosition()
        {
            float rad = _paddle.CurrentAngleDegrees * Mathf.Deg2Rad;
            Vector2 pos = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * _paddle.settings.paddleOrbitRadius;
            return new Vector3(pos.x, pos.y, focusCamera.transform.position.z);
        }

        private IEnumerator AnimateCamera(Vector3 fromPosition, Vector3 toPosition, float fromSize, float toSize, float duration)
        {
            float elapsed = 0f;
            float safeDuration = Mathf.Max(0.0001f, duration);
            while (elapsed < safeDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / safeDuration);
                focusCamera.transform.position = Vector3.Lerp(fromPosition, toPosition, t);
                SetCameraZoom(Mathf.Lerp(fromSize, toSize, t));
                yield return null;
            }
            focusCamera.transform.position = toPosition;
            SetCameraZoom(toSize);
        }

        /// <summary>Reads whichever property actually controls focusCamera's zoom - orthographicSize
        /// has no effect on a perspective camera (it's silently ignored), so the focus cinematic
        /// has to drive fieldOfView instead when focusCamera isn't orthographic, or the zoom-in
        /// degenerates into a plain pan (see focusFieldOfView).</summary>
        private float GetCameraZoom()
        {
            return focusCamera.orthographic ? focusCamera.orthographicSize : focusCamera.fieldOfView;
        }

        private void SetCameraZoom(float value)
        {
            if (focusCamera.orthographic) focusCamera.orthographicSize = value;
            else focusCamera.fieldOfView = value;
        }

        /// <summary>Enables/disables Move and Fire for the duration of the focus effect - mirrors
        /// CardOfferController.SetGameplayActionsEnabled exactly, since both freeze gameplay via
        /// Time.timeScale and need the same guard against an eager press queuing up and firing
        /// the instant control returns.</summary>
        private void SetGameplayActionsEnabled(bool enabled)
        {
            if (_paddle.actions == null) return;

            var playerMap = _paddle.actions.FindActionMap("Player");
            var move = playerMap.FindAction("Move");
            var fire = playerMap.FindAction("Fire");

            if (enabled)
            {
                move.Enable();
                fire.Enable();
            }
            else
            {
                move.Disable();
                fire.Disable();
            }
        }

        /// <summary>Called by BallManager the instant every ball has been lost - abilities
        /// don't survive losing the ball, and any bullets already in flight are destroyed
        /// immediately rather than being left to finish their flight. Also clears any
        /// still-falling power-up capsule (e.g. an Autopilot drop that hadn't been caught yet) -
        /// otherwise catching it right after respawning would silently re-grant the very ability
        /// that's supposed to have just been reset, reading as "it persisted through death."</summary>
        public void ResetAbilities()
        {
            // First, before anything else - if a power-up focus cinematic is mid-flight when a
            // death/transition lands, it must be torn down cleanly (time/input/camera restored)
            // before the sequence that called this starts deactivating objects out from under it.
            AbortFocusSequence();

            SetCannonAmmo(0);
            _autopilotTimeRemaining = 0f;
            _paddle.AutopilotOverrideAngleDegrees = null;
            SetCannonVisualsActive(false);

            _droneTimeRemaining = 0f;
            _drones.Clear();
            foreach (var drone in Object.FindObjectsByType<DroneController>(FindObjectsSortMode.None))
                Destroy(drone.gameObject);

            foreach (var bullet in Object.FindObjectsByType<Bullet>(FindObjectsSortMode.None))
                Destroy(bullet.gameObject);
            foreach (var beam in Object.FindObjectsByType<LaserBeam>(FindObjectsSortMode.None))
                Destroy(beam.gameObject);
            foreach (var capsule in Object.FindObjectsByType<PowerUpCapsule>(FindObjectsSortMode.None))
                Destroy(capsule.gameObject);
        }

        /// <summary>Instantly hides every barrel, main and twin alike (used when ammo runs out
        /// or the ability is reset on death) - the grow-out reveal only plays on collection, via
        /// RevealCannons.</summary>
        private void SetCannonVisualsActive(bool active)
        {
            if (_cannonRevealCoroutine != null)
            {
                StopCoroutine(_cannonRevealCoroutine);
                _cannonRevealCoroutine = null;
            }

            _cannonLeft.SetActive(active);
            _cannonRight.SetActive(active);
            if (_twinCannonLeft != null) _twinCannonLeft.SetActive(active);
            if (_twinCannonRight != null) _twinCannonRight.SetActive(active);
            if (!active)
            {
                // Reset to zero-grown so the next pickup's reveal starts from scratch instead of
                // popping to whatever scale was left over from an interrupted animation.
                _cannonLeft.transform.localScale = new Vector3(0f, 1f, 1f);
                _cannonRight.transform.localScale = new Vector3(0f, 1f, 1f);
                if (_twinCannonLeft != null) _twinCannonLeft.transform.localScale = new Vector3(0f, 1f, 1f);
                if (_twinCannonRight != null) _twinCannonRight.transform.localScale = new Vector3(0f, 1f, 1f);
            }
        }

        /// <summary>Animates both barrels scaling up from nothing along local +X (the direction
        /// each sprite should extend away from the paddle's surface, per its own pivot), so they
        /// visibly grow out of the paddle rather than just popping into existence.</summary>
        private IEnumerator RevealCannons()
        {
            // Re-synced on every reveal (not just once at Awake) so the barrels always match
            // whatever turret skin is currently selected - handy while iterating, since a freshly
            // caught Cannon capsule always picks up the latest choice.
            RefreshTurretSkin();
            _cannonLeft.SetActive(true);
            _cannonRight.SetActive(true);
            if (_twinCannonLeft != null) _twinCannonLeft.SetActive(true);
            if (_twinCannonRight != null) _twinCannonRight.SetActive(true);

            float elapsed = 0f;
            float duration = Mathf.Max(0.0001f, cannonRevealDuration);
            while (elapsed < duration)
            {
                // Unscaled: this plays during the power-up focus effect's pause (see
                // PlayPowerUpFocusSequence, which holds Time.timeScale at 0 and waits on this
                // very coroutine) - scaled deltaTime would freeze it there and deadlock the wait.
                elapsed += Time.unscaledDeltaTime;
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
            if (_twinCannonLeft != null) _twinCannonLeft.transform.localScale = new Vector3(t, 1f, 1f);
            if (_twinCannonRight != null) _twinCannonRight.transform.localScale = new Vector3(t, 1f, 1f);
        }

        /// <summary>Applies the currently selected TurretSkin's sprite (and optional material
        /// override) to every barrel, main and twin alike.</summary>
        private void RefreshTurretSkin()
        {
            var skin = GetCurrentTurretSkin();
            if (skin == null) return;

            ApplyTurretSkin(_cannonLeft, skin);
            ApplyTurretSkin(_cannonRight, skin);
            if (_twinCannonLeft != null) ApplyTurretSkin(_twinCannonLeft, skin);
            if (_twinCannonRight != null) ApplyTurretSkin(_twinCannonRight, skin);
        }

        /// <summary>The currently equipped TurretSkin asset (not just its index) - read by
        /// FireBarrel to look up that skin's own BulletSkin, so each turret look fires its own
        /// kind of bullet.</summary>
        private TurretSkin GetCurrentTurretSkin()
        {
            if (availableTurretSkins == null || availableTurretSkins.Length == 0) return null;

            int index = Mathf.Clamp(CosmeticsManager.GetTurretSkinIndex(), 0, availableTurretSkins.Length - 1);
            return availableTurretSkins[index];
        }

        private static void ApplyTurretSkin(GameObject cannon, TurretSkin skin)
        {
            var spriteRenderer = cannon.GetComponent<SpriteRenderer>();
            if (skin.sprite != null) spriteRenderer.sprite = skin.sprite;
            if (skin.materialOverride != null) spriteRenderer.sharedMaterial = skin.materialOverride;
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

        /// <summary>Counts down the active Drone power-up's remaining duration - once it runs
        /// out, every currently-spawned drone phases out (see DespawnDrones) instead of the
        /// countdown just silently going negative and doing nothing, the way it would if this
        /// only ever checked "&gt; 0" without also reacting to the exact frame it crosses zero.</summary>
        private void UpdateDrones()
        {
            if (_droneTimeRemaining <= 0f) return;

            _droneTimeRemaining -= Time.fixedDeltaTime;
            if (_droneTimeRemaining <= 0f) DespawnDrones();
        }

        /// <summary>Spawns 1 + ModifierType.DroneCountBonus drones, each offset to its own share
        /// of a full circle around the paddle (see DroneController's own orbitPhaseOffsetDegrees
        /// param) so multiple drones spread out instead of stacking on top of one another. Fire
        /// rate is boosted by ModifierType.DroneFireRateMultiplier (a shorter interval = fires
        /// more often) - computed once here and baked into each drone at spawn time rather than
        /// having DroneController read RunModifiers itself, keeping it as dumb/self-contained as
        /// Bullet.</summary>
        private void SpawnDrones()
        {
            float countBonus = runModifiers != null ? runModifiers.GetBonus(ModifierType.DroneCountBonus) : 0f;
            int droneCount = Mathf.Max(1, 1 + Mathf.RoundToInt(countBonus));

            float fireRateMultiplier = runModifiers != null ? runModifiers.GetMultiplier(ModifierType.DroneFireRateMultiplier) : 1f;
            float effectiveFireInterval = droneFireInterval / Mathf.Max(0.01f, fireRateMultiplier);

            for (int i = 0; i < droneCount; i++)
            {
                float orbitPhaseOffsetDegrees = droneCount > 1 ? (360f / droneCount) * i : 0f;

                var droneObject = new GameObject($"Drone_{i}");
                var drone = droneObject.AddComponent<DroneController>();
                drone.Initialize(_paddle, audioManager, droneDissolveMaterial, droneSprite, droneTintColor,
                    droneVisualRadius, droneHoverRadiusOffset, orbitPhaseOffsetDegrees,
                    droneHoverWobbleAmplitudeDegrees, droneHoverWobbleSpeed, droneHoverFollowSpeed,
                    effectiveFireInterval, droneBulletSpeed, droneBulletMaterial);
                _drones.Add(drone);
            }
        }

        /// <summary>Starts every active drone's dissolve-out-and-destroy sequence (see
        /// DroneController.BeginPhaseOut) and forgets about them - unlike ResetAbilities' own
        /// instant force-destroy, this is the graceful "ran out of time" ending, not a hard
        /// reset.</summary>
        private void DespawnDrones()
        {
            foreach (var drone in _drones)
                if (drone != null) drone.BeginPhaseOut(dronePhaseOutDuration);
            _drones.Clear();
        }

        /// <summary>Fires both of the main paddle's cannon barrels at once - one bullet per
        /// barrel, both flying straight out parallel to each other (matching the barrels' own
        /// visual orientation), just spawned from each barrel's own sideways-offset position
        /// rather than converging from the paddle's center. Also fires the twin paddle's own pair
        /// (see Card_QuantumMirror) when it's active, from the same single press and the same
        /// shared ammo pool - Quantum Mirror doubles firepower, not shot cost.</summary>
        private void FireCannon()
        {
            float half = EffectiveTurretSpacing / 2f;
            FireBarrel(_paddle, -half);
            FireBarrel(_paddle, half);

            if (twinPaddle != null && twinPaddle.gameObject.activeSelf)
            {
                FireBarrel(twinPaddle, -half);
                FireBarrel(twinPaddle, half);
            }
        }

        /// <summary>Fires one barrel's shot from the given paddle (the main one, or the twin -
        /// see FireCannon). Normally a single bullet, but a ModifierType.ExtraBulletsPerBarrel
        /// card (Split the Atom) fans out extra bullets around the barrel's own aim direction
        /// without costing any extra ammo (a shot is still a shot). A ModifierType.LaserBeamEnabled
        /// card (Laser Cannon) replaces bullets entirely with one continuous beam per barrel
        /// instead - Split the Atom stacks widen that single beam rather than also fanning out
        /// separate bullets. A ModifierType.BulletRicochetBonus card (Ricochet Rounds) gives each
        /// fired bullet that many extra bounces off a brick instead of being destroyed on hit -
        /// ignored while the Laser Cannon is active, since there are no bullets to bounce.</summary>
        private void FireBarrel(PaddleController paddle, float lateralOffset)
        {
            float fireAngleDegrees = paddle.CurrentAngleDegrees;
            float spawnRadius = paddle.settings.paddleOrbitRadius + paddle.radialThickness / 2f + 0.3f;
            Vector2 spawnPos = paddle.transform.TransformPoint(new Vector3(spawnRadius, lateralOffset, 0f));

            bool laserEnabled = runModifiers != null && runModifiers.GetBonus(ModifierType.LaserBeamEnabled) > 0f;
            int extraBullets = runModifiers != null ? Mathf.RoundToInt(runModifiers.GetBonus(ModifierType.ExtraBulletsPerBarrel)) : 0;

            if (laserEnabled)
            {
                FireLaserBeam(paddle, spawnPos, fireAngleDegrees, extraBullets);
                return;
            }

            audioManager?.PlayBullet();

            int ricochets = runModifiers != null ? Mathf.RoundToInt(runModifiers.GetBonus(ModifierType.BulletRicochetBonus)) : 0;

            // The equipped turret's own BulletSkin (if any) overrides the shared defaults below,
            // so each turret look can fire its own kind of bullet. The run-modifier multiplier
            // stacks on top of (rather than replacing) that per-skin flavor.
            var bulletSkin = GetCurrentTurretSkin()?.bulletSkin;
            Material material = bulletSkin != null && bulletSkin.materialOverride != null ? bulletSkin.materialOverride : bulletMaterial;
            Color? color = bulletSkin != null ? bulletSkin.color : (Color?)null;
            float bulletSpeedMultiplier = runModifiers != null ? runModifiers.GetMultiplier(ModifierType.BulletSpeedMultiplier) : 1f;
            float speed = bulletSpeed * bulletSpeedMultiplier * (bulletSkin != null ? bulletSkin.speedMultiplier : 1f);

            int bulletCount = Mathf.Max(1, 1 + extraBullets);
            for (int i = 0; i < bulletCount; i++)
            {
                float offsetDegrees = bulletCount > 1 ? (i - (bulletCount - 1) / 2f) * bulletSpreadDegrees : 0f;
                float bulletAngleDegrees = fireAngleDegrees + offsetDegrees;

                var bulletObject = new GameObject("Bullet");
                var bullet = bulletObject.AddComponent<Bullet>();
                bullet.Launch(spawnPos, bulletAngleDegrees, speed, paddle.settings, material, color, ricochets: ricochets);

                // Bullets shouldn't physically collide with any ball in play (primary or
                // multiball clones) - without this, the physics solver would bounce the bullet
                // off (or nudge) the ball on contact, which reads as an unintended bug rather
                // than a shot cleanly passing through toward the bricks it's actually meant to hit.
                var bulletCollider = bulletObject.GetComponent<Collider2D>();
                foreach (var ball in Object.FindObjectsByType<BallController>(FindObjectsSortMode.None))
                {
                    var ballCollider = ball.GetComponent<Collider2D>();
                    if (ballCollider != null) Physics2D.IgnoreCollision(bulletCollider, ballCollider, true);
                }
            }
        }

        /// <summary>Fires one continuous beam from a single barrel - the whole beam appears
        /// instantly spanning out to the arena's outer wall (see LaserBeam), rather than a bullet
        /// traveling there. Width grows with extraBullets (i.e. how many Split the Atom cards are
        /// also equipped), so a combined build fires one dramatically wider beam instead of
        /// multiple separate ones.</summary>
        private void FireLaserBeam(PaddleController paddle, Vector2 spawnPos, float fireAngleDegrees, int extraBullets)
        {
            float width = laserBeamBaseWidth + extraBullets * laserBeamWidthPerExtraBullet;
            float length = Mathf.Max(1f, paddle.settings.outerWallRadius - spawnPos.magnitude);

            var beamObject = new GameObject("LaserBeam");
            var beam = beamObject.AddComponent<LaserBeam>();
            beam.duration = laserBeamDuration;
            beam.Initialize(spawnPos, fireAngleDegrees, width, length, laserBeamMaterial);
            audioManager?.PlayLaser();
        }

        /// <summary>Builds one cannon barrel as a sprite sticking straight outward from the given
        /// paddle's outer edge (the main paddle, or the twin - see FireCannon), offset sideways
        /// from center by lateralOffset (world units) - parented under that paddle (built in the
        /// same local, angle-0-centered space as its own mesh) so it rotates along with it for
        /// free via the parent transform. Both barrels share the same (identity) local rotation
        /// so they point straight out parallel to each other, rather than each fanning outward at
        /// its own angle. Purely cosmetic (no collider), so - unlike the paddle itself - it's a
        /// plain SpriteRenderer rather than a procedural mesh, letting each turret skin be actual
        /// hand-drawn artwork. A small negative local Z nudges it in front of the paddle's own
        /// MeshRenderer, since Unity doesn't otherwise guarantee draw order between a
        /// SpriteRenderer and a MeshRenderer at the same depth. Starts hidden; CollectPowerUp/
        /// ResetAbilities toggle it via SetCannonVisualsActive. The sprite itself is assigned by
        /// RefreshTurretSkin, not here.</summary>
        private static GameObject BuildCannonVisual(PaddleController paddle, float lateralOffset, string label)
        {
            var go = new GameObject($"CannonBarrel_{label}");
            go.transform.SetParent(paddle.transform, worldPositionStays: false);

            float radius = paddle.settings.paddleOrbitRadius + paddle.radialThickness / 2f;
            go.transform.localPosition = new Vector3(radius, lateralOffset, -0.01f);
            go.transform.localRotation = Quaternion.identity;

            go.AddComponent<SpriteRenderer>();

            go.transform.localScale = new Vector3(0f, 1f, 1f);
            go.SetActive(false);
            return go;
        }
    }
}
