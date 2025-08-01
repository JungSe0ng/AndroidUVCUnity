using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Android;
using UnityEngine.UI;
using System.Linq;

public class UVCCameraManager : MonoBehaviour
{
    [System.Serializable]
    public class CameraRenderTarget
    {
        public string cameraName = "";
        public RawImage targetRawImage;
        public Texture2D cameraTexture;
        public bool isActive = false;
        public int width = 1920;
        public int height = 1080;
        public int fps = 30;
        public Coroutine renderCoroutine;
    }

    AndroidJavaObject plugin;
    AndroidJavaObject activity;
    AndroidJavaClass unityPlayer;

    [Header("Camera Settings")]
    [SerializeField] private int maxCameras = 2;
    [SerializeField] private CameraRenderTarget[] cameraTargets = new CameraRenderTarget[2];

    [Header("UI Controls")]
    [SerializeField] private Button reconnectButton;
    [SerializeField] private Text statusText;

    private List<string> availableCameras = new List<string>();
    private bool isInitialized = false;
    private bool isReconnecting = false;

    void Start()
    {
        Debug.Log("[MultiCamera] Start");

        // CameraRenderTarget �迭 �ʱ�ȭ
        if (cameraTargets == null || cameraTargets.Length != maxCameras)
        {
            cameraTargets = new CameraRenderTarget[maxCameras];
            for (int i = 0; i < maxCameras; i++)
            {
                cameraTargets[i] = new CameraRenderTarget();
            }
        }

        SetupUI();
        StartCoroutine(InitializeWithRetry());

        InvokeRepeating("GetCameraStatus", 10f,3f);
    }

    void SetupUI()
    {
        if (reconnectButton != null)
        {
            reconnectButton.onClick.AddListener(() => {
                if (!isReconnecting)
                {
                    StartCoroutine(ReconnectCameras());
                }
            });
        }
        UpdateStatusText("�ʱ�ȭ ��...");
    }

    void UpdateStatusText(string message)
    {
        if (statusText != null)
        {
            statusText.text = $"[{System.DateTime.Now:HH:mm:ss}] {message}";
        }
        Debug.Log($"[MultiCamera] {message}");
    }

    IEnumerator InitializeWithRetry()
    {
        UpdateStatusText("���� ��û ��...");
        RequestAndroidPermissions();
        yield return new WaitForSeconds(2f);

        if (!isInitialized)
        {
            UpdateStatusText("�÷����� �ʱ�ȭ ��...");
            InitializePlugin();
            yield return new WaitForSeconds(1f);
            isInitialized = true;
        }

        UpdateStatusText("ī�޶� �˻� ��...");
        yield return StartCoroutine(FindCamerasAndStart());
    }

    void RequestAndroidPermissions()
    {
        string[] requiredPermissions = {
           "horizonos.permission.USB_CAMERA",
           Permission.Camera,
           "android.permission.CAMERA"
       };

        foreach (string permission in requiredPermissions)
        {
            if (!Permission.HasUserAuthorizedPermission(permission))
            {
                Permission.RequestUserPermission(permission);
                Debug.Log($"[MultiCamera] {permission} ���� ��û");
            }
            else
            {
                Debug.Log($"[MultiCamera] {permission} ���� �̹� ����");
            }
        }
    }

    void InitializePlugin()
    {
        try
        {
            Debug.Log("[MultiCamera] Initializing UVC Plugin...");

            plugin = new AndroidJavaObject("edu.uga.engr.vel.unityuvcplugin.UnityUVCPlugin");
            unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            
            plugin.Call("Init", activity);
            
            Debug.Log("[MultiCamera] Plugin initialized successfully");
        }
        catch (Exception e)
        {
            Debug.LogError("[MultiCamera] Plugin initialization failed: " + e.Message);
        }
    }

