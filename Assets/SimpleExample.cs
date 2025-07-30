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
        // 권한 요청
        RequestAndroidPermissions();
        yield return new WaitForSeconds(2f);

        // 플러그인 초기화
        InitializePlugin();
        yield return new WaitForSeconds(1f);

        // 카메라 찾기 및 시작
        yield return StartCoroutine(FindCameraAndStart());
    }

    void RequestAndroidPermissions()
    {
        // Quest 3 전용 권한들
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
                Debug.Log($"[UVC] {permission} 권한 요청");
            }
            else
            {
                Debug.Log($"[UVC] {permission} 권한 이미 있음");
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
                    Debug.Log("[UVC] 카메라 발견: " + cameras[0]);
                    currentCameraName = cameras[0];
                    break;
                }
                else
                {
                    Debug.Log($"[UVC] 카메라가 없습니다. 다시 시도합니다... ({retryCount + 1}/{maxRetries})");
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
            Debug.LogError("[UVC] 카메라를 찾을 수 없습니다. 중단합니다.");
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

        // 권한 확인 루프
        int permissionRetries = 0;
        const int maxPermissionRetries = 6;

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

                    // 매 2번째 시도마다 권한 재요청
                    if (permissionRetries % 2 == 1)
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
            Debug.LogError($"[UVC] {cameraName} 권한을 얻지 못했습니다. 중단합니다.");
            ShowPermissionInstructions();
            yield break;
        }

        // 카메라 실행
        StartCoroutine(RunCamera(cameraName));
    }

    void ShowPermissionInstructions()
    {
        Debug.Log("=== 권한 설정 방법 ===");
        Debug.Log("1. Quest 헤드셋에서 Settings -> Apps -> [앱 이름] 으로 이동");
        Debug.Log("2. Permissions 섹션에서 Camera 권한 활성화");
        Debug.Log("3. 앱을 다시 시작하세요");
        Debug.Log("====================");
    }

    IEnumerator RunCamera(string cameraName)
    {
        Debug.Log($"[UVC] {cameraName} 카메라 시작");

        // 디바이스 정보 확인
        try
        {
            string deviceInfo = plugin.Call<string>("GetUSBDeviceInfo", cameraName);
            Debug.Log($"[UVC] 디바이스 정보:\n{deviceInfo}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[UVC] 디바이스 정보 가져오기 실패: {e.Message}");
        }

        // 권한 재확인
        bool hasPermission = false;
        try
        {
            hasPermission = plugin.Call<bool>("hasPermission", cameraName);
            Debug.Log($"[UVC] 권한 상태: {hasPermission}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[UVC] 권한 확인 실패: {e.Message}");
            yield break;
        }

        if (!hasPermission)
        {
            Debug.LogError("[UVC] 권한이 없습니다!");
            yield break;
        }

        // 먼저 기존 연결이 있다면 닫기
        try
        {
            plugin.Call("Close", cameraName);
            Debug.Log("[UVC] 기존 연결 닫기 시도 완료");
        }
        catch (Exception closeEx)
        {
            Debug.Log($"[UVC] Close 시도 (무시됨): {closeEx.Message}");
        }

        yield return new WaitForSeconds(1f);

        string[] infos = null;
        bool openSuccess = false;

        // 첫 번째 카메라 열기 시도
        try
        {
            Debug.Log($"[UVC] '{cameraName}' 카메라 열기 시도");
            infos = plugin.Call<string[]>("Open", cameraName);
            openSuccess = (infos != null);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[UVC] 첫 번째 Camera open failed: {e.Message}");
            openSuccess = false;
        }

        // 첫 번째 시도가 실패하면 재시도
        if (!openSuccess || infos == null)
        {
            Debug.LogError("[UVC] Open 메서드가 null을 반환했습니다.");
            Debug.LogError("[UVC] 이는 네이티브 openCamera 함수가 실패했음을 의미합니다.");

            yield return new WaitForSeconds(3f);

            try
            {
                Debug.Log("[UVC] 카메라 열기 재시도...");
                infos = plugin.Call<string[]>("Open", cameraName);
                openSuccess = (infos != null);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[UVC] 재시도 Camera open failed: {e.Message}");
                openSuccess = false;
            }

            if (!openSuccess || infos == null)
            {
                Debug.LogError("[UVC] 재시도도 실패했습니다. 카메라를 사용할 수 없습니다.");
                yield break;
            }
        }

        Debug.Log($"[UVC] Open 성공! 사용 가능한 포맷 수: {infos.Length}");
        for (int i = 0; i < infos.Length; i++)
        {
            Debug.Log($"[UVC] Format {i}: {infos[i]}");
        }

        // 카메라의 실제 디스크립터 정보 확인
        try
        {
            string descriptorInfo = plugin.Call<string>("getDescriptor", 0);
            Debug.Log($"[UVC] 카메라 디스크립터 상세 정보:\n{descriptorInfo}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[UVC] 디스크립터 정보 가져오기 실패: {e.Message}");
        }

        // MJPEG 포맷 찾기 (타입 6) - 적절한 해상도 우선 선택
        int goodIndex = -1;
        int[] preferredResolutions = { 640, 848, 960, 1280, 1024 };

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
        bool startSuccess = false;

        // 로그에서 확인된 실제 포맷 인덱스를 사용 (mode가 포맷 인덱스)
        // Format 8: 6,640,480,30 - 640x480 MJPEG
        // Format 5: 6,848,480,30 - 848x480 MJPEG  
        // Format 7: 6,1280,720,30 - 1280x720 MJPEG
        // Format 9: 6,1920,1080,30 - 1920x1080 MJPEG
        // Format 4: 6,640,360,30 - 640x360 MJPEG
        // Format 3: 6,424,240,30 - 424x240 MJPEG
        // Format 2: 6,320,240,30 - 320x240 MJPEG (이전 성공)

        int[][] resolutionSettings = {
            new int[] {1920, 1080, 30, 9, 5},   // Format 9: 1920x1080, bw=0.05
            new int[] {1280, 720, 30, 7, 8},    // Format 7: 1280x720, bw=0.08
            new int[] {848, 480, 30, 5, 12},    // Format 5: 848x480, bw=0.12
            new int[] {640, 480, 30, 8, 15},    // Format 8: 640x480, bw=0.15
            new int[] {640, 360, 30, 4, 18},    // Format 4: 640x360, bw=0.18
            new int[] {424, 240, 30, 3, 20},    // Format 3: 424x240, bw=0.2
            new int[] {320, 240, 30, 2, 10},    // Format 2: 320x240, bw=0.1 (이전 성공)
        };

        for (int i = 0; i < resolutionSettings.Length; i++)
        {
            int testWidth = resolutionSettings[i][0];
            int testHeight = resolutionSettings[i][1];
            int testFps = resolutionSettings[i][2];
            int testMode = resolutionSettings[i][3]; // 이제 실제 포맷 인덱스
            float testBandwidth = resolutionSettings[i][4] / 100.0f;

            try
            {
                Debug.Log($"[UVC] 해상도 시도 {i + 1}: {testWidth}x{testHeight}@{testFps}fps, Format {testMode}, bw={testBandwidth}");
                res = plugin.Call<int>("Start", cameraName, testWidth, testHeight, testFps, testMode, testBandwidth, true, false);
                Debug.Log($"[UVC] 결과: {res}");

                if (res == 0)
                {
                    Debug.Log($"[UVC] 성공! 해상도: {testWidth}x{testHeight}, Format {testMode}");
                    width = testWidth;
                    height = testHeight;
                    fps = testFps;
                    startSuccess = true;
                    break;
                }
                else
                {
                    Debug.LogWarning($"[UVC] 실패: {testWidth}x{testHeight} Format {testMode} (에러: {res})");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[UVC] 해상도 {testWidth}x{testHeight} Format {testMode} 시도 에러: {e.Message}");
            }

            yield return new WaitForSeconds(0.3f);
        }

        if (!startSuccess)
        {
            Debug.LogError($"[UVC] 모든 시도 실패. 마지막 오류 코드: {res}");
            Debug.LogError("[UVC] 카메라 포맷이 지원되지 않거나 하드웨어 문제일 수 있습니다.");
            Debug.LogError("[UVC] 시도를 중단합니다.");
            yield break;
        }

        Debug.Log("[UVC] 카메라 스트리밍 시작 성공!");

        // 텍스처 생성 및 설정
        Texture2D cameraTexture = null;
        try
        {
            // MJPEG는 RGB24로 디코딩됨
            cameraTexture = new Texture2D(width, height, TextureFormat.RGB24, false);
            Debug.Log($"[UVC] 텍스처 생성: {width}x{height}, RGB24");

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

    // 디버그용 - Inspector에서 수동으로 카메라 재시작
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