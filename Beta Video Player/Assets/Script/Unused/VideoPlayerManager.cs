// using System.Collections;
// using UnityEngine;
// using UnityEngine.Video;

// public class VideoPlayerManager : MonoBehaviour
// {
//     [Header("References")]
//     [SerializeField] private VideoPlayer videoPlayer;

//     [Header("Behavior")]
//     [Tooltip("Seconds to ignore new triggers after video completes.")]
//     [SerializeField] private float cooldownSeconds = 4f;

//     [Tooltip("Treat 'false' messages as stop /")]
//     [SerializeField] private bool reactToFalseAsStop = false;

//     [SerializeField] private bool _busy;             // playing or in cooldown
//     private bool _subscribed;       // guard for duplicate subscriptions

//     [Header("Clear Last Frame")]
//     [Tooltip("When true, attempt to remove the last video frame (clear textures) when playback finishes or is stopped.")]
//     [SerializeField] private bool clearLastFrame = true;


//     // Saved camera alpha (used when VideoPlayer is in Camera Far/Near Plane render mode)
//     private float _savedCameraAlpha = 1f;



//     void Reset()
//     {
//         videoPlayer = GetComponent<VideoPlayer>();
//     }

//     void OnEnable()
//     {
//         TrySubscribe();
//     }

//     void Start()
//     {
//         TrySubscribe();
//         if (videoPlayer != null)
//         {
//             // Ensure we only subscribe once
//             videoPlayer.loopPointReached -= OnVideoPlayerLoopPointReached;
//             videoPlayer.loopPointReached += OnVideoPlayerLoopPointReached;
//             // Capture initial camera alpha for Camera render modes so we can restore it later
//             try
//             {
//                 if (videoPlayer.renderMode == VideoRenderMode.CameraFarPlane || videoPlayer.renderMode == VideoRenderMode.CameraNearPlane)
//                     _savedCameraAlpha = videoPlayer.targetCameraAlpha;
//             }
//             catch { }
//         }
//     }

//     void OnDisable()
//     {
//         if (UdpBooleanListener.Instance != null)
//             UdpBooleanListener.Instance.OnBooleanReceived -= OnBoolean;
//         if (SerialTriggerReader.Instance != null)
//             SerialTriggerReader.Instance.OnPortReadTrue -= OnBoolean;
//         _subscribed = false;
//         if (videoPlayer != null)
//             videoPlayer.loopPointReached -= OnVideoPlayerLoopPointReached;
//     }

//     private void TrySubscribe()
//     {
//         if (_subscribed) return;

//         var didSubscribe = false;

//         // Subscribe to UDP boolean listener if available
//         if (UdpBooleanListener.Instance != null)
//         {
//             UdpBooleanListener.Instance.OnBooleanReceived += OnBoolean;
//             didSubscribe = true;
//         }

//         // Subscribe to serial reader instance event if present
//         if (SerialTriggerReader.Instance != null)
//         {
//             SerialTriggerReader.Instance.OnPortReadTrue += OnBoolean;
//             didSubscribe = true;
//         }

//         // Only mark subscribed if at least one subscription occurred
//         if (didSubscribe) _subscribed = true;
//     }

//     void Update()
//     {
//         // If we failed to subscribe earlier (e.g. SerialTriggerReader or UDP listener initialized later), retry.
//         if (!_subscribed) TrySubscribe();
//     }            

//     private void OnBoolean(bool value)
//     {
//         if (_busy) return; // ignore any triggers while busy
//         Debug.Log($"[VideoPlayerManager] Received boolean: {value}");
//         if (value)
//         {
//             // Start playing flow
//             StartCoroutine(PlayThenCooldown());
//         }
//         else if (reactToFalseAsStop)
//         {
//             // Optional: stop immediately on false
//             StopAllCoroutines();
//             if (videoPlayer != null && videoPlayer.isPlaying)
//                 videoPlayer.Stop();
//             _busy = false; // allow triggers again immediately or set a short cooldown if desired
//         }
//     }

//         private void OnVideoPlayerLoopPointReached(VideoPlayer vp)
//         {
//             if (clearLastFrame)
//                 ClearLastFrame();
//         }

//         private void ClearLastFrame()
//         {
//             try
//             {
//                 if (videoPlayer != null && videoPlayer.targetTexture != null)
//                 {
//                     videoPlayer.targetTexture.Release();
//                     videoPlayer.targetTexture = null;
//                 }
//             }
//             catch { }
//             // If the VideoPlayer rendered to the camera plane, set alpha to 0 to hide it
//             try
//             {
//                 if (videoPlayer != null && (videoPlayer.renderMode == VideoRenderMode.CameraFarPlane || videoPlayer.renderMode == VideoRenderMode.CameraNearPlane))
//                 {
//                     videoPlayer.targetCameraAlpha = 0f;
//                 }
//             }
//             catch { }

//         }

//     private IEnumerator PlayThenCooldown()
//     {
//         if (videoPlayer == null) yield break;

//         _busy = true;

//         // Restore camera alpha if we previously hid the camera plane
//         try
//         {
//             if (videoPlayer != null && (videoPlayer.renderMode == VideoRenderMode.CameraFarPlane || videoPlayer.renderMode == VideoRenderMode.CameraNearPlane))
//                 videoPlayer.targetCameraAlpha = _savedCameraAlpha;
//         }
//         catch { }

//         // Start from beginning, then play
//         videoPlayer.time = 0;
//         videoPlayer.Play();

//         // Wait until it actually starts (prepare phase)
//         while (!videoPlayer.isPlaying)
//             yield return null;

//         // Wait for video to finish
//         // Note: isPlaying becomes false when finished or stopped
//         while (videoPlayer.isPlaying)
//             yield return null;

//         // Cooldown
//         if (cooldownSeconds > 0f)
//             yield return new WaitForSecondsRealtime(cooldownSeconds);

//         _busy = false;
//     }
// }