    IEnumerator FindCamerasAndStart()
    {
        string[] cameras = null;
        int retryCount = 0;
        const int maxRetries = 10;

        while (retryCount < maxRetries)
        {
            try
            {
                cameras = plugin.Call<string[]>("GetUSBDevices");
                if (cameras != null && cameras.Length > 0)
                {
                    UpdateStatusText($"�߰ߵ� ī�޶� ��: {cameras.Length}");
                    availableCameras.Clear();
                    for (int i = 0; i < cameras.Length; i++)
                    {
                        Debug.Log($"[MultiCamera] ī�޶� {i}: {cameras[i]}");
                        availableCameras.Add(cameras[i]);
                    }
                    break;
                }
                else
                {
                    UpdateStatusText($"ī�޶� �˻� ��... ({retryCount + 1}/{maxRetries})");
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[MultiCamera] GetUSBDevices failed: " + e.Message);
                UpdateStatusText($"ī�޶� �˻� ����: {e.Message}");
            }

            retryCount++;
            yield return new WaitForSeconds(1f);
        }

        if (cameras == null || cameras.Length == 0)
        {
            UpdateStatusText("ī�޶� ã�� �� �����ϴ�. �翬�� ��ư�� �����ּ���.");
            yield break;
        }

        int camerasToStart = Mathf.Min(cameras.Length, maxCameras);
        UpdateStatusText($"{camerasToStart}�� ī�޶� ���� ����...");

        int successCount = 0;
        for (int i = 0; i < camerasToStart; i++)
        {
            cameraTargets[i].cameraName = cameras[i];
            bool success = false;
            yield return StartCoroutine(SetupCamera(i, (result) => success = result));
            if (success) successCount++;
            yield return new WaitForSeconds(1f);
        }

        UpdateStatusText($"ī�޶� ���� �Ϸ�: {successCount}/{camerasToStart}�� ����");
    }

    IEnumerator SetupCamera(int cameraIndex, System.Action<bool> onComplete = null)
    {
        CameraRenderTarget target = cameraTargets[cameraIndex];
        string cameraName = target.cameraName;

        Debug.Log($"[MultiCamera] ī�޶� {cameraIndex} ({cameraName}) ���� ����");
        UpdateStatusText($"ī�޶� {cameraIndex + 1} ���� ��...");

        yield return StartCoroutine(RequestPermissionAndCheck(cameraName, cameraIndex));

        if (!HasCameraPermission(cameraName))
        {
            Debug.LogError($"[MultiCamera] ī�޶� {cameraIndex} ({cameraName}) ���� ȹ�� ����");
            UpdateStatusText($"ī�޶� {cameraIndex + 1} ���� ����");
            if (onComplete != null) onComplete(false);
            yield break;
        }

        bool success = false;
        yield return StartCoroutine(RunCamera(cameraIndex, (result) => success = result));
        if (onComplete != null) onComplete(success);
    }

    IEnumerator RequestPermissionAndCheck(string cameraName, int cameraIndex)
    {
        Debug.Log($"[MultiCamera] ī�޶� {cameraIndex} ({cameraName})�� ���� ���� ��û ����");

        try
        {
            plugin.Call("ObtainPermission", cameraName);
            Debug.Log($"[MultiCamera] ī�޶� {cameraIndex} ({cameraName}) ���� ��û �Ϸ�");
        }
        catch (Exception e)
        {
            Debug.LogError($"[MultiCamera] ī�޶� {cameraIndex} ���� ��û ����: {e.Message}");
        }

        int permissionRetries = 0;
        const int maxPermissionRetries = 6;

        while (permissionRetries < maxPermissionRetries)
        {
            try
            {
                bool hasPermission = plugin.Call<bool>("hasPermission", cameraName);
                if (hasPermission)
                {
                    Debug.Log($"[MultiCamera] ī�޶� {cameraIndex} ({cameraName}) ���� ȹ�� ����!");
                    yield break;
                }
                else
                {
                    Debug.Log($"[MultiCamera] ī�޶� {cameraIndex} ({cameraName}) ���� ��� ��... ({permissionRetries + 1}/{maxPermissionRetries})");

                    if (permissionRetries % 2 == 1)
                    {
                        plugin.Call("ObtainPermission", cameraName);
                        Debug.Log($"[MultiCamera] ī�޶� {cameraIndex} ({cameraName}) ���� ���û");
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[MultiCamera] ī�޶� {cameraIndex} ���� Ȯ�� ����: {e.Message}");
            }

            permissionRetries++;
            yield return new WaitForSeconds(3f);
        }
    }

    bool HasCameraPermission(string cameraName)
    {
        try
        {
            return plugin.Call<bool>("hasPermission", cameraName);
        }
        catch (Exception e)
        {
            Debug.LogError($"[MultiCamera] ���� Ȯ�� ����: {e.Message}");
            return false;
        }
    }

    IEnumerator RunCamera(int cameraIndex, System.Action<bool> onComplete = null)
    {
        CameraRenderTarget target = cameraTargets[cameraIndex];
        string cameraName = target.cameraName;

        Debug.Log($"[MultiCamera] ī�޶� {cameraIndex} ({cameraName}) ����");

        try
        {
            string deviceInfo = plugin.Call<string>("GetUSBDeviceInfo", cameraName);
            Debug.Log($"[MultiCamera] ī�޶� {cameraIndex} ����̽� ����:\n{deviceInfo}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[MultiCamera] ī�޶� {cameraIndex} ����̽� ���� �������� ����: {e.Message}");
        }

        yield return new WaitForSeconds(1f);

        string[] infos = null;
        bool openSuccess = false;
        int openRetries = 0;
        const int maxOpenRetries = 3;

        while (!openSuccess && openRetries < maxOpenRetries)
        {
            try
            {
                Debug.Log($"[MultiCamera] ī�޶� {cameraIndex} ({cameraName}) ���� �õ� {openRetries + 1}/{maxOpenRetries}");
                infos = plugin.Call<string[]>("Open", cameraName);
                openSuccess = (infos != null && infos.Length > 0);

                if (openSuccess)
                {
                    Debug.Log($"[MultiCamera] ī�޶� {cameraIndex} Open ����! ��� ������ ���� ��: {infos.Length}");
                }
                else
                {
                    Debug.LogWarning($"[MultiCamera] ī�޶� {cameraIndex} Open ���� - null �Ǵ� �� �迭 ��ȯ");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[MultiCamera] ī�޶� {cameraIndex} Open �õ� {openRetries + 1} ����: {e.Message}");
                openSuccess = false;
            }

            openRetries++;
            if (!openSuccess && openRetries < maxOpenRetries)
            {
                yield return new WaitForSeconds(openRetries);
            }
        }

        if (!openSuccess || infos == null)
        {
            Debug.LogError($"[MultiCamera] ī�޶� {cameraIndex} Open ����");
            UpdateStatusText($"ī�޶� {cameraIndex + 1} ���� ����");
            if (onComplete != null) onComplete(false);
            yield break;
        }

        bool streamSuccess = false;
        yield return StartCoroutine(StartCameraStream(cameraIndex, (result) => streamSuccess = result));

        if (streamSuccess)
        {
            UpdateStatusText($"ī�޶� {cameraIndex + 1} ���� ����");
        }

        if (onComplete != null) onComplete(streamSuccess);
    }

    IEnumerator StartCameraStream(int cameraIndex, System.Action<bool> onComplete = null)
    {
        CameraRenderTarget target = cameraTargets[cameraIndex];
        string cameraName = target.cameraName;

        Debug.Log($"[MultiCamera] ī�޶� {cameraIndex} ��Ʈ�� ����");

        bool startSuccess = false;
        int width = 1920, height = 1080, fps = 30;
        int format = 9; // UVC_FRAME_FORMAT_MJPEG
        float bandwidth = 0.3f;

        Debug.Log($"[MultiCamera] ī�޶� {cameraIndex} MJPEG 1080p �õ�: {width}x{height}@{fps}fps, Format {format}, BW {bandwidth}");

        int res = plugin.Call<int>("Start", cameraName, width, height, fps, format, bandwidth, true, false);

        if (res == 0)
        {
            Debug.Log($"[MultiCamera] ī�޶� {cameraIndex} MJPEG 1080p ����!");
            startSuccess = true;
        }
        else
        {
            Debug.LogError($"[MultiCamera] ī�޶� {cameraIndex} MJPEG 1080p ���� (����: {res})");

            width = 1280; height = 720;
            Debug.Log($"[MultiCamera] ī�޶� {cameraIndex} MJPEG 720p fallback �õ�");

            res = plugin.Call<int>("Start", cameraName, width, height, fps, format, bandwidth, true, false);

            if (res == 0)
            {
                Debug.Log($"[MultiCamera] ī�޶� {cameraIndex} MJPEG 720p ����!");
                startSuccess = true;
            }
            else
            {
                Debug.LogError($"[MultiCamera] ī�޶� {cameraIndex} MJPEG 720p ���� (����: {res})");

                // 480p�� ���� fallback
                width = 640; height = 480;
                Debug.Log($"[MultiCamera] ī�޶� {cameraIndex} MJPEG 480p ���� �õ�");

                res = plugin.Call<int>("Start", cameraName, width, height, fps, format, bandwidth, true, false);

                if (res == 0)
                {
                    Debug.Log($"[MultiCamera] ī�޶� {cameraIndex} MJPEG 480p ����!");
                    startSuccess = true;
                }
                else
                {
                    Debug.LogError($"[MultiCamera] ī�޶� {cameraIndex} ��� �ػ� ���� (����: {res})");
                }
            }
        }

        if (!startSuccess)
        {
            Debug.LogError($"[MultiCamera] ī�޶� {cameraIndex} ��Ʈ�� ���� ����");
            if (onComplete != null) onComplete(false);
            yield break;
        }

        // �ؽ�ó ����
        if (target.cameraTexture != null)
        {
            Destroy(target.cameraTexture);
        }

        target.cameraTexture = new Texture2D(width, height, TextureFormat.RGB24, false);
        target.width = width;
        target.height = height;
        target.fps = fps;
        target.isActive = true;

        if (target.targetRawImage != null)
        {
            target.targetRawImage.texture = target.cameraTexture;
            Debug.Log($"[MultiCamera] ī�޶� {cameraIndex} �ؽ�ó ���� �Ϸ�: {width}x{height}");
        }
        else
        {
            Debug.LogError($"[MultiCamera] ī�޶� {cameraIndex} RawImage�� �Ҵ���� �ʾҽ��ϴ�!");
            if (onComplete != null) onComplete(false);
            yield break;
        }

        target.renderCoroutine = StartCoroutine(RenderCameraFrames(cameraIndex));
        if (onComplete != null) onComplete(true);
    }

    IEnumerator RenderCameraFrames(int cameraIndex)
    {
        CameraRenderTarget target = cameraTargets[cameraIndex];
        string cameraName = target.cameraName;

        int frameCount = 0;
        int errorCount = 0;
        const int maxErrors = 10;

        Debug.Log($"[MultiCamera] ī�޶� {cameraIndex} ������ ������ ����");

        while (target.isActive && errorCount < maxErrors)
        {
            sbyte[] frameData = null;
            bool frameSuccess = false;

            try
            {
                frameData = plugin.Call<sbyte[]>("GetFrameData", cameraName);
                frameSuccess = true;
            }
            catch (Exception e)
            {
                errorCount++;
                Debug.LogError($"[MultiCamera] ī�޶� {cameraIndex} ������ ������ ���� ({errorCount}/{maxErrors}): {e.Message}");
                frameSuccess = false;
            }

            if (frameSuccess && frameData != null && frameData.Length > 0)
            {
                // 60�����Ӹ��� �� ���� ������ ���� �α�
                if (frameCount % 60 == 0)
                {
                    Debug.Log($"[MultiCamera] ī�޶� {cameraIndex} ������ ������: ũ��={frameData.Length}, ù 10����Ʈ: [{string.Join(",", frameData.Take(10))}]");
                }

                try
                {
                    target.cameraTexture.LoadRawTextureData((byte[])(System.Array)frameData);
                    target.cameraTexture.Apply(false, false);

                    frameCount++;
                    if (frameCount % 60 == 0)
                    {
                        Debug.Log($"[MultiCamera] ī�޶� {cameraIndex} ������ {frameCount} ó����");
                    }

                    errorCount = 0;
                }
                catch (Exception e)
                {
                    errorCount++;
                    Debug.LogError($"[MultiCamera] ī�޶� {cameraIndex} �ؽ�ó ������Ʈ ���� ({errorCount}/{maxErrors}): {e.Message}");
                }
            }
            else if (frameSuccess)
            {
                errorCount++;
                Debug.LogWarning($"[MultiCamera] ī�޶� {cameraIndex} �� ������ ������ ({errorCount}/{maxErrors}), ����: {frameData?.Length ?? -1}");
            }

            if (!frameSuccess || (frameData == null || frameData.Length == 0))
            {
                if (errorCount >= maxErrors)
                {
                    Debug.LogError($"[MultiCamera] ī�޶� {cameraIndex} �ʹ� ���� ������ ����. ����.");
                    break;
                }
                yield return new WaitForSeconds(0.1f);
            }
            else
            {
                yield return null;
            }
        }

        Debug.LogError($"[MultiCamera] ī�޶� {cameraIndex} ��Ʈ���� �ߴܵ�");
        target.isActive = false;
    }

    public void GetCameraStatus()
    {
        string statusMessage = "=== ī�޶� ���� ===\n";

        for (int i = 0; i < cameraTargets.Length; i++)
        {
            CameraRenderTarget target = cameraTargets[i];
            statusMessage += $"ī�޶� {i}: {target.cameraName}, Ȱ��: {target.isActive}, �ػ�: {target.width}x{target.height}\n";

            if (!string.IsNullOrEmpty(target.cameraName))
            {
                try
                {
                    int frameNumber = plugin.Call<int>("GetFrameNumber", target.cameraName);
                    statusMessage += $"  ������ ��ȣ: {frameNumber}\n";

                    sbyte[] testFrame = plugin.Call<sbyte[]>("GetFrameData", target.cameraName);
                    if (testFrame != null)
                    {
                        statusMessage += $"  ������ ũ��: {testFrame.Length}\n";

                        bool hasNonZero = false;
                        for (int j = 0; j < Math.Min(100, testFrame.Length); j++)
                        {
                            if (testFrame[j] != 0)
                            {
                                hasNonZero = true;
                                break;
                            }
                        }
                        statusMessage += $"  ������� ���� ������: {hasNonZero}\n";
                    }
                }
                catch (System.Exception e)
                {
                    statusMessage += $"  �÷����� ����: {e.Message}\n";
                }
            }
        }
        statusMessage += "==================";

        Debug.Log(statusMessage);
        UpdateStatusText("ī�޶� ���� Ȯ�ε�");
    }


    public IEnumerator ReconnectCameras()
    {
        if (isReconnecting)
        {
            UpdateStatusText("�̹� �翬�� ���Դϴ�...");
            yield break;
        }

        isReconnecting = true;
        UpdateStatusText("��Ȱ�� ī�޶� �翬�� ��...");

        // 1. ���� ��� ������ ī�޶� ��� �ٽ� ��������
        string[] currentCameras = null;
        try
        {
            currentCameras = plugin.Call<string[]>("GetUSBDevices");
            if (currentCameras != null)
            {
                UpdateStatusText($"�߰ߵ� ī�޶� ��: {currentCameras.Length}");
                availableCameras.Clear();
                availableCameras.AddRange(currentCameras);
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[MultiCamera] ī�޶� ��� �������� ����: {e.Message}");
            UpdateStatusText("ī�޶� ��� �������� ����");
            isReconnecting = false;
            yield break;
        }

        if (currentCameras == null || currentCameras.Length == 0)
        {
            UpdateStatusText("���� ������ ī�޶� �����ϴ�.");
            isReconnecting = false;
            yield break;
        }

        int reconnectedCount = 0;
        for (int i = 0; i < cameraTargets.Length; i++)
        {
            CameraRenderTarget target = cameraTargets[i];

            if (target.isActive && target.cameraTexture != null && !string.IsNullOrEmpty(target.cameraName))
            {
                Debug.Log($"[MultiCamera] ī�޶� {i} ({target.cameraName})�� �̹� Ȱ�� ���� - �ǳʶ�");
                continue;
            }

            if (i < currentCameras.Length)
            {
                if (!string.IsNullOrEmpty(target.cameraName))
                {
                    StopSingleCamera(i);
                    yield return new WaitForSeconds(1f);
                }

                target.cameraName = currentCameras[i];
                UpdateStatusText($"ī�޶� {i + 1} �翬�� �õ�: {target.cameraName}");

                bool success = false;
                yield return StartCoroutine(SetupCamera(i, (result) => success = result));

                if (success)
                {
                    reconnectedCount++;
                    UpdateStatusText($"ī�޶� {i + 1} �翬�� ����");
                }
                else
                {
                    UpdateStatusText($"ī�޶� {i + 1} �翬�� ����");
                }

                yield return new WaitForSeconds(1f);
            }
        }

        UpdateStatusText($"�翬�� �Ϸ�: {reconnectedCount}�� ī�޶� �翬���");
        isReconnecting = false;
    }

    void StopSingleCamera(int cameraIndex)
    {
        if (cameraIndex < 0 || cameraIndex >= cameraTargets.Length) return;

        CameraRenderTarget target = cameraTargets[cameraIndex];
        if (target == null) return;

        Debug.Log($"[MultiCamera] ī�޶� {cameraIndex} ({target.cameraName}) ���� ����");

        target.isActive = false;

        if (target.renderCoroutine != null)
        {
            StopCoroutine(target.renderCoroutine);
            target.renderCoroutine = null;
        }

        if (target.cameraTexture != null)
        {
            if (target.targetRawImage != null)
            {
                target.targetRawImage.texture = null;
            }
            Destroy(target.cameraTexture);
            target.cameraTexture = null;
        }

        try
        {
            if (plugin != null && !string.IsNullOrEmpty(target.cameraName))
            {
                plugin.Call("Close", target.cameraName);
                Debug.Log($"[MultiCamera] ī�޶� {cameraIndex} ({target.cameraName}) �÷����� ���� �Ϸ�");
            }
        }
        catch (Exception e)
        {
            Debug.Log($"[MultiCamera] ī�޶� {cameraIndex} �÷����� ���� �õ�: {e.Message}");
        }

        // ���� �ʱ�ȭ
        target.cameraName = "";
        target.width = 1920;
        target.height = 1080;
        target.fps = 30;

        Debug.Log($"[MultiCamera] ī�޶� {cameraIndex} ���� �Ϸ�");
    }

    void StopAllCameras()
    {
        UpdateStatusText("��� ī�޶� ���� ��...");

        for (int i = 0; i < cameraTargets.Length; i++)
        {
            CameraRenderTarget target = cameraTargets[i];
            if (target != null)
            {
                target.isActive = false;

                if (target.renderCoroutine != null)
                {
                    StopCoroutine(target.renderCoroutine);
                    target.renderCoroutine = null;
                }

                if (target.cameraTexture != null)
                {
                    if (target.targetRawImage != null)
                    {
                        target.targetRawImage.texture = null;
                    }
                    Destroy(target.cameraTexture);
                    target.cameraTexture = null;
                }

                try
                {
                    if (plugin != null && !string.IsNullOrEmpty(target.cameraName))
                    {
                        plugin.Call("Close", target.cameraName);
                        Debug.Log($"[MultiCamera] ī�޶� {i} ({target.cameraName}) ���� �Ϸ�");
                    }
                }
                catch (Exception e)
                {
                    Debug.Log($"[MultiCamera] ī�޶� {i} ���� �õ�: {e.Message}");
                }

                target.cameraName = "";
                target.width = 1920;
                target.height = 1080;
                target.fps = 30;
            }
        }

        UpdateStatusText("��� ī�޶� ���� �Ϸ�");
    }

    void OnDestroy()
    {
        StopAllCameras();
    }

}