using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Android;
using UnityEngine.UI;

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

        // ù ��° ī�޶�� �ڱ� �ڽ��� ������ ���
        if (cameraTargets[0].targetRenderer == null)
        {
            cameraTargets[0].targetRenderer = GetComponent<Renderer>();
        }

        // UI ��ư �̺�Ʈ ����
        SetupUI();

        StartCoroutine(InitializeWithRetry());
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

        // ���� ��û
        RequestAndroidPermissions();
        yield return new WaitForSeconds(2f);

        // �÷����� �ʱ�ȭ
        if (!isInitialized)
        {
            UpdateStatusText("�÷����� �ʱ�ȭ ��...");
            InitializePlugin();
            yield return new WaitForSeconds(1f);
            isInitialized = true;
        }

        // ī�޶� ã�� �� ����
        UpdateStatusText("ī�޶� �˻� ��...");
        yield return StartCoroutine(FindCamerasAndStart());
    }

    // ī�޶� �翬�� �޼��� - ���� ������ ī�޶� ���� �� �� ī�޶� ����
    public IEnumerator ReconnectCameras()
    {
        if (isReconnecting)
        {
            UpdateStatusText("�̹� �翬�� ���Դϴ�...");
            yield break;
        }

        isReconnecting = true;
        UpdateStatusText("ī�޶� ���� Ȯ�� ��...");

        // 1�ܰ�: ���� ����� ���� ī�޶� ��� ��������
        string[] allCameras = null;
        try
        {
            allCameras = plugin.Call<string[]>("GetUSBDevices");
        }
        catch (Exception e)
        {
            UpdateStatusText($"ī�޶� �˻� ����: {e.Message}");
            isReconnecting = false;
            yield break;
        }

        List<string> activeCameraList = new List<string>();
        if (allCameras != null)
        {
            activeCameraList.AddRange(allCameras);
        }

        // 2�ܰ�: ������ ������ ī�޶� ����
        int cleanedCameras = 0;
        for (int i = 0; i < cameraTargets.Length; i++)
        {
            CameraRenderTarget target = cameraTargets[i];

            // Ȱ��ȭ�Ǿ� ������ �����δ� ������� ���� ī�޶� ã��
            if (target.isActive && !string.IsNullOrEmpty(target.cameraName))
            {
                if (!activeCameraList.Contains(target.cameraName))
                {
                    // ������ ������ ī�޶� ����
                    UpdateStatusText($"ī�޶� {i + 1} ���� ������ ����, ���� ��...");

                    target.isActive = false;
                    if (target.renderCoroutine != null)
                    {
                        StopCoroutine(target.renderCoroutine);
                        target.renderCoroutine = null;
                    }

                    // �ؽ�ó ����
                    if (target.cameraTexture != null)
                    {
                        if (target.targetRenderer != null && target.targetRenderer.material != null)
                        {
                            target.targetRenderer.material.mainTexture = null;
                        }
                        Destroy(target.cameraTexture);
                        target.cameraTexture = null;
                    }

                    target.cameraName = "";
                    cleanedCameras++;
                }
            }
        }

        if (cleanedCameras > 0)
        {
            UpdateStatusText($"{cleanedCameras}���� ������ ī�޶� ���� �Ϸ�");
            yield return new WaitForSeconds(1f);
        }

        // 3�ܰ�: ���� ����� ī�޶���� �̸��� �ٽ� ����
        List<string> currentActiveCameras = new List<string>();
        for (int i = 0; i < cameraTargets.Length; i++)
        {
            if (cameraTargets[i].isActive && !string.IsNullOrEmpty(cameraTargets[i].cameraName))
            {
                currentActiveCameras.Add(cameraTargets[i].cameraName);
            }
        }

        UpdateStatusText("�� ī�޶� �˻� ��...");

        // 4�ܰ�: ���� ����� ī�޶� ã��
        List<string> newCameras = new List<string>();
        foreach (string camera in activeCameraList)
        {
            if (!currentActiveCameras.Contains(camera))
            {
                newCameras.Add(camera);
            }
        }

        if (newCameras.Count == 0)
        {
            UpdateStatusText("���� ����� ī�޶� �����ϴ�.");
            isReconnecting = false;
            yield break;
        }

        UpdateStatusText($"{newCameras.Count}���� �� ī�޶� �߰�!");

        // 5�ܰ�: �� ���Կ� �� ī�޶� ����
        int connectedNewCameras = 0;
        for (int i = 0; i < cameraTargets.Length && connectedNewCameras < newCameras.Count; i++)
        {
            // ����ִ� ���� ã��
            if (!cameraTargets[i].isActive || string.IsNullOrEmpty(cameraTargets[i].cameraName))
            {
                cameraTargets[i].cameraName = newCameras[connectedNewCameras];
                UpdateStatusText($"ī�޶� {i + 1}�� �� ī�޶� ���� �õ�...");

                bool success = false;
                yield return StartCoroutine(SetupCamera(i, (result) => success = result));

                if (success)
                {
                    UpdateStatusText($"ī�޶� {i + 1} �� ī�޶� ���� ����!");
                    connectedNewCameras++;
                }
                else
                {
                    UpdateStatusText($"ī�޶� {i + 1} �� ī�޶� ���� ����");
                    cameraTargets[i].cameraName = ""; // ���н� �̸� �ʱ�ȭ
                }

                yield return new WaitForSeconds(1f);
            }
        }

        // 6�ܰ�: ��� ����
        string resultMessage = "";
        if (cleanedCameras > 0 && connectedNewCameras > 0)
        {
            resultMessage = $"���� {cleanedCameras}��, �űԿ��� {connectedNewCameras}��";
        }
        else if (cleanedCameras > 0)
        {
            resultMessage = $"������ ī�޶� {cleanedCameras}�� �����Ϸ�";
        }
        else if (connectedNewCameras > 0)
        {
            resultMessage = $"�� ī�޶� {connectedNewCameras}�� ����Ϸ�";
        }
        else
        {
            resultMessage = "������� ���� - ��� ī�޶� ����";
        }

        UpdateStatusText(resultMessage);
        isReconnecting = false;
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
                    UpdateStatusText($"�߰ߵ� ī�޶� ��: {cameras.Length}");
                    availableCameras.Clear(); // ���� ����Ʈ Ŭ����
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

        // �ִ� maxCameras������ ī�޶� ����
        int camerasToStart = Mathf.Min(cameras.Length, maxCameras);
        UpdateStatusText($"{camerasToStart}�� ī�޶� ���� ����...");

        int successCount = 0;
        for (int i = 0; i < camerasToStart; i++)
        {
            cameraTargets[i].cameraName = cameras[i];
            bool success = false;
            yield return StartCoroutine(SetupCamera(i, (result) => success = result));
            if (success) successCount++;
            yield return new WaitForSeconds(1f); // ī�޶� �� �ʱ�ȭ ����
        }

        UpdateStatusText($"ī�޶� ���� �Ϸ�: {successCount}/{camerasToStart}�� ����");
    }

    IEnumerator SetupCamera(int cameraIndex, System.Action<bool> onComplete = null)
    {
        CameraRenderTarget target = cameraTargets[cameraIndex];
        string cameraName = target.cameraName;

        Debug.Log($"[MultiCamera] ī�޶� {cameraIndex} ({cameraName}) ���� ����");
        UpdateStatusText($"ī�޶� {cameraIndex + 1} ���� ��...");

        // ���� ��û �� Ȯ��
        yield return StartCoroutine(RequestPermissionAndCheck(cameraName, cameraIndex));

        if (!HasCameraPermission(cameraName))
        {
            Debug.LogError($"[MultiCamera] ī�޶� {cameraIndex} ({cameraName}) ���� ȹ�� ����");
            UpdateStatusText($"ī�޶� {cameraIndex + 1} ���� ����");
            onComplete?.Invoke(false);
            yield break;
        }

        // ī�޶� ����
        bool success = false;
        yield return StartCoroutine(RunCamera(cameraIndex, (result) => success = result));

        onComplete?.Invoke(success);
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

    IEnumerator RunCamera(int cameraIndex, System.Action<bool> onComplete = null)
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
            // Java �÷����ο��� Stop �޼��尡 ����, Close�� closeCamera�� �����Ǿ� ����
            // ������ Unity������ Close�� ���� ȣ���� �� �����Ƿ� �ǳʶ�
            Debug.Log($"[MultiCamera] ī�޶� {cameraIndex} ���� ���� ���� �õ�");
        }
        catch (Exception closeEx)
        {
            Debug.Log($"[MultiCamera] ī�޶� {cameraIndex} Close �õ� (���õ�): {closeEx.Message}");
        }

        yield return new WaitForSeconds(1f);

        // ī�޶� ���� (��õ� ���� �߰�)
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
                Debug.Log($"[MultiCamera] ī�޶� {cameraIndex} {openRetries + 1}�� �� ��õ�...");
                yield return new WaitForSeconds(openRetries); // ������ ��� �ð� ����
            }
        }

        if (!openSuccess || infos == null)
        {
            Debug.LogError($"[MultiCamera] ī�޶� {cameraIndex} Open ����");
            UpdateStatusText($"ī�޶� {cameraIndex + 1} ���� ����");
            onComplete?.Invoke(false);
            yield break;
        }

        Debug.Log($"[MultiCamera] ī�޶� {cameraIndex} Open ����! ��� ������ ���� ��: {infos.Length}");

        // MJPEG ���� ã��
        int goodIndex = FindBestMJPEGFormat(infos, cameraIndex);
        if (goodIndex < 0)
        {
            Debug.LogError($"[MultiCamera] ī�޶� {cameraIndex} MJPEG ������ ã�� �� �����ϴ�.");
            UpdateStatusText($"ī�޶� {cameraIndex + 1} ���� ����");
            onComplete?.Invoke(false);
            yield break;
        }

        // ī�޶� ���� �Ľ� �� ����
        bool streamSuccess = false;
        yield return StartCoroutine(StartCameraStream(cameraIndex, infos, goodIndex, (result) => streamSuccess = result));

        if (streamSuccess)
        {
            UpdateStatusText($"ī�޶� {cameraIndex + 1} ���� ����");
        }

        onComplete?.Invoke(streamSuccess);
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

    IEnumerator StartCameraStream(int cameraIndex, string[] infos, int formatIndex, System.Action<bool> onComplete = null)
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
            onComplete?.Invoke(false);
            yield break;
        }

        // ���� �ؽ�ó�� �ִٸ� ����
        if (target.cameraTexture != null)
        {
            Destroy(target.cameraTexture);
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
                onComplete?.Invoke(false);
                yield break;
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[MultiCamera] ī�޶� {cameraIndex} �ؽ�ó ���� ����: {e.Message}");
            onComplete?.Invoke(false);
            yield break;
        }

        // ������ ������ �ڷ�ƾ ����
        target.renderCoroutine = StartCoroutine(RenderCameraFrames(cameraIndex));
        onComplete?.Invoke(true);
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

                try
                {
                    if (plugin != null && !string.IsNullOrEmpty(target.cameraName))
                    {
                        // Java �÷������� ���� �޼���� ���缭 ȣ��
                        // Stop �޼���� ����, Close�� closeCamera�� ������
                        plugin.Call("Close", target.cameraName); // �� �κ��� ������ �� �� ������ �õ�
                        Debug.Log($"[MultiCamera] ī�޶� {i} ({target.cameraName}) ���� �õ� �Ϸ�");
                    }
                }
                catch (Exception e)
                {
                    // �޼��尡 ��� ������ ���� ���� ���� - ����
                    Debug.Log($"[MultiCamera] ī�޶� {i} ���� �õ� (�޼��� ����): {e.Message}");
                }

                // �ؽ�ó ����
                if (target.cameraTexture != null)
                {
                    if (target.targetRenderer != null && target.targetRenderer.material != null)
                    {
                        target.targetRenderer.material.mainTexture = null;
                    }
                    Destroy(target.cameraTexture);
                    target.cameraTexture = null;
                }

                // ī�޶� �̸��� �ʱ�ȭ (�߿�!)
                target.cameraName = "";
                target.width = 640;
                target.height = 480;
                target.fps = 30;
            }
        }

        UpdateStatusText("��� ī�޶� ���� �Ϸ�");
    }

    void OnDestroy()
    {
        StopAllCameras();
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
        if (!isReconnecting)
        {
            StartCoroutine(ReconnectCameras());
        }
    }

    // ��Ÿ�ӿ��� ī�޶� ���� Ȯ��
    public void GetCameraStatus()
    {
        string statusMessage = "=== ī�޶� ���� ===\n";
        for (int i = 0; i < cameraTargets.Length; i++)
        {
            CameraRenderTarget target = cameraTargets[i];
            statusMessage += $"ī�޶� {i}: {target.cameraName}, Ȱ��: {target.isActive}, �ػ�: {target.width}x{target.height}\n";
        }
        statusMessage += "==================";

        UpdateStatusText("ī�޶� ���� Ȯ�ε�");
        Debug.Log(statusMessage);
    }

    // ���� ī�޶� �翬��
    public void ReconnectSingleCamera(int cameraIndex)
    {
        if (cameraIndex >= 0 && cameraIndex < cameraTargets.Length && !isReconnecting)
        {
            StartCoroutine(ReconnectSingleCameraCoroutine(cameraIndex));
        }
    }

    IEnumerator ReconnectSingleCameraCoroutine(int cameraIndex)
    {
        CameraRenderTarget target = cameraTargets[cameraIndex];
        UpdateStatusText($"ī�޶� {cameraIndex + 1} �翬�� ��...");

        // �ش� ī�޶� ����
        target.isActive = false;
        if (target.renderCoroutine != null)
        {
            StopCoroutine(target.renderCoroutine);
            target.renderCoroutine = null;
        }

        try
        {
            if (plugin != null && !string.IsNullOrEmpty(target.cameraName))
            {
                plugin.Call("Stop", target.cameraName);
                plugin.Call("Close", target.cameraName);
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[MultiCamera] ī�޶� {cameraIndex} ���� ����: {e.Message}");
        }

        yield return new WaitForSeconds(1f);

        // ��� ������ ī�޶� �ٽ� �˻�
        string[] cameras = null;
        bool getDevicesSuccess = false;

        try
        {
            cameras = plugin.Call<string[]>("GetUSBDevices");
            getDevicesSuccess = true;
        }
        catch (Exception e)
        {
            UpdateStatusText($"ī�޶� {cameraIndex + 1} �翬�� ����: {e.Message}");
            getDevicesSuccess = false;
        }

        if (getDevicesSuccess && cameras != null && cameras.Length > cameraIndex)
        {
            target.cameraName = cameras[cameraIndex];
            bool success = false;
            yield return StartCoroutine(SetupCamera(cameraIndex, (result) => success = result));

            if (success)
            {
                UpdateStatusText($"ī�޶� {cameraIndex + 1} �翬�� ����");
            }
            else
            {
                UpdateStatusText($"ī�޶� {cameraIndex + 1} �翬�� ����");
            }
        }
        else if (getDevicesSuccess)
        {
            UpdateStatusText($"ī�޶� {cameraIndex + 1} ã�� �� ����");
        }
    }
}