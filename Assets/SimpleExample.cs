using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Android;

[RequireComponent(typeof(MeshRenderer))]
public class SimpleExample : MonoBehaviour
{
    AndroidJavaObject plugin;
    AndroidJavaObject activity;
    AndroidJavaClass unityPlayer;

    private bool permissionRequested = false;
    private string currentCameraName = "";

    void Start()
    {
        Debug.Log("Start");
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
        yield return StartCoroutine(FindCameraAndStart());
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
                Debug.Log($"[UVC] {permission} ���� ��û");
            }
            else
            {
                Debug.Log($"[UVC] {permission} ���� �̹� ����");
            }
        }
    }

    void InitializePlugin()
    {
        try
        {
            Debug.Log("[UVC] Initializing UVC Plugin...");

            plugin = new AndroidJavaObject("edu.uga.engr.vel.unityuvcplugin.UnityUVCPlugin");
            Debug.Log("plugin: " + plugin);

            unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            Debug.Log("unityPlayer: " + unityPlayer);

            activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            Debug.Log("activity: " + activity);

            plugin.Call("Init", activity);
            Debug.Log("[UVC] Plugin initialized successfully");
        }
        catch (Exception e)
        {
            Debug.LogError("[UVC] Plugin initialization failed: " + e.Message);
        }
    }

    IEnumerator FindCameraAndStart()
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
                    Debug.Log("[UVC] ī�޶� �߰�: " + cameras[0]);
                    currentCameraName = cameras[0];
                    break;
                }
                else
                {
                    Debug.Log($"[UVC] ī�޶� �����ϴ�. �ٽ� �õ��մϴ�... ({retryCount + 1}/{maxRetries})");
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[UVC] GetUSBDevices failed: " + e.Message);
            }

            retryCount++;
            yield return new WaitForSeconds(1f);
        }

        if (cameras == null || cameras.Length == 0)
        {
            Debug.LogError("[UVC] ī�޶� ã�� �� �����ϴ�. �ߴ��մϴ�.");
            yield break;
        }

        // ���� ��û �� ī�޶� ����
        StartCoroutine(RequestPermissionAndRunCamera(currentCameraName));
    }

    IEnumerator RequestPermissionAndRunCamera(string cameraName)
    {
        Debug.Log($"[UVC] {cameraName}�� ���� ���� ��û ����");

        // ����ڰ� �������� ������ ����� �ð��� ��
        if (!permissionRequested)
        {
            try
            {
                plugin.Call("ObtainPermission", cameraName);
                permissionRequested = true;
                Debug.Log($"[UVC] {cameraName} ���� ��û �Ϸ�");
            }
            catch (Exception e)
            {
                Debug.LogError($"[UVC] Permission request failed: {e.Message}");
            }
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
                    Debug.Log($"[UVC] {cameraName} ���� ȹ�� ����!");
                    break;
                }
                else
                {
                    Debug.Log($"[UVC] {cameraName} ���� ��� ��... ({permissionRetries + 1}/{maxPermissionRetries})");

                    // �� 2��° �õ����� ���� ���û
                    if (permissionRetries % 2 == 1)
                    {
                        plugin.Call("ObtainPermission", cameraName);
                        Debug.Log($"[UVC] {cameraName} ���� ���û");
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[UVC] Permission check failed: {e.Message}");
            }

            permissionRetries++;
            yield return new WaitForSeconds(5f);
        }

        // ���� ���� Ȯ��
        bool finalPermissionCheck = false;
        try
        {
            finalPermissionCheck = plugin.Call<bool>("hasPermission", cameraName);
        }
        catch (Exception e)
        {
            Debug.LogError($"[UVC] Final permission check failed: {e.Message}");
            yield break;
        }

        if (!finalPermissionCheck)
        {
            Debug.LogError($"[UVC] {cameraName} ������ ���� ���߽��ϴ�. �ߴ��մϴ�.");
            ShowPermissionInstructions();
            yield break;
        }

        // ī�޶� ����
        StartCoroutine(RunCamera(cameraName));
    }

    void ShowPermissionInstructions()
    {
        Debug.Log("=== ���� ���� ��� ===");
        Debug.Log("1. Quest ���¿��� Settings -> Apps -> [�� �̸�] ���� �̵�");
        Debug.Log("2. Permissions ���ǿ��� Camera ���� Ȱ��ȭ");
        Debug.Log("3. ���� �ٽ� �����ϼ���");
        Debug.Log("====================");
    }

    IEnumerator RunCamera(string cameraName)
    {
        Debug.Log($"[UVC] {cameraName} ī�޶� ����");

        // ����̽� ���� Ȯ��
        try
        {
            string deviceInfo = plugin.Call<string>("GetUSBDeviceInfo", cameraName);
            Debug.Log($"[UVC] ����̽� ����:\n{deviceInfo}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[UVC] ����̽� ���� �������� ����: {e.Message}");
        }

        // ���� ��Ȯ��
        bool hasPermission = false;
        try
        {
            hasPermission = plugin.Call<bool>("hasPermission", cameraName);
            Debug.Log($"[UVC] ���� ����: {hasPermission}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[UVC] ���� Ȯ�� ����: {e.Message}");
            yield break;
        }

        if (!hasPermission)
        {
            Debug.LogError("[UVC] ������ �����ϴ�!");
            yield break;
        }

        // ���� ���� ������ �ִٸ� �ݱ�
        try
        {
            plugin.Call("Close", cameraName);
            Debug.Log("[UVC] ���� ���� �ݱ� �õ� �Ϸ�");
        }
        catch (Exception closeEx)
        {
            Debug.Log($"[UVC] Close �õ� (���õ�): {closeEx.Message}");
        }

        yield return new WaitForSeconds(1f);

        string[] infos = null;
        bool openSuccess = false;

        // ù ��° ī�޶� ���� �õ�
        try
        {
            Debug.Log($"[UVC] '{cameraName}' ī�޶� ���� �õ�");
            infos = plugin.Call<string[]>("Open", cameraName);
            openSuccess = (infos != null);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[UVC] ù ��° Camera open failed: {e.Message}");
            openSuccess = false;
        }

        // ù ��° �õ��� �����ϸ� ��õ�
        if (!openSuccess || infos == null)
        {
            Debug.LogError("[UVC] Open �޼��尡 null�� ��ȯ�߽��ϴ�.");
            Debug.LogError("[UVC] �̴� ����Ƽ�� openCamera �Լ��� ���������� �ǹ��մϴ�.");

            yield return new WaitForSeconds(3f);

            try
            {
                Debug.Log("[UVC] ī�޶� ���� ��õ�...");
                infos = plugin.Call<string[]>("Open", cameraName);
                openSuccess = (infos != null);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[UVC] ��õ� Camera open failed: {e.Message}");
                openSuccess = false;
            }

            if (!openSuccess || infos == null)
            {
                Debug.LogError("[UVC] ��õ��� �����߽��ϴ�. ī�޶� ����� �� �����ϴ�.");
                yield break;
            }
        }

        Debug.Log($"[UVC] Open ����! ��� ������ ���� ��: {infos.Length}");
        for (int i = 0; i < infos.Length; i++)
        {
            Debug.Log($"[UVC] Format {i}: {infos[i]}");
        }

        // ī�޶��� ���� ��ũ���� ���� Ȯ��
        try
        {
            string descriptorInfo = plugin.Call<string>("getDescriptor", 0);
            Debug.Log($"[UVC] ī�޶� ��ũ���� �� ����:\n{descriptorInfo}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[UVC] ��ũ���� ���� �������� ����: {e.Message}");
        }

        // MJPEG ���� ã�� (Ÿ�� 6) - ������ �ػ� �켱 ����
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
                        int xx = int.Parse(parts[1]);
                        if (xx == prefWidth)
                        {
                            goodIndex = i;
                            Debug.Log($"[UVC] ��ȣ �ػ� MJPEG ���� �߰�: {infos[i]}");
                            break;
                        }
                    }
                }
            }
            if (goodIndex >= 0) break;
        }

        // ��ȣ �ػ󵵰� ���ٸ� ù ��° MJPEG ���
        if (goodIndex < 0)
        {
            for (int i = 0; i < infos.Length; i++)
            {
                if (!string.IsNullOrEmpty(infos[i]) && infos[i].StartsWith("6"))
                {
                    goodIndex = i;
                    Debug.Log($"[UVC] �⺻ MJPEG ���� �߰�: {infos[i]}");
                    break;
                }
            }
        }

        if (goodIndex < 0)
        {
            Debug.LogError("[UVC] MJPEG ����(Ÿ�� 6)�� ã�� �� �����ϴ�.");
            Debug.LogError("[UVC] ��� ������ ����:");
            for (int i = 0; i < infos.Length; i++)
            {
                Debug.LogError($"[UVC]   {i}: {infos[i]}");
            }
            yield break;
        }

        // ī�޶� ���� �Ľ�
        string[] info = null;
        try
        {
            info = infos[goodIndex].Split(',');
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[UVC] Format parsing failed: {e.Message}");
            Debug.LogError($"[UVC] Format string: '{infos[goodIndex]}'");
            yield break;
        }

        if (info == null || info.Length < 4)
        {
            Debug.LogError($"[UVC] ī�޶� ���� ������ �ùٸ��� �ʽ��ϴ�. ����: {info?.Length ?? 0}");
            Debug.LogError($"[UVC] ���� ���ڿ�: '{infos[goodIndex]}'");
            yield break;
        }

        int width, height, fps;
        try
        {
            width = int.Parse(info[1]);
            height = int.Parse(info[2]);
            fps = int.Parse(info[3]);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[UVC] ī�޶� ���� �Ľ� ����: {e.Message}");
            Debug.LogError($"[UVC] info[1]='{info[1]}', info[2]='{info[2]}', info[3]='{info[3]}'");
            yield break;
        }

        var bandwidth = 1.0f;

        Debug.Log($"[UVC] ī�޶� ����: {width}x{height} @ {fps}fps");

        int res = -1;
        bool startSuccess = false;

        // �α׿��� Ȯ�ε� ���� ���� �ε����� ��� (mode�� ���� �ε���)
        // Format 8: 6,640,480,30 - 640x480 MJPEG
        // Format 5: 6,848,480,30 - 848x480 MJPEG  
        // Format 7: 6,1280,720,30 - 1280x720 MJPEG
        // Format 9: 6,1920,1080,30 - 1920x1080 MJPEG
        // Format 4: 6,640,360,30 - 640x360 MJPEG
        // Format 3: 6,424,240,30 - 424x240 MJPEG
        // Format 2: 6,320,240,30 - 320x240 MJPEG (���� ����)

        int[][] resolutionSettings = {
            new int[] {1920, 1080, 30, 9, 5},   // Format 9: 1920x1080, bw=0.05
            new int[] {1280, 720, 30, 7, 8},    // Format 7: 1280x720, bw=0.08
            new int[] {848, 480, 30, 5, 12},    // Format 5: 848x480, bw=0.12
            new int[] {640, 480, 30, 8, 15},    // Format 8: 640x480, bw=0.15
            new int[] {640, 360, 30, 4, 18},    // Format 4: 640x360, bw=0.18
            new int[] {424, 240, 30, 3, 20},    // Format 3: 424x240, bw=0.2
            new int[] {320, 240, 30, 2, 10},    // Format 2: 320x240, bw=0.1 (���� ����)
        };

        for (int i = 0; i < resolutionSettings.Length; i++)
        {
            int testWidth = resolutionSettings[i][0];
            int testHeight = resolutionSettings[i][1];
            int testFps = resolutionSettings[i][2];
            int testMode = resolutionSettings[i][3]; // ���� ���� ���� �ε���
            float testBandwidth = resolutionSettings[i][4] / 100.0f;

            try
            {
                Debug.Log($"[UVC] �ػ� �õ� {i + 1}: {testWidth}x{testHeight}@{testFps}fps, Format {testMode}, bw={testBandwidth}");
                res = plugin.Call<int>("Start", cameraName, testWidth, testHeight, testFps, testMode, testBandwidth, true, false);
                Debug.Log($"[UVC] ���: {res}");

                if (res == 0)
                {
                    Debug.Log($"[UVC] ����! �ػ�: {testWidth}x{testHeight}, Format {testMode}");
                    width = testWidth;
                    height = testHeight;
                    fps = testFps;
                    startSuccess = true;
                    break;
                }
                else
                {
                    Debug.LogWarning($"[UVC] ����: {testWidth}x{testHeight} Format {testMode} (����: {res})");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[UVC] �ػ� {testWidth}x{testHeight} Format {testMode} �õ� ����: {e.Message}");
            }

            yield return new WaitForSeconds(0.3f);
        }

        if (!startSuccess)
        {
            Debug.LogError($"[UVC] ��� �õ� ����. ������ ���� �ڵ�: {res}");
            Debug.LogError("[UVC] ī�޶� ������ �������� �ʰų� �ϵ���� ������ �� �ֽ��ϴ�.");
            Debug.LogError("[UVC] �õ��� �ߴ��մϴ�.");
            yield break;
        }

        Debug.Log("[UVC] ī�޶� ��Ʈ���� ���� ����!");

        // �ؽ�ó ���� �� ����
        Texture2D cameraTexture = null;
        try
        {
            // MJPEG�� RGB24�� ���ڵ���
            cameraTexture = new Texture2D(width, height, TextureFormat.RGB24, false);
            Debug.Log($"[UVC] �ؽ�ó ����: {width}x{height}, RGB24");

            Renderer renderer = GetComponent<Renderer>();
            if (renderer == null)
            {
                Debug.LogError("[UVC] Renderer component not found!");
                yield break;
            }

            if (renderer.material == null)
            {
                Debug.LogError("[UVC] Renderer material is null!");
                yield break;
            }

            renderer.material.mainTexture = cameraTexture;
            Debug.Log("[UVC] �ؽ�ó ���� �Ϸ�");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[UVC] Texture creation failed: {e.Message}");
            yield break;
        }

        // ������ ������ �б� ����
        int frameCount = 0;
        int errorCount = 0;
        const int maxErrors = 10;

        while (errorCount < maxErrors)
        {
            sbyte[] frameData = null;
            bool frameSuccess = false;

            // try-catch ��� �и� (yield ����)
            try
            {
                frameData = plugin.Call<sbyte[]>("GetFrameData", cameraName);
                frameSuccess = true;
            }
            catch (System.Exception e)
            {
                errorCount++;
                Debug.LogError($"[UVC] Frame data error ({errorCount}/{maxErrors}): {e.Message}");
                frameSuccess = false;
            }

            // ������ ��� ó��
            if (frameSuccess && frameData != null && frameData.Length > 0)
            {
                try
                {
                    cameraTexture.LoadRawTextureData((byte[])(System.Array)frameData);
                    cameraTexture.Apply(false, false);

                    frameCount++;
                    if (frameCount % 30 == 0) // 30�����Ӹ��� �α�
                    {
                        Debug.Log($"[UVC] ������ {frameCount} ó����");
                    }

                    errorCount = 0; // �����ϸ� ���� ī��Ʈ ����
                }
                catch (System.Exception e)
                {
                    errorCount++;
                    Debug.LogError($"[UVC] Texture update error ({errorCount}/{maxErrors}): {e.Message}");
                }
            }
            else if (frameSuccess) // frameData�� null�̰ų� �� ���
            {
                errorCount++;
                Debug.LogWarning($"[UVC] �� ������ ������ ({errorCount}/{maxErrors})");
            }

            // ������ �߻��� ��쿡�� ��� (yield�� try-catch ������)
            if (!frameSuccess || (frameData == null || frameData.Length == 0))
            {
                if (errorCount >= maxErrors)
                {
                    Debug.LogError("[UVC] �ʹ� ���� ������ ����. ī�޶� ����.");
                    break;
                }
                yield return new WaitForSeconds(0.1f); // ���� �߻��� ���
            }
            else
            {
                yield return null; // ���� ó���� ���� ����������
            }
        }

        Debug.LogError("[UVC] ī�޶� ��Ʈ���� �ߴܵ�");
    }

    void OnDestroy()
    {
        try
        {
            if (plugin != null && !string.IsNullOrEmpty(currentCameraName))
            {
                plugin.Call("Stop", currentCameraName);
                plugin.Call("Close", currentCameraName);
                Debug.Log("[UVC] Camera stopped and closed");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[UVC] Cleanup failed: {e.Message}");
        }
    }

    // ����׿� - Inspector���� �������� ���� ��û
    [ContextMenu("Manual Permission Request")]
    void ManualPermissionRequest()
    {
        if (plugin != null && !string.IsNullOrEmpty(currentCameraName))
        {
            plugin.Call("ObtainPermission", currentCameraName);
            Debug.Log("[UVC] Manual permission request sent");
        }
    }

    // ����׿� - Inspector���� �������� ī�޶� �����
    [ContextMenu("Restart Camera")]
    void RestartCamera()
    {
        if (!string.IsNullOrEmpty(currentCameraName))
        {
            StopAllCoroutines();
            StartCoroutine(RunCamera(currentCameraName));
        }
    }
}