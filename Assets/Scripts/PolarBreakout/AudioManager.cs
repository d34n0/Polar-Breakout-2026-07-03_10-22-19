using UnityEngine;
using UnityEngine.Audio;

namespace PolarBreakout
{
    /// <summary>
    /// Central home for background music and one-shot SFX. Plain scene MonoBehaviour - no
    /// DontDestroyOnLoad, matching this project's established manager pattern (see
    /// GameSettings.cs's own reasoning) - callers hold a direct Inspector-wired reference rather
    /// than reaching for a static singleton. Reads GameSettings.MasterVolume/MusicVolume/
    /// SFXVolume directly on every play/volume-change rather than waiting on an AudioMixer (none
    /// exists in the project yet - see AudioSettingsPanel), so the Options menu's sliders are
    /// actually audible immediately; assign musicMixerGroup/sfxMixerGroup later if/when
    /// MainMixer.mixer gets created - this keeps working unchanged either way.
    ///
    /// SFX fields are typed AudioResource rather than AudioClip, so an Audio Random Container
    /// asset (Window > Audio > Audio Random Container) can be assigned in place of a plain clip
    /// for per-play pitch/volume/clip-choice randomization - AudioClip itself is just one kind of
    /// AudioResource, so a plain clip still works unchanged. AudioSource.PlayOneShot has no
    /// AudioResource overload (confirmed against this Unity version - only plain AudioClip), so
    /// SFX playback goes through a small round-robin pool of AudioSources (each one's own
    /// .resource + Play()) instead of a single PlayOneShot source, preserving the ability for
    /// several SFX to overlap (e.g. multiple bricks hit the same frame).
    /// </summary>
    public class AudioManager : MonoBehaviour
    {
        [Header("Mixer Routing (optional)")]
        [Tooltip("Optional. Routes music through this AudioMixerGroup once MainMixer.mixer exists. " +
                 "Leave unset - volume is already driven directly from GameSettings.MusicVolume/" +
                 "MasterVolume regardless, so this only matters for mixer-side processing " +
                 "(compression, ducking, etc), not basic volume control.")]
        public AudioMixerGroup musicMixerGroup;
        [Tooltip("Optional. Same as musicMixerGroup, but for one-shot SFX.")]
        public AudioMixerGroup sfxMixerGroup;

        [Header("Background Music")]
        public AudioClip backgroundMusic;
        [Range(0f, 1f)] public float musicVolume = 1f;
        public bool playMusicOnStart = true;

        [Header("One-Shot SFX")]
        [Tooltip("Played once per HexWipeTransition sweep (level build-in/tear-down), not once " +
                 "per individual hex cell - see HexWipeTransition.RunSweep. Accepts a plain " +
                 "AudioClip or an Audio Random Container asset.")]
        public AudioResource wipeTransitionSound;
        [Tooltip("Played whenever a DissolveEffect.DissolveIn is triggered (paddle/ball/death " +
                 "zone fading back into place).")]
        public AudioResource dissolveInSound;
        [Tooltip("Played whenever a DissolveEffect.DissolveOut is triggered (paddle/ball/death " +
                 "zone fading away).")]
        public AudioResource dissolveOutSound;
        [Tooltip("Played once the player actually loses their ball (all balls gone - see " +
                 "BallManager.OnAllBallsLost), alongside the explosion effect.")]
        public AudioResource deathSound;
        [Tooltip("Played once per Cannon shot fired (see PaddleAbilities.FireBarrel) - once per " +
                 "barrel, not once per individual bullet in a fanned-out multi-bullet shot.")]
        public AudioResource bulletSound;
        [Tooltip("Played once per Laser Cannon beam fired (see PaddleAbilities.FireLaserBeam).")]
        public AudioResource laserSound;
        [Tooltip("Played whenever the ball bounces off the outer wall/screen edge (see " +
                 "BallController.BounceOffScreenEdges/BounceOffCircularWall).")]
        public AudioResource boundaryHitSound;
        [Tooltip("Played whenever a launched ball collides with the paddle.")]
        public AudioResource paddleHitSound;
        [Tooltip("Played whenever the paddle catches a PowerUpCapsule.")]
        public AudioResource capsulePickupSound;
        [Tooltip("Played whenever the paddle catches a ShardPickup.")]
        public AudioResource shardPickupSound;
        [Tooltip("Played once whenever the ball enters its phasing/spin state (see " +
                 "BallController.IsPhasing) - i.e. the moment |Spin| crosses the phase threshold, " +
                 "not every frame it stays phasing. Leave unset for silence.")]
        public AudioResource spinEnterSound;
        [Range(0f, 1f)]
        public float sfxVolume = 1f;
        [Tooltip("How many simultaneous one-shot SFX sources to keep ready - avoids cutting off " +
                 "overlapping sounds (e.g. several bricks destroyed in the same frame). Each play " +
                 "call round-robins to the next source in the pool.")]
        public int sfxPoolSize = 6;

