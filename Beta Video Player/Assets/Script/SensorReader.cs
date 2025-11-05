using UnityEngine;
using System.IO.Ports;
using System.Threading;
using System.Collections.Concurrent;
using System;
using System.Collections.Generic;
using System.Linq;

public class SensorReader : MonoBehaviour
{
    public static SensorReader Instance { get; private set; }

    [Header("Serial Port Settings")]
    public PortName portName = PortName.COM4;
    public BaudRate baudRate = BaudRate.Baud115200;

    [Header("Input Format")]
    [Tooltip("If a non-numeric line equals this literal, it will also trigger (legacy support).")]
    public string legacyTrueLiteral = "true";
    [Tooltip("Unit in which numeric values are printed by the device(s).")]
    public InputUnit numericInputUnit = InputUnit.Millimeters;

    [Header("Parsing")]
    [Tooltip("Tokens are split by commas or spaces. Key=Value pairs are parsed into sensorId/value. "
           + "If the line is a single number, it is treated as 'DEFAULT' sensor.")]
    public string defaultSensorId = "DEFAULT";
    [Tooltip("Optional whitelist. If empty, any key (A,B,C...) is accepted.")]
    public List<string> allowedSensorIds = new List<string>(); // e.g., ["A","B","C"]

    [Header("Trigger Logic (centimeters)")]
    [Tooltip("If true: trigger when smoothed_cm <= threshold. If false: trigger when smoothed_cm >= threshold.")]
    public bool lessThanTriggers = true;
    public float triggerThresholdCM = 50f;
    [Tooltip("Minimum seconds between any two triggers (any sensor).")]
    public float retriggerCooldownSec = 0.5f;

    [Header("Smoothing / Anti-Glitch")]
    [Tooltip("Time constant (seconds) for exponential moving average per sensor. Smaller = more responsive.")]
    public float smoothingSeconds = 0.25f;
    [Tooltip("Limit how much the value may change per sample before smoothing (in cm). 0 = no cap.")]
    public float maxStepPerSampleCM = 0f;
    [Tooltip("Require the condition to be true for this many consecutive processed samples before firing.")]
    public int minConsecutiveSamplesToTrigger = 1;

    [Header("Debug")]
    public bool logAllSerial = false;
    public bool logTriggers = true;
    public bool logPerSensor = false;

    public event Action<string, float> OnSensorParsed;       // (sensorId, rawCm)
    public event Action<string, float> OnSensorSmoothed;     // (sensorId, smoothedCm)
    public event Action<string, float> OnSensorTrigger;      // (sensorId, smoothedCm) - specific
    public event Action<string, float> OnAnyTrigger;         // (sensorId, smoothedCm) - first that triggered

    private SerialPort _serial;
    private Thread _readThread;
    private bool _running;
    private readonly ConcurrentQueue<Action> _main = new ConcurrentQueue<Action>();
    private float _lastAnyTriggerAt = -999f;

    // Per-sensor state
    private class SensorState
    {
        public string id;
        public float smoothedCm;
        public bool hasValue;
        public float lastSampleTime; 
        public int consecutiveTrue;  
    }
    private readonly Dictionary<string, SensorState> _sensors = new Dictionary<string, SensorState>(StringComparer.OrdinalIgnoreCase);

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void Start()
    {
        try
        {
            _serial = new SerialPort(portName.ToString(), (int)baudRate)
            {
                NewLine = "\n",
                ReadTimeout = 1000,
                DtrEnable = true,
                RtsEnable = true
            };
            _serial.Open();
            _running = true;
            _readThread = new Thread(ReadLoop) { IsBackground = true };
            _readThread.Start();
            Debug.Log($"[MultiSerial] Listening on {portName} @ {baudRate}");
        }
        catch (Exception ex)
        {
            Debug.LogError("[MultiSerial] Failed to open port: " + ex.Message);
        }
    }

    void OnDestroy()
    {
        _running = false;
        try { _serial?.Close(); } catch { }
    }

    void Update()
    {
        while (_main.TryDequeue(out var a))
        {
            try { a?.Invoke(); } catch (Exception ex) { Debug.LogException(ex); }
        }
    }

    // ---------- Thread: read loop ----------
    private void ReadLoop()
    {
        while (_running)
        {
            try
            {
                string raw = _serial.ReadLine();
                if (raw == null) continue;
                string line = raw.Trim();

                // Legacy literal
                if (!string.IsNullOrEmpty(legacyTrueLiteral) &&
                    line.Equals(legacyTrueLiteral, StringComparison.OrdinalIgnoreCase))
                {
                    _main.Enqueue(() => HandleLegacyLiteral(line));
                    continue;
                }

                // Try parse "K=V,K=V ..." multi-sensor
                if (TryParseKeyValueLine(line, out var kvs))
                {
                    foreach (var kv in kvs)
                    {
                        _main.Enqueue(() => HandleSensorValue(kv.Key, kv.Value, line));
                    }
                    continue;
                }

                // Fallback: single numeric -> default sensor
                if (float.TryParse(line, out float single))
                {
                    float cm = (numericInputUnit == InputUnit.Millimeters) ? (single / 10f) : single;
                    _main.Enqueue(() => HandleSensorValue(defaultSensorId, cm, line));
                }
                else if (logAllSerial)
                {
                    _main.Enqueue(() => Debug.Log($"[MultiSerial] {Time.realtimeSinceStartup:F3}s <- '{line}'"));
                }
            }
            catch (TimeoutException) { /* normal */ }
            catch (Exception ex)
            {
                if (_running) _main.Enqueue(() => Debug.LogWarning($"[MultiSerial] Read error: {ex.Message}"));
                Thread.Sleep(50);
            }
        }
    }

