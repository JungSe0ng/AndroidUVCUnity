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

        // 먼저 Android 런타임 권한 요청
        RequestAndroidPermissions();

        // UVC 플러그인 초기화
        InitializePlugin();

        // 카메라 찾을 때까지 반복
        StartCoroutine(FindCameraAndStart());
    }

    void RequestAndroidPermissions()
    {
        // Horizon OS USB Camera 권한 요청
        if (!Permission.HasUserAuthorizedPermission("horizonos.permission.USB_CAMERA"))
        {
            Permission.RequestUserPermission("horizonos.permission.USB_CAMERA");
            Debug.Log("[UVC] Requesting Horizon OS USB Camera permission");
        }

        // 기본 카메라 권한 요청
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
            // WebCamTexture 체크 제거 (내장 카메라 관련이므로)
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
        const int maxRetries = 30; // 30초 동안 시도

        while (retryCount < maxRetries)
        {
            try
            {
                cameras = plugin.Call<string[]>("GetUSBDevices");
                if (cameras != null && cameras.Length > 0)
                {
                    Debug.Log("[UVC] 카메라 발견: " + cameras[0]);
                    currentCameraName = cameras[0];
                    break;
                }
                else
                {
                    Debug.Log("[UVC] 카메라가 없습니다. 다시 시도합니다...");
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
            Debug.LogError("[UVC] 카메라를 찾을 수 없습니다. USB 연결을 확인하세요.");
            yield break;
        }

        // 권한 요청 및 카메라 실행
        StartCoroutine(RequestPermissionAndRunCamera(currentCameraName));
    }

    IEnumerator RequestPermissionAndRunCamera(string cameraName)
    {
        Debug.Log($"[UVC] {cameraName}에 대한 권한 요청 시작");

        // 사용자가 수동으로 권한을 허용할 시간을 줌
        if (!permissionRequested)
        {
            try
            {
                plugin.Call("ObtainPermission", cameraName);
                permissionRequested = true;
                Debug.Log($"[UVC] {cameraName} 권한 요청 완료");
            }
            catch (Exception e)
            {
                Debug.LogError($"[UVC] Permission request failed: {e.Message}");
            }
        }

        // 권한 확인 루프 (더 긴 대기 시간과 재시도 로직)
        int permissionRetries = 0;
        const int maxPermissionRetries = 12; // 60초 동안 시도

        while (permissionRetries < maxPermissionRetries)
        {
            try
            {
                bool hasPermission = plugin.Call<bool>("hasPermission", cameraName);
                if (hasPermission)
                {
                    Debug.Log($"[UVC] {cameraName} 권한 획득 성공!");
                    break;
                }
                else
                {
                    Debug.Log($"[UVC] {cameraName} 권한 대기 중... ({permissionRetries + 1}/{maxPermissionRetries})");

                    // 매 3번째 시도마다 권한 재요청
                    if (permissionRetries % 3 == 2)
                    {
                        plugin.Call("ObtainPermission", cameraName);
                        Debug.Log($"[UVC] {cameraName} 권한 재요청");
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

        // 최종 권한 확인
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
            Debug.LogError($"[UVC] {cameraName} 권한을 얻지 못했습니다. Quest 설정에서 수동으로 권한을 허용하세요.");
            ShowPermissionInstructions();
            yield break;
        }

        // 카메라 실행
        StartCoroutine(RunCamera(cameraName));
    }

    void ShowPermissionInstructions()
    {
        Debug.Log("=== 권한 설정 방법 ===");
        Debug.Log("1. Quest 헤드셋에서 Settings → Apps → [앱 이름] 으로 이동");
        Debug.Log("2. Permissions 섹션에서 Camera 권한 활성화");
        Debug.Log("3. 앱을 다시 시작하세요");
        Debug.Log("====================");
    }

    IEnumerator RunCamera(string cameraName)
    {
        Debug.Log($"[UVC] {cameraName} 카메라 시작");

        string[] infos = null;
        try
        {
            // 카메라 열기
            infos = plugin.Call<string[]>("Open", cameraName);

            // null 체크
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

            Debug.Log($"[UVC] Open 성공. 사용 가능한 포맷 수: {infos.Length}");
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

        // MJPEG 포맷 찾기 (타입 6) - 적절한 해상도 우선 선택
        int goodIndex = -1;
        int[] preferredResolutions = { 640, 848, 960, 1280, 1024 }; // 선호하는 가로 해상도 순서

        // 먼저 선호하는 해상도 중에서 찾기
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
                            Debug.Log($"[UVC] 선호 해상도 MJPEG 포맷 발견: {infos[i]}");
                            break;
                        }
                    }
                }
            }
            if (goodIndex >= 0) break;
        }

        // 선호 해상도가 없다면 첫 번째 MJPEG 사용
        if (goodIndex < 0)
        {
            for (int i = 0; i < infos.Length; i++)
            {
                if (!string.IsNullOrEmpty(infos[i]) && infos[i].StartsWith("6"))
                {
                    goodIndex = i;
                    Debug.Log($"[UVC] 기본 MJPEG 포맷 발견: {infos[i]}");
                    break;
                }
            }
        }

        if (goodIndex < 0)
        {
            Debug.LogError("[UVC] MJPEG 포맷(타입 6)을 찾을 수 없습니다.");
            Debug.LogError("[UVC] 사용 가능한 포맷:");
            for (int i = 0; i < infos.Length; i++)
            {
                Debug.LogError($"[UVC]   {i}: {infos[i]}");
            }
            yield break;
        }

        // 카메라 설정 파싱
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
            Debug.LogError($"[UVC] 카메라 정보 형식이 올바르지 않습니다. 길이: {info?.Length ?? 0}");
            Debug.LogError($"[UVC] 원본 문자열: '{infos[goodIndex]}'");
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
            Debug.LogError($"[UVC] 카메라 설정 파싱 실패: {e.Message}");
            Debug.LogError($"[UVC] info[1]='{info[1]}', info[2]='{info[2]}', info[3]='{info[3]}'");
            yield break;
        }

        var bandwidth = 1.0f;

        Debug.Log($"[UVC] 카메라 설정: {width}x{height} @ {fps}fps");

        int res = -1;
        try
        {
            // 카메라 스트리밍 시작 - 올바른 메서드 시그니처 사용
            // 원본 코드에서는 6개 파라미터였지만, 실제로는 5개 파라미터인 것 같음
            Debug.Log($"[UVC] Start 메서드 호출: {cameraName}, {width}, {height}, {fps}, {bandwidth}");

            // 다양한 시그니처 시도
            try
            {
                // 시도 1: 5개 파라미터 (String, int, int, int, float)
                res = plugin.Call<int>("Start", cameraName, width, height, fps, bandwidth);
                Debug.Log($"[UVC] Start method (5 params) successful: {res}");
            }
            catch (System.Exception e1)
            {
                Debug.LogWarning($"[UVC] Start method (5 params) failed: {e1.Message}");

                try
                {
                    // 시도 2: 4개 파라미터 (String, int, int, int)
                    res = plugin.Call<int>("Start", cameraName, width, height, fps);
                    Debug.Log($"[UVC] Start method (4 params) successful: {res}");
                }
                catch (System.Exception e2)
                {
                    Debug.LogWarning($"[UVC] Start method (4 params) failed: {e2.Message}");

                    try
                    {
                        // 시도 3: startStreaming 메서드 사용
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
            Debug.LogError($"[UVC] 카메라 시작 실패. 오류 코드: {res}");
            yield break;
        }

        Debug.Log("[UVC] 카메라 스트리밍 시작 성공!");

        // 텍스처 생성 및 설정
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
            Debug.Log("[UVC] 텍스처 설정 완료");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[UVC] Texture creation failed: {e.Message}");
            yield break;
        }

        // 프레임 데이터 읽기 루프
        int frameCount = 0;
        int errorCount = 0;
        const int maxErrors = 10;

        while (errorCount < maxErrors)
        {
            sbyte[] frameData = null;
            bool frameSuccess = false;

            // try-catch 블록 분리 (yield 없음)
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

            // 성공한 경우 처리
            if (frameSuccess && frameData != null && frameData.Length > 0)
            {
                try
                {
                    cameraTexture.LoadRawTextureData((byte[])(System.Array)frameData);
                    cameraTexture.Apply(false, false);

                    frameCount++;
                    if (frameCount % 30 == 0) // 30프레임마다 로그
                    {
                        Debug.Log($"[UVC] 프레임 {frameCount} 처리됨");
                    }

                    errorCount = 0; // 성공하면 에러 카운트 리셋
                }
                catch (System.Exception e)
                {
                    errorCount++;
                    Debug.LogError($"[UVC] Texture update error ({errorCount}/{maxErrors}): {e.Message}");
                }
            }
            else if (frameSuccess) // frameData가 null이거나 빈 경우
            {
                errorCount++;
                Debug.LogWarning($"[UVC] 빈 프레임 데이터 ({errorCount}/{maxErrors})");
            }

            // 에러가 발생한 경우에만 대기 (yield를 try-catch 밖으로)
            if (!frameSuccess || (frameData == null || frameData.Length == 0))
            {
                if (errorCount >= maxErrors)
                {
                    Debug.LogError("[UVC] 너무 많은 프레임 에러. 카메라 중지.");
                    break;
                }
                yield return new WaitForSeconds(0.1f); // 에러 발생시 대기
            }
            else
            {
                yield return null; // 정상 처리시 다음 프레임으로
            }
        }

        Debug.LogError("[UVC] 카메라 스트리밍 중단됨");
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

    // 디버그용 - Inspector에서 수동으로 권한 요청
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