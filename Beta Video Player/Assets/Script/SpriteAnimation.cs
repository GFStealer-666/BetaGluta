using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class SpriteAnimation : MonoBehaviour
{
    [SerializeField] private BatterySensorDrivenUI source;
    [SerializeField] private GameObject imageA;   // HeartScaleup
    [SerializeField] private GameObject imageB;   // HeartScaledown
    [SerializeField] private float frequencyHz = 6f;

    private Coroutine runner;

    private void Reset()
    {
        if (!source) source = GetComponentInParent<BatterySensorDrivenUI>();
    }

    private void OnEnable()
    {
        if (!source) source = GetComponentInParent<BatterySensorDrivenUI>();
        HideBoth();

        source.OnHoldPhaseChanged += HandleHoldChanged;

        // start immediately if we enabled during hold
        if (source.IsHoldPhase) StartBlink();
    }

    private void OnDisable()
    {
        if (source) source.OnHoldPhaseChanged -= HandleHoldChanged;
        StopBlink();
        HideBoth();
    }

    private void HandleHoldChanged(bool active)
    {
        if (active) StartBlink();
        else        StopBlink();
    }

    private void StartBlink()
    {
        if (runner != null) return;
        runner = StartCoroutine(CoBlink());
    }

    private void StopBlink()
    {
        if (runner == null) return;
        StopCoroutine(runner);
        runner = null;
        HideBoth();
    }

    private IEnumerator CoBlink()
    {
        float half = 0.5f / Mathf.Max(0.0001f, frequencyHz);
        bool flip = false;
        while (true) // runs only during hold-phase
        {
            flip = !flip;
            SetActive(imageA,  flip);
            SetActive(imageB, !flip);
            yield return new WaitForSeconds(half);
        }
    }

    private void HideBoth()
    {
        SetActive(imageA, false);
        SetActive(imageB, false);
    }

    private static void SetActive(GameObject go, bool on)
    {
        if (go && go.activeSelf != on) go.SetActive(on);
    }
}
