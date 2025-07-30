using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Android;

[RequireComponent(typeof(MeshRenderer))]
public class MultiCameraExample : MonoBehaviour
{
    [System.Serializable]
    public class CameraRenderTarget
    {
        public string cameraName = "";
        public Renderer targetRenderer;
        public Texture2D cameraTexture;
        public bool isActive = false;
        public int width = 640;
        public int height = 480;
        public int fps = 30;
        public Coroutine renderCoroutine;
    }

    AndroidJavaObject plugin;
    AndroidJavaObject activity;
    AndroidJavaClass unityPlayer;

    private bool permissionRequested = false;

    [Header("Camera Settings")]
    [SerializeField] private int maxCameras = 2;
    [SerializeField] private CameraRenderTarget[] cameraTargets = new CameraRenderTarget[2];

    private List<string> availableCameras = new List<string>();

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

        // ù ��° ī�޶�� �ڱ� �ڽ��� ������ ���
        if (cameraTargets[0].targetRenderer == null)
        {
            cameraTargets[0].targetRenderer = GetComponent<Renderer>();
        }

        StartCoroutine(InitializeWithRetry());
    }

    IEnumerator InitializeWithRetry()
    {
        // ���� ��û
        RequestAndroidPermissions();
        yield return new WaitForSeconds(2f);

        // �÷����� �ʱ�ȭ
        InitializePlugin();
        yield return new WaitForSeconds(1f);

        // ī�޶� ã�� �� ����
        yield return StartCoroutine(FindCamerasAndStart());
    }

    void RequestAndroidPermissions()
    {
        // Quest 3 ���� ���ѵ�
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
            Debug.Log("plugin: " + plugin);

            unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            Debug.Log("unityPlayer: " + unityPlayer);

            activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            Debug.Log("activity: " + activity);

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
                    Debug.Log($"[MultiCamera] �߰ߵ� ī�޶� ��: {cameras.Length}");
                    for (int i = 0; i < cameras.Length; i++)
                    {
                        Debug.Log($"[MultiCamera] ī�޶� {i}: {cameras[i]}");
                        availableCameras.Add(cameras[i]);
                    }
                    break;
                }
                else
                {
                    Debug.Log($"[MultiCamera] ī�޶� �����ϴ�. �ٽ� �õ��մϴ�... ({retryCount + 1}/{maxRetries})");
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[MultiCamera] GetUSBDevices failed: " + e.Message);
            }

            retryCount++;
            yield return new WaitForSeconds(1f);
        }

        if (cameras == null || cameras.Length == 0)
        {
            Debug.LogError("[MultiCamera] ī�޶� ã�� �� �����ϴ�. �ߴ��մϴ�.");
            yield break;
        }

        // �ִ� maxCameras������ ī�޶� ����
        int camerasToStart = Mathf.Min(cameras.Length, maxCameras);
        for (int i = 0; i < camerasToStart; i++)
        {
            cameraTargets[i].cameraName = cameras[i];
            StartCoroutine(SetupCamera(i));
            yield return new WaitForSeconds(1f); // ī�޶� �� �ʱ�ȭ ����
        }
    }

    IEnumerator SetupCamera(int cameraIndex)
    {
        CameraRenderTarget target = cameraTargets[cameraIndex];
        string cameraName = target.cameraName;

        Debug.Log($"[MultiCamera] ī�޶� {cameraIndex} ({cameraName}) ���� ����");

        // ���� ��û �� Ȯ��
        yield return StartCoroutine(RequestPermissionAndCheck(cameraName, cameraIndex));

        if (!HasCameraPermission(cameraName))
        {
            Debug.LogError($"[MultiCamera] ī�޶� {cameraIndex} ({cameraName}) ���� ȹ�� ����");
            yield break;
        }

        // ī�޶� ����
        yield return StartCoroutine(RunCamera(cameraIndex));
    }

    IEnumerator RequestPermissionAndCheck(string cameraName, int cameraIndex)
    {
        Debug.Log($"[MultiCamera] ī�޶� {cameraIndex} ({cameraName})�� ���� ���� ��û ����");

        // ���� ��û
        try
        {
            plugin.Call("ObtainPermission", cameraName);
            Debug.Log($"[MultiCamera] ī�޶� {cameraIndex} ({cameraName}) ���� ��û �Ϸ�");
        }
        catch (Exception e)
        {
            Debug.LogError($"[MultiCamera] ī�޶� {cameraIndex} ���� ��û ����: {e.Message}");
        }

        // ���� Ȯ�� ����
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

                    // �� 2��° �õ����� ���� ���û
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

    IEnumerator RunCamera(int cameraIndex)
    {
        CameraRenderTarget target = cameraTargets[cameraIndex];
        string cameraName = target.cameraName;

        Debug.Log($"[MultiCamera] ī�޶� {cameraIndex} ({cameraName}) ����");

        // ����̽� ���� Ȯ��
        try
        {
            string deviceInfo = plugin.Call<string>("GetUSBDeviceInfo", cameraName);
            Debug.Log($"[MultiCamera] ī�޶� {cameraIndex} ����̽� ����:\n{deviceInfo}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[MultiCamera] ī�޶� {cameraIndex} ����̽� ���� �������� ����: {e.Message}");
        }

        // ���� ������ �ִٸ� �ݱ�
        try
        {
            plugin.Call("Close", cameraName);
            Debug.Log($"[MultiCamera] ī�޶� {cameraIndex} ���� ���� �ݱ� �õ� �Ϸ�");
        }
        catch (Exception closeEx)
        {
            Debug.Log($"[MultiCamera] ī�޶� {cameraIndex} Close �õ� (���õ�): {closeEx.Message}");
        }

        yield return new WaitForSeconds(1f);

        // ī�޶� ����
        string[] infos = null;
        bool openSuccess = false;

        try
        {
            Debug.Log($"[MultiCamera] ī�޶� {cameraIndex} ({cameraName}) ���� �õ�");
            infos = plugin.Call<string[]>("Open", cameraName);
            openSuccess = (infos != null);
        }
        catch (Exception e)
        {
            Debug.LogError($"[MultiCamera] ī�޶� {cameraIndex} Open ����: {e.Message}");
            openSuccess = false;
        }

        if (!openSuccess || infos == null)
        {
            Debug.LogError($"[MultiCamera] ī�޶� {cameraIndex} Open ����");
            yield break;
        }

        Debug.Log($"[MultiCamera] ī�޶� {cameraIndex} Open ����! ��� ������ ���� ��: {infos.Length}");

        // MJPEG ���� ã��
        int goodIndex = FindBestMJPEGFormat(infos, cameraIndex);
        if (goodIndex < 0)
        {
            Debug.LogError($"[MultiCamera] ī�޶� {cameraIndex} MJPEG ������ ã�� �� �����ϴ�.");
            yield break;
        }

        // ī�޶� ���� �Ľ� �� ����
        yield return StartCoroutine(StartCameraStream(cameraIndex, infos, goodIndex));
    }

    int FindBestMJPEGFormat(string[] infos, int cameraIndex)
    {
        int goodIndex = -1;
        int[] preferredResolutions = { 640, 848, 960, 1280, 1024 };

        // ���� ��ȣ�ϴ� �ػ� �߿��� ã��
        foreach (int prefWidth in preferredResolutions)
        {
            for (int i = 0; i < infos.Length; i++)
            {
                if (!string.IsNullOrEmpty(infos[i]) && infos[i].StartsWith("6"))
                {
                    string[] parts = infos[i].Split(',');
                    if (parts.Length >= 4)
                    {
                        int width = int.Parse(parts[1]);
                        if (width == prefWidth)
                        {
                            goodIndex = i;
                            Debug.Log($"[MultiCamera] ī�޶� {cameraIndex} ��ȣ �ػ� MJPEG ���� �߰�: {infos[i]}");
                            return goodIndex;
                        }
                    }
                }
            }
        }

        // ��ȣ �ػ󵵰� ���ٸ� ù ��° MJPEG ���
        for (int i = 0; i < infos.Length; i++)
        {
            if (!string.IsNullOrEmpty(infos[i]) && infos[i].StartsWith("6"))
            {
                goodIndex = i;
                Debug.Log($"[MultiCamera] ī�޶� {cameraIndex} �⺻ MJPEG ���� �߰�: {infos[i]}");
                break;
            }
        }

        return goodIndex;
    }

    IEnumerator StartCameraStream(int cameraIndex, string[] infos, int formatIndex)
    {
        CameraRenderTarget target = cameraTargets[cameraIndex];
        string cameraName = target.cameraName;

        // �ػ� ������ (ī�޶󺰷� �ٸ� �ػ� ��� ����)
        int[][] resolutionSettings = {
            new int[] {640, 480, 30, 8, 15},    // Format 8: 640x480, bw=0.15
            new int[] {848, 480, 30, 5, 12},    // Format 5: 848x480, bw=0.12
            new int[] {320, 240, 30, 2, 10},    // Format 2: 320x240, bw=0.1
            new int[] {424, 240, 30, 3, 20},    // Format 3: 424x240, bw=0.2
        };

        bool startSuccess = false;
        int width = 640, height = 480, fps = 30;

        for (int i = 0; i < resolutionSettings.Length; i++)
        {
            int testWidth = resolutionSettings[i][0];
            int testHeight = resolutionSettings[i][1];
            int testFps = resolutionSettings[i][2];
            int testMode = resolutionSettings[i][3];
            float testBandwidth = resolutionSettings[i][4] / 100.0f;

            try
            {
                Debug.Log($"[MultiCamera] ī�޶� {cameraIndex} �ػ� �õ�: {testWidth}x{testHeight}@{testFps}fps, Format {testMode}");
                int res = plugin.Call<int>("Start", cameraName, testWidth, testHeight, testFps, testMode, testBandwidth, true, false);

                if (res == 0)
                {
                    Debug.Log($"[MultiCamera] ī�޶� {cameraIndex} ����! �ػ�: {testWidth}x{testHeight}");
                    width = testWidth;
                    height = testHeight;
                    fps = testFps;
                    startSuccess = true;
                    break;
                }
                else
                {
                    Debug.LogWarning($"[MultiCamera] ī�޶� {cameraIndex} ����: {testWidth}x{testHeight} (����: {res})");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[MultiCamera] ī�޶� {cameraIndex} �õ� ����: {e.Message}");
            }

            yield return new WaitForSeconds(0.3f);
        }

        if (!startSuccess)
        {
            Debug.LogError($"[MultiCamera] ī�޶� {cameraIndex} ��� �õ� ����");
            yield break;
        }

        // �ؽ�ó ����
        try
        {
            target.cameraTexture = new Texture2D(width, height, TextureFormat.RGB24, false);
            target.width = width;
            target.height = height;
            target.fps = fps;
            target.isActive = true;

            if (target.targetRenderer != null && target.targetRenderer.material != null)
            {
                target.targetRenderer.material.mainTexture = target.cameraTexture;
                Debug.Log($"[MultiCamera] ī�޶� {cameraIndex} �ؽ�ó ���� �Ϸ�: {width}x{height}");
            }
            else
            {
                Debug.LogError($"[MultiCamera] ī�޶� {cameraIndex} Renderer �Ǵ� Material�� �����ϴ�!");
                yield break;
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[MultiCamera] ī�޶� {cameraIndex} �ؽ�ó ���� ����: {e.Message}");
            yield break;
        }

        // ������ ������ �ڷ�ƾ ����
        target.renderCoroutine = StartCoroutine(RenderCameraFrames(cameraIndex));
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
                try
                {
                    target.cameraTexture.LoadRawTextureData((byte[])(System.Array)frameData);
                    target.cameraTexture.Apply(false, false);

                    frameCount++;
                    if (frameCount % 60 == 0) // 60�����Ӹ��� �α�
                    {
                        Debug.Log($"[MultiCamera] ī�޶� {cameraIndex} ������ {frameCount} ó����");
                    }

                    errorCount = 0; // �����ϸ� ���� ī��Ʈ ����
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
                Debug.LogWarning($"[MultiCamera] ī�޶� {cameraIndex} �� ������ ������ ({errorCount}/{maxErrors})");
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

    void OnDestroy()
    {
        StopAllCameras();
    }

    void StopAllCameras()
    {
        for (int i = 0; i < cameraTargets.Length; i++)
        {
            CameraRenderTarget target = cameraTargets[i];
            if (target != null && target.isActive)
            {
                target.isActive = false;

                if (target.renderCoroutine != null)
                {
                    StopCoroutine(target.renderCoroutine);
                }

                try
                {
                    if (plugin != null && !string.IsNullOrEmpty(target.cameraName))
                    {
                        plugin.Call("Stop", target.cameraName);
                        plugin.Call("Close", target.cameraName);
                        Debug.Log($"[MultiCamera] ī�޶� {i} ({target.cameraName}) ���� �� �ݱ� �Ϸ�");
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"[MultiCamera] ī�޶� {i} ���� ����: {e.Message}");
                }
            }
        }
    }

    // ����׿� �޼����
    [ContextMenu("Manual Permission Request All")]
    void ManualPermissionRequestAll()
    {
        for (int i = 0; i < cameraTargets.Length; i++)
        {
            CameraRenderTarget target = cameraTargets[i];
            if (plugin != null && !string.IsNullOrEmpty(target.cameraName))
            {
                plugin.Call("ObtainPermission", target.cameraName);
                Debug.Log($"[MultiCamera] ī�޶� {i} ���� ���� ��û ����");
            }
        }
    }

    [ContextMenu("Restart All Cameras")]
    void RestartAllCameras()
    {
        StopAllCameras();
        StopAllCoroutines();
        StartCoroutine(InitializeWithRetry());
    }

    [ContextMenu("Stop All Cameras")]
    void StopAllCamerasManual()
    {
        StopAllCameras();
    }

    // ��Ÿ�ӿ��� ī�޶� ���� Ȯ��
    public void GetCameraStatus()
    {
        Debug.Log("=== ī�޶� ���� ===");
        for (int i = 0; i < cameraTargets.Length; i++)
        {
            CameraRenderTarget target = cameraTargets[i];
            Debug.Log($"ī�޶� {i}: {target.cameraName}, Ȱ��: {target.isActive}, �ػ�: {target.width}x{target.height}");
        }
        Debug.Log("==================");
    }
}