    // ---------- Parsing helpers ----------
    private bool TryParseKeyValueLine(string line, out List<KeyValuePair<string, float>> kvs)
    {
        kvs = null;
        // Fast check: contains '='
        if (!line.Contains("=")) return false;

        var list = new List<KeyValuePair<string, float>>();
        // Split by comma or whitespace
        var tokens = line.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var t in tokens)
        {
            var kv = t.Split('=');
            if (kv.Length != 2) continue;

            string key = kv[0].Trim();
            string val = kv[1].Trim();

            if (string.IsNullOrEmpty(key)) continue;
            if (allowedSensorIds != null && allowedSensorIds.Count > 0 &&
                !allowedSensorIds.Contains(key, StringComparer.OrdinalIgnoreCase))
            {
                continue; // not in whitelist
            }

            if (!float.TryParse(val, out float numeric)) continue;
            float cm = (numericInputUnit == InputUnit.Millimeters) ? (numeric / 10f) : numeric;

            list.Add(new KeyValuePair<string, float>(key, cm));
        }

        if (list.Count == 0) return false;
        kvs = list;
        return true;
    }

    // ---------- Main-thread handlers ----------
    private void HandleLegacyLiteral(string src)
    {
        if (logAllSerial)
            Debug.Log($"[MultiSerial] {Time.realtimeSinceStartup:F3}s <- '{src}' (legacy true)");
        TryFireAnyTrigger("(legacy)", float.NaN);
    }

    private void HandleSensorValue(string sensorId, float cm, string rawLine)
    {
        if (logAllSerial)
            Debug.Log($"[MultiSerial] {Time.realtimeSinceStartup:F3}s <- {rawLine}  [{sensorId}≈{cm:F1}cm]");

        OnSensorParsed?.Invoke(sensorId, cm);

        var s = GetOrCreateSensor(sensorId);
        float now = Time.realtimeSinceStartup;

        // Anti-glitch: cap per-sample jump (pre-smoothing)
        float input = cm;
        if (s.hasValue && maxStepPerSampleCM > 0f)
        {
            float prev = s.smoothedCm;
            float delta = input - prev;
            if (Mathf.Abs(delta) > maxStepPerSampleCM)
                input = prev + Mathf.Sign(delta) * maxStepPerSampleCM;
        }

        // EMA smoothing with time-based alpha
        float dt = s.hasValue ? Mathf.Max(0.0001f, now - s.lastSampleTime) : 0.0f;
        float alpha = 1f;
        if (smoothingSeconds > 0.0001f && dt > 0f)
        {
            // alpha = 1 - exp(-dt/tau)
            alpha = 1f - Mathf.Exp(-dt / Mathf.Max(0.0001f, smoothingSeconds));
        }
        float newSmoothed = s.hasValue ? (s.smoothedCm + alpha * (input - s.smoothedCm)) : input;

        s.smoothedCm = newSmoothed;
        s.lastSampleTime = now;
        s.hasValue = true;

        if (logPerSensor)
            Debug.Log($"[MultiSerial:{sensorId}] raw={cm:F1}cm, inputAfterCap={input:F1}cm, smoothed={s.smoothedCm:F1}cm");

        OnSensorSmoothed?.Invoke(sensorId, s.smoothedCm);

        // Evaluate condition
        bool condition = lessThanTriggers ? (s.smoothedCm <= triggerThresholdCM)
                                          : (s.smoothedCm >= triggerThresholdCM);
        if (condition)
            s.consecutiveTrue++;
        else
            s.consecutiveTrue = 0;

        if (s.consecutiveTrue >= Mathf.Max(1, minConsecutiveSamplesToTrigger))
        {
            // Global cooldown
            if (Time.realtimeSinceStartup - _lastAnyTriggerAt >= retriggerCooldownSec)
            {
                _lastAnyTriggerAt = Time.realtimeSinceStartup;

                if (logTriggers)
                    Debug.Log($"[MultiSerial] TRIGGER by '{sensorId}' @ {s.smoothedCm:F1}cm (thr {triggerThresholdCM}cm)");

                OnSensorTrigger?.Invoke(sensorId, s.smoothedCm);
                OnAnyTrigger?.Invoke(sensorId, s.smoothedCm);
            }
            // keep counting, or reset to allow new triggers after cooldown?
            // Here we reset to avoid immediate re-trigger on the next sample:
            s.consecutiveTrue = 0;
        }
    }

    private SensorState GetOrCreateSensor(string id)
    {
        if (!_sensors.TryGetValue(id, out var s))
        {
            s = new SensorState { id = id, hasValue = false, smoothedCm = 0f, lastSampleTime = Time.realtimeSinceStartup, consecutiveTrue = 0 };
            _sensors[id] = s;
        }
        return s;
    }

    // ---------- Public helpers ----------
    public bool TryGetSmoothed(string sensorId, out float cm)
    {
        if (_sensors.TryGetValue(sensorId, out var s) && s.hasValue)
        {
            cm = s.smoothedCm;
            return true;
        }
        cm = 0f;
        return false;
    }

    public float? GetAverageSmoothedAllSensors()
    {
        float sum = 0f; int n = 0;
        foreach (var kv in _sensors)
        {
            if (kv.Value.hasValue) { sum += kv.Value.smoothedCm; n++; }
        }
        if (n == 0) return null;
        return sum / n;
    }

    private void TryFireAnyTrigger(string sensorId, float cm)
    {
        if (Time.realtimeSinceStartup - _lastAnyTriggerAt >= retriggerCooldownSec)
        {
            _lastAnyTriggerAt = Time.realtimeSinceStartup;
            if (logTriggers)
                Debug.Log($"[MultiSerial] TRIGGER (legacy) by {sensorId}");
            OnAnyTrigger?.Invoke(sensorId, cm);
        }
    }

}

    
