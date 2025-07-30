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

        // ���� Android ��Ÿ�� ���� ��û
        RequestAndroidPermissions();

        // UVC �÷����� �ʱ�ȭ
        InitializePlugin();

        // ī�޶� ã�� ������ �ݺ�
        StartCoroutine(FindCameraAndStart());
    }

    void RequestAndroidPermissions()
    {
        // Horizon OS USB Camera ���� ��û
        if (!Permission.HasUserAuthorizedPermission("horizonos.permission.USB_CAMERA"))
        {
            Permission.RequestUserPermission("horizonos.permission.USB_CAMERA");
            Debug.Log("[UVC] Requesting Horizon OS USB Camera permission");
        }

        // �⺻ ī�޶� ���� ��û
        if (!Permission.HasUserAuthorizedPermission(Permission.Camera))
        {
            Permission.RequestUserPermission(Permission.Camera);
            Debug.Log("[UVC] Requesting Camera permission");
        }
    }

    void InitializePlugin()
    {
        try
        {
            // WebCamTexture üũ ���� (���� ī�޶� �����̹Ƿ�)
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
        const int maxRetries = 30; // 30�� ���� �õ�

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
                    Debug.Log("[UVC] ī�޶� �����ϴ�. �ٽ� �õ��մϴ�...");
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
            Debug.LogError("[UVC] ī�޶� ã�� �� �����ϴ�. USB ������ Ȯ���ϼ���.");
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

        // ���� Ȯ�� ���� (�� �� ��� �ð��� ��õ� ����)
        int permissionRetries = 0;
        const int maxPermissionRetries = 12; // 60�� ���� �õ�

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

                    // �� 3��° �õ����� ���� ���û
                    if (permissionRetries % 3 == 2)
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
            Debug.LogError($"[UVC] {cameraName} ������ ���� ���߽��ϴ�. Quest �������� �������� ������ ����ϼ���.");
            ShowPermissionInstructions();
            yield break;
        }

        // ī�޶� ����
        StartCoroutine(RunCamera(cameraName));
    }

    void ShowPermissionInstructions()
    {
        Debug.Log("=== ���� ���� ��� ===");
        Debug.Log("1. Quest ���¿��� Settings �� Apps �� [�� �̸�] ���� �̵�");
        Debug.Log("2. Permissions ���ǿ��� Camera ���� Ȱ��ȭ");
        Debug.Log("3. ���� �ٽ� �����ϼ���");
        Debug.Log("====================");
    }

    IEnumerator RunCamera(string cameraName)
    {
        Debug.Log($"[UVC] {cameraName} ī�޶� ����");

        string[] infos = null;
        try
        {
            // ī�޶� ����
            infos = plugin.Call<string[]>("Open", cameraName);

            // null üũ
            if (infos == null)
            {
                Debug.LogError("[UVC] plugin.Call('Open') returned null");
                yield break;
            }

            if (infos.Length == 0)
            {
                Debug.LogError("[UVC] plugin.Call('Open') returned empty array");
                yield break;
            }

            Debug.Log($"[UVC] Open ����. ��� ������ ���� ��: {infos.Length}");
            for (int i = 0; i < infos.Length; i++)
            {
                Debug.Log($"[UVC] Format {i}: {infos[i]}");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[UVC] Camera open failed: {e.Message}");
            Debug.LogError($"[UVC] Stack trace: {e.StackTrace}");
            yield break;
        }

        // MJPEG ���� ã�� (Ÿ�� 6) - ������ �ػ� �켱 ����
        int goodIndex = -1;
        int[] preferredResolutions = { 640, 848, 960, 1280, 1024 }; // ��ȣ�ϴ� ���� �ػ� ����

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
        try
        {
            // ī�޶� ��Ʈ���� ���� - �ùٸ� �޼��� �ñ״�ó ���
            // ���� �ڵ忡���� 6�� �Ķ���Ϳ�����, �����δ� 5�� �Ķ������ �� ����
            Debug.Log($"[UVC] Start �޼��� ȣ��: {cameraName}, {width}, {height}, {fps}, {bandwidth}");

            // �پ��� �ñ״�ó �õ�
            try
            {
                // �õ� 1: 5�� �Ķ���� (String, int, int, int, float)
                res = plugin.Call<int>("Start", cameraName, width, height, fps, bandwidth);
                Debug.Log($"[UVC] Start method (5 params) successful: {res}");
            }
            catch (System.Exception e1)
            {
                Debug.LogWarning($"[UVC] Start method (5 params) failed: {e1.Message}");

                try
                {
                    // �õ� 2: 4�� �Ķ���� (String, int, int, int)
                    res = plugin.Call<int>("Start", cameraName, width, height, fps);
                    Debug.Log($"[UVC] Start method (4 params) successful: {res}");
                }
                catch (System.Exception e2)
                {
                    Debug.LogWarning($"[UVC] Start method (4 params) failed: {e2.Message}");

                    try
                    {
                        // �õ� 3: startStreaming �޼��� ���
                        res = plugin.Call<int>("startStreaming", cameraName, width, height, fps);
                        Debug.Log($"[UVC] startStreaming method successful: {res}");
                    }
                    catch (System.Exception e3)
                    {
                        Debug.LogError($"[UVC] All Start method attempts failed");
                        Debug.LogError($"[UVC] Method 1 (5 params): {e1.Message}");
                        Debug.LogError($"[UVC] Method 2 (4 params): {e2.Message}");
                        Debug.LogError($"[UVC] Method 3 (startStreaming): {e3.Message}");
                        yield break;
                    }
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[UVC] Camera start failed: {e.Message}");
            yield break;
        }

        if (res != 0)
        {
            Debug.LogError($"[UVC] ī�޶� ���� ����. ���� �ڵ�: {res}");
            yield break;
        }

        Debug.Log("[UVC] ī�޶� ��Ʈ���� ���� ����!");

        // �ؽ�ó ���� �� ����
        Texture2D cameraTexture = null;
        try
        {
            cameraTexture = new Texture2D(width, height, TextureFormat.RGB24, false, true);

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
}