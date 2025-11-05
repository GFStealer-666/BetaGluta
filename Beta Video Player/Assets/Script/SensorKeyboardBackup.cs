using UnityEngine;

public class SensorKeyboardBackup : MonoBehaviour
{
    [Header("Target(s) to simulate pulses on")]
    public BatterySensorDrivenUI[] targets;

    [Header("Input")]
    public KeyCode key = KeyCode.S;
    [Tooltip("If true, holds the key will repeatedly pulse; if false, one pulse per key press.")]
    public bool holdToRepeat = true;

    [Header("Pulse Rate (when holding)")]
    [Tooltip("How often to send pulses while the key is held (seconds). " +
             "Choose <= your presenceTimeout to keep it 'present'.")]
    public float repeatInterval = 0.05f; // 20 Hz good default

    private float _nextPulseAt = 0f;

    void Update()
    {
        if (targets == null || targets.Length == 0) return;

        if (holdToRepeat)
        {
            if (Input.GetKey(key))
            {
                if (Time.time >= _nextPulseAt)
                {
                    PulseAll();
                    _nextPulseAt = Time.time + repeatInterval;
                }
            }
            else
            {
                // reset so the next hold fires immediately
                _nextPulseAt = 0f;
            }
        }
        else
        {
            if (Input.GetKeyDown(key))
                PulseAll();
        }
    }

    private void PulseAll()
    {
        for (int i = 0; i < targets.Length; i++)
        {
            if (targets[i] != null)
                targets[i].RegisterPresencePulse();
        }
    }
}