        private AudioSource _musicSource;
        private AudioSource[] _sfxPool;
        private int _nextSfxIndex;

        private void Awake()
        {
            _musicSource = gameObject.AddComponent<AudioSource>();
            _musicSource.loop = true;
            _musicSource.playOnAwake = false;
            _musicSource.outputAudioMixerGroup = musicMixerGroup;

            _sfxPool = new AudioSource[Mathf.Max(1, sfxPoolSize)];
            for (int i = 0; i < _sfxPool.Length; i++)
            {
                var src = gameObject.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.loop = false;
                src.outputAudioMixerGroup = sfxMixerGroup;
                _sfxPool[i] = src;
            }
        }

        private void OnEnable() => GameSettings.OnSettingsChanged += ApplyMusicVolume;
        private void OnDisable() => GameSettings.OnSettingsChanged -= ApplyMusicVolume;

        private void Start()
        {
            if (playMusicOnStart && backgroundMusic != null) PlayMusic(backgroundMusic);
        }

        private float EffectiveMusicVolume => musicVolume * GameSettings.MusicVolume * GameSettings.MasterVolume;
        private float EffectiveSfxVolume => sfxVolume * GameSettings.SFXVolume * GameSettings.MasterVolume;

        /// <summary>Live-updates the music source so dragging the Options menu's Music/Master
        /// sliders is audible immediately, not just on the next PlayMusic call.</summary>
        private void ApplyMusicVolume()
        {
            if (_musicSource != null && _musicSource.isPlaying) _musicSource.volume = EffectiveMusicVolume;
        }

        /// <summary>Starts looping background music. Safe to call with the clip already playing -
        /// it's a no-op in that case rather than restarting it.</summary>
        public void PlayMusic(AudioClip clip)
        {
            if (clip == null) return;
            if (_musicSource.clip == clip && _musicSource.isPlaying) return;

            _musicSource.clip = clip;
            _musicSource.volume = EffectiveMusicVolume;
            _musicSource.Play();
        }

        public void StopMusic() => _musicSource.Stop();

        /// <summary>Fires a one-shot SFX - safe to call rapidly/overlapping (e.g. several bricks
        /// destroyed the same frame), since each call claims the next source in the round-robin
        /// pool rather than sharing one. resource can be a plain AudioClip or an Audio Random
        /// Container - the container re-rolls its own configured randomization (clip choice,
        /// pitch, volume) every time it's played.</summary>
        public void PlaySfx(AudioResource resource, float volumeScale = 1f)
        {
            if (resource == null) return;
            var src = _sfxPool[_nextSfxIndex];
            _nextSfxIndex = (_nextSfxIndex + 1) % _sfxPool.Length;
            src.resource = resource;
            src.volume = volumeScale * EffectiveSfxVolume;
            src.Play();
        }

        public void PlayWipeTransition() => PlaySfx(wipeTransitionSound);
        public void PlayDissolveIn() => PlaySfx(dissolveInSound);
        public void PlayDissolveOut() => PlaySfx(dissolveOutSound);
        public void PlayDeath() => PlaySfx(deathSound);
        public void PlayBullet() => PlaySfx(bulletSound);
        public void PlayLaser() => PlaySfx(laserSound);
        public void PlayBoundaryHit() => PlaySfx(boundaryHitSound);
        public void PlayPaddleHit() => PlaySfx(paddleHitSound);
        public void PlayCapsulePickup() => PlaySfx(capsulePickupSound);
        public void PlayShardPickup() => PlaySfx(shardPickupSound);
        public void PlaySpinEnter() => PlaySfx(spinEnterSound);
    }
}
