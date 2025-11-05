using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using UnityEngine;

public class UdpBooleanListener : MonoBehaviour
{
    public static UdpBooleanListener Instance { get; private set; }

    [Header("UDP Settings")]
    [Tooltip("Must match the ESP32 UNITY_PORT")]
    public int listenPort = 2000;

    [Header("Debug")]
    public bool logIncoming = true;
    [Range(0f, 2f)] public float logThrottleSeconds = 0.25f;

    private UdpClient udpClient;
    private CancellationTokenSource cts;

    // Thread-safe handoff from socket thread -> Unity main thread
    private readonly ConcurrentQueue<(IPEndPoint ep, bool value, string raw)> _boolQueue =
        new ConcurrentQueue<(IPEndPoint, bool, string)>();

    private float _lastLogTime;

    /// <summary>
    /// Event fired on the main thread when a boolean arrives.
    /// </summary>
    public event Action<bool> OnBooleanReceived;

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
            udpClient = new UdpClient(AddressFamily.InterNetwork);
            udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            udpClient.Client.ExclusiveAddressUse = false;
#endif
            udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, listenPort));
            cts = new CancellationTokenSource();
            _ = ReceiveLoopAsync(cts.Token);
            Debug.Log($"[UDP] Listening on 0.0.0.0:{listenPort}");
        }
        catch (Exception ex)
        {
            Debug.LogError("[UDP] Failed to start listener: " + ex);
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var result = await udpClient.ReceiveAsync();
                var msg = Encoding.UTF8.GetString(result.Buffer).Trim();

                if (TryParseBool(msg, out bool value))
                {
                    _boolQueue.Enqueue((result.RemoteEndPoint, value, msg));
                }
                else
                {
                    // Not a pure bool? Try to sniff common patterns e.g. DOG_TEST_TRIGGER|...=1
                    if (TryExtractBoolFromComplex(msg, out bool complexVal))
                        _boolQueue.Enqueue((result.RemoteEndPoint, complexVal, msg));
                    else
                        _boolQueue.Enqueue((result.RemoteEndPoint, false, msg)); // fall back if you prefer
                }
            }
            catch (ObjectDisposedException) { /* socket closed during quit */ }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested)
                    Debug.LogWarning("[UDP] Receive error: " + ex.Message);
                await Task.Delay(50, token);
            }
        }
    }

    void Update()
    {
        // Drain queue on main thread and invoke event
        while (_boolQueue.TryDequeue(out var item))
        {
            if (logIncoming && Time.realtimeSinceStartup - _lastLogTime >= logThrottleSeconds)
            {
                _lastLogTime = Time.realtimeSinceStartup;
                Debug.Log($"[UDP] {item.ep.Address}:{item.ep.Port} -> \"{item.raw}\" => {item.value}");
            }
            OnBooleanReceived?.Invoke(item.value);
        }
    }

    void OnDestroy()
    {
        try { cts?.Cancel(); } catch { }
        try { udpClient?.Close(); } catch { }
        try { udpClient?.Dispose(); } catch { }
    }

    
    // Parsing Boolean 
    private static bool TryParseBool(string s, out bool value)
    {
        // Accept true/false, TRUE/FALSE, 1/0
        if (bool.TryParse(s, out value))
            return true;

        if (s == "1" || s.Equals("on", StringComparison.OrdinalIgnoreCase))
        { value = true; return true; }

        if (s == "0" || s.Equals("off", StringComparison.OrdinalIgnoreCase))
        { value = false; return true; }

        return false;
    }

    private static bool TryExtractBoolFromComplex(string s, out bool value)
    {
        // Accept patterns like: DOG_TEST_TRIGGER|seq=12|value=1  OR ...|true
        // Greedy but safe:
        var lower = s.ToLowerInvariant();

        // direct tokens
        if (lower.Contains("true")) { value = true; return true; }
        if (lower.Contains("false")) { value = false; return true; }

        // key=value pairs
        // find "=1" or "=0"
        int idx = lower.LastIndexOf('=');
        if (idx >= 0 && idx + 1 < lower.Length)
        {
            var tail = lower.Substring(idx + 1).Trim();
            if (tail.StartsWith("1")) { value = true; return true; }
            if (tail.StartsWith("0")) { value = false; return true; }
        }

        value = false;
        return false;
    }
}
