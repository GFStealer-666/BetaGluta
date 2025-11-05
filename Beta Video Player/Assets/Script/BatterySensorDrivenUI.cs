using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;
using System;
using Unity.VisualScripting;
public class BatterySensorDrivenUI : MonoBehaviour
{
    [Serializable]
    public class BatteryLevelDef
    {
        public Sprite sprite;
        [Header("Sound")]
        public AudioClip onEnterUp;
        public AudioClip onEnterDown;   
        public AudioClip onHoldAtMax;   
    }

    [Header("UI references")]
    [SerializeField] private Image targetImage; 
    [FormerlySerializedAs("levels")]
    [SerializeField] private BatteryLevelDef[] levelDefs;  // Battery order

    [Header("Timings")]
    [Tooltip("The amount of seconds to detect if no one standing there, before draining the battery down.")]
    [SerializeField] private float presenceTimeout = 2f; 
    [Tooltip("Seconds to keep battery full before draining.")]
    [SerializeField] private float holdAtMaxSeconds = 5;

    [Header("Speeds (levels per second)")]
    [SerializeField] private float fillSpeedLps = 6f;  
    [SerializeField] private float drainSpeedLps = 4f;  

    [Header("Runtime state (Don't touch)")]
    private float lastTrueTime = -999f;
    [SerializeField] private bool lockout = false;
    public bool IsLockout => lockout;
    public event Action<bool> OnLockoutChanged;
    public bool IsHoldPhase { get; private set; }
    public event Action<bool> OnHoldPhaseChanged;

    [Header("Audio")]
    [SerializeField] private AudioSource sfxSource;
    [Range(0f, 1f)] public float sfxVolume = 1f;

    [Header("Debug field")]
    [SerializeField] private float holdUntil = 0f;
    [SerializeField] private float spriteLevel = 0f;           
    [SerializeField] private int _lastApplied = -999;

    int MaxIndex => (levelDefs == null || levelDefs.Length == 0) ? 0 : levelDefs.Length - 1; // Get max index from level length

    void Reset()
    {
        if(targetImage  == null)
        {
            targetImage = GetComponent<Image>();
        }
        if(sfxSource == null)
        {
            sfxSource = GetComponent<AudioSource>();
        }
    }
    void OnEnable()
    {
        if (!targetImage) targetImage = GetComponent<Image>();
        if (!sfxSource) sfxSource = GetComponent<AudioSource>();

        if (SensorReader.Instance != null)
        {
            SensorReader.Instance.OnAnyTrigger += OnAnySensorTriggered;
            ApplyBatteryLevel();
        }
            
    }

    void OnDisable()
    {
        if (SensorReader.Instance != null)
        {
            SensorReader.Instance.OnAnyTrigger -= OnAnySensorTriggered;
        }

    }
    private void OnAnySensorTriggered(string sensorId, float cm)
    {
        // Treat as presence pulse regardless of which sensor fired
        RegisterPresencePulse();
    }

    private void SetLockout(bool value)
    {
        if (lockout == value) return;
        lockout = value;
        OnLockoutChanged?.Invoke(lockout);
    }
    private void SetHoldPhase(bool v)
    {
        if (IsHoldPhase == v) return;
        IsHoldPhase = v;    
        OnHoldPhaseChanged?.Invoke(IsHoldPhase);
    }
    void Update()
    {
        if (!IsCurrentSettingValid()) return;

        // Lockout: hold at max, then drain to zero ignoring sensor
        if (lockout)
        {
            if (Time.time < holdUntil)
            {
                spriteLevel = MaxIndex;
                ApplyBatteryLevel(); // no sfx spam; idx unchanged while holding
                return;
            }
            if (IsHoldPhase) SetHoldPhase(false);
            sfxSource.Stop();
            if (spriteLevel > 0f)
            {
                float prev = spriteLevel;
                spriteLevel = Mathf.Max(0f, spriteLevel - drainSpeedLps * Time.deltaTime);
                ApplyBatteryLevel(prev, spriteLevel);
                return;
            }

            SetLockout(false);
            return;
        }

        bool present = (Time.time - lastTrueTime) <= presenceTimeout;

        if (present)
        {
            if (spriteLevel < MaxIndex)
            {
                float prev = spriteLevel;
                spriteLevel = Mathf.Min(MaxIndex, spriteLevel + fillSpeedLps * Time.deltaTime);
                ApplyBatteryLevel(prev, spriteLevel);

                if (spriteLevel >= MaxIndex)
                {
                    EnterHoldAtMax();
                }
            }
            else
            {
                // Already at max but not yet in lockout → enter hold explicitly
                EnterHoldAtMax();
            }
        }
        else
        {
            if (spriteLevel > 0f)
            {
                float prev = spriteLevel;
                spriteLevel = Mathf.Max(0f, spriteLevel - drainSpeedLps * Time.deltaTime);
                ApplyBatteryLevel(prev, spriteLevel);
            }
        }
    }
    private void EnterHoldAtMax()
    {
        if (!lockout)
        {
            SetLockout(true);
            SetHoldPhase(true);
            holdUntil = Time.time + holdAtMaxSeconds;

            // Play special max-hold clip once (if defined for max level)
            var maxDef = GetDef(MaxIndex);
            if (maxDef != null && maxDef.onHoldAtMax && sfxSource)
                sfxSource.PlayOneShot(maxDef.onHoldAtMax, sfxVolume);
        }
    }
     private void ApplyBatteryLevel(bool forceSprite = false)
    {
        ApplyBatteryLevel(spriteLevel, spriteLevel, forceSprite);
    }
    private void ApplyBatteryLevel(float prevLevel, float newLevel, bool forceSprite = false)
    {
        int prevIdx = Mathf.Clamp(Mathf.RoundToInt(prevLevel), 0, MaxIndex);
        int newIdx = Mathf.Clamp(Mathf.RoundToInt(newLevel), 0, MaxIndex);

        // Sprite update (debounced)
        if (forceSprite || newIdx != _lastApplied)
        {
            var def = GetDef(newIdx);
            if (def != null && def.sprite && targetImage)
                targetImage.sprite = def.sprite;

            // Decide which clip to play for this transition
            if (newIdx != _lastApplied && sfxSource)
            {
                bool wentUp = newIdx > _lastApplied;
                var clip = wentUp ? def?.onEnterUp : def?.onEnterDown;
                if (clip) sfxSource.PlayOneShot(clip, sfxVolume);
            }

            _lastApplied = newIdx;
        }
    }
    private BatteryLevelDef GetDef(int idx)
    {
        if (levelDefs == null || levelDefs.Length == 0) return null;
        idx = Mathf.Clamp(idx, 0, levelDefs.Length - 1);
        return levelDefs[idx];
    }
    public void SetLevelInstant(int index)
    {
        spriteLevel = Mathf.Clamp(index, 0, MaxIndex);
        ApplyBatteryLevel       ();
    }

    private bool IsCurrentSettingValid()
    {
        if (levelDefs == null || levelDefs.Length == 0 || targetImage == null) return false;
        else return true;
    }


    #region Backup
    [ContextMenu("Manual Presence Pulse")]
    public void RegisterPresencePulse()
    {
        if (lockout) return;
        lastTrueTime = Time.time;      // updating like it real sensor getting trigger
    }
    #endregion
}
