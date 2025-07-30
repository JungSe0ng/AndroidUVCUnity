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

        // CameraRenderTarget 배열 초기화
        if (cameraTargets == null || cameraTargets.Length != maxCameras)
        {
            cameraTargets = new CameraRenderTarget[maxCameras];
            for (int i = 0; i < maxCameras; i++)
            {
                cameraTargets[i] = new CameraRenderTarget();
            }
        }

        // 첫 번째 카메라는 자기 자신의 렌더러 사용
        if (cameraTargets[0].targetRenderer == null)
        {
            cameraTargets[0].targetRenderer = GetComponent<Renderer>();
        }

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
        yield return StartCoroutine(FindCamerasAndStart());
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
                Debug.Log($"[MultiCamera] {permission} 권한 요청");
            }
            else
            {
                Debug.Log($"[MultiCamera] {permission} 권한 이미 있음");
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
                    Debug.Log($"[MultiCamera] 발견된 카메라 수: {cameras.Length}");
                    for (int i = 0; i < cameras.Length; i++)
                    {
                        Debug.Log($"[MultiCamera] 카메라 {i}: {cameras[i]}");
                        availableCameras.Add(cameras[i]);
                    }
                    break;
                }
                else
                {
                    Debug.Log($"[MultiCamera] 카메라가 없습니다. 다시 시도합니다... ({retryCount + 1}/{maxRetries})");
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
            Debug.LogError("[MultiCamera] 카메라를 찾을 수 없습니다. 중단합니다.");
            yield break;
        }

        // 최대 maxCameras개까지 카메라 시작
        int camerasToStart = Mathf.Min(cameras.Length, maxCameras);
        for (int i = 0; i < camerasToStart; i++)
        {
            cameraTargets[i].cameraName = cameras[i];
            StartCoroutine(SetupCamera(i));
            yield return new WaitForSeconds(1f); // 카메라 간 초기화 간격
        }
    }

    IEnumerator SetupCamera(int cameraIndex)
    {
        CameraRenderTarget target = cameraTargets[cameraIndex];
        string cameraName = target.cameraName;

        Debug.Log($"[MultiCamera] 카메라 {cameraIndex} ({cameraName}) 설정 시작");

        // 권한 요청 및 확인
        yield return StartCoroutine(RequestPermissionAndCheck(cameraName, cameraIndex));

        if (!HasCameraPermission(cameraName))
        {
            Debug.LogError($"[MultiCamera] 카메라 {cameraIndex} ({cameraName}) 권한 획득 실패");
            yield break;
        }

        // 카메라 시작
        yield return StartCoroutine(RunCamera(cameraIndex));
    }

    IEnumerator RequestPermissionAndCheck(string cameraName, int cameraIndex)
    {
        Debug.Log($"[MultiCamera] 카메라 {cameraIndex} ({cameraName})에 대한 권한 요청 시작");

        // 권한 요청
        try
        {
            plugin.Call("ObtainPermission", cameraName);
            Debug.Log($"[MultiCamera] 카메라 {cameraIndex} ({cameraName}) 권한 요청 완료");
        }
        catch (Exception e)
        {
            Debug.LogError($"[MultiCamera] 카메라 {cameraIndex} 권한 요청 실패: {e.Message}");
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
                    Debug.Log($"[MultiCamera] 카메라 {cameraIndex} ({cameraName}) 권한 획득 성공!");
                    yield break;
                }
                else
                {
                    Debug.Log($"[MultiCamera] 카메라 {cameraIndex} ({cameraName}) 권한 대기 중... ({permissionRetries + 1}/{maxPermissionRetries})");

                    // 매 2번째 시도마다 권한 재요청
                    if (permissionRetries % 2 == 1)
                    {
                        plugin.Call("ObtainPermission", cameraName);
                        Debug.Log($"[MultiCamera] 카메라 {cameraIndex} ({cameraName}) 권한 재요청");
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[MultiCamera] 카메라 {cameraIndex} 권한 확인 실패: {e.Message}");
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
            Debug.LogError($"[MultiCamera] 권한 확인 실패: {e.Message}");
            return false;
        }
    }

    IEnumerator RunCamera(int cameraIndex)
    {
        CameraRenderTarget target = cameraTargets[cameraIndex];
        string cameraName = target.cameraName;

        Debug.Log($"[MultiCamera] 카메라 {cameraIndex} ({cameraName}) 시작");

        // 디바이스 정보 확인
        try
        {
            string deviceInfo = plugin.Call<string>("GetUSBDeviceInfo", cameraName);
            Debug.Log($"[MultiCamera] 카메라 {cameraIndex} 디바이스 정보:\n{deviceInfo}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[MultiCamera] 카메라 {cameraIndex} 디바이스 정보 가져오기 실패: {e.Message}");
        }

        // 기존 연결이 있다면 닫기
        try
        {
            plugin.Call("Close", cameraName);
            Debug.Log($"[MultiCamera] 카메라 {cameraIndex} 기존 연결 닫기 시도 완료");
        }
        catch (Exception closeEx)
        {
            Debug.Log($"[MultiCamera] 카메라 {cameraIndex} Close 시도 (무시됨): {closeEx.Message}");
        }

        yield return new WaitForSeconds(1f);

        // 카메라 열기
        string[] infos = null;
        bool openSuccess = false;

        try
        {
            Debug.Log($"[MultiCamera] 카메라 {cameraIndex} ({cameraName}) 열기 시도");
            infos = plugin.Call<string[]>("Open", cameraName);
            openSuccess = (infos != null);
        }
        catch (Exception e)
        {
            Debug.LogError($"[MultiCamera] 카메라 {cameraIndex} Open 실패: {e.Message}");
            openSuccess = false;
        }

        if (!openSuccess || infos == null)
        {
            Debug.LogError($"[MultiCamera] 카메라 {cameraIndex} Open 실패");
            yield break;
        }

        Debug.Log($"[MultiCamera] 카메라 {cameraIndex} Open 성공! 사용 가능한 포맷 수: {infos.Length}");

        // MJPEG 포맷 찾기
        int goodIndex = FindBestMJPEGFormat(infos, cameraIndex);
        if (goodIndex < 0)
        {
            Debug.LogError($"[MultiCamera] 카메라 {cameraIndex} MJPEG 포맷을 찾을 수 없습니다.");
            yield break;
        }

        // 카메라 설정 파싱 및 시작
        yield return StartCoroutine(StartCameraStream(cameraIndex, infos, goodIndex));
    }

    int FindBestMJPEGFormat(string[] infos, int cameraIndex)
    {
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
                        int width = int.Parse(parts[1]);
                        if (width == prefWidth)
                        {
                            goodIndex = i;
                            Debug.Log($"[MultiCamera] 카메라 {cameraIndex} 선호 해상도 MJPEG 포맷 발견: {infos[i]}");
                            return goodIndex;
                        }
                    }
                }
            }
        }

        // 선호 해상도가 없다면 첫 번째 MJPEG 사용
        for (int i = 0; i < infos.Length; i++)
        {
            if (!string.IsNullOrEmpty(infos[i]) && infos[i].StartsWith("6"))
            {
                goodIndex = i;
                Debug.Log($"[MultiCamera] 카메라 {cameraIndex} 기본 MJPEG 포맷 발견: {infos[i]}");
                break;
            }
        }

        return goodIndex;
    }

    IEnumerator StartCameraStream(int cameraIndex, string[] infos, int formatIndex)
    {
        CameraRenderTarget target = cameraTargets[cameraIndex];
        string cameraName = target.cameraName;

        // 해상도 설정들 (카메라별로 다른 해상도 사용 가능)
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
                Debug.Log($"[MultiCamera] 카메라 {cameraIndex} 해상도 시도: {testWidth}x{testHeight}@{testFps}fps, Format {testMode}");
                int res = plugin.Call<int>("Start", cameraName, testWidth, testHeight, testFps, testMode, testBandwidth, true, false);

                if (res == 0)
                {
                    Debug.Log($"[MultiCamera] 카메라 {cameraIndex} 성공! 해상도: {testWidth}x{testHeight}");
                    width = testWidth;
                    height = testHeight;
                    fps = testFps;
                    startSuccess = true;
                    break;
                }
                else
                {
                    Debug.LogWarning($"[MultiCamera] 카메라 {cameraIndex} 실패: {testWidth}x{testHeight} (에러: {res})");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[MultiCamera] 카메라 {cameraIndex} 시도 에러: {e.Message}");
            }

            yield return new WaitForSeconds(0.3f);
        }

        if (!startSuccess)
        {
            Debug.LogError($"[MultiCamera] 카메라 {cameraIndex} 모든 시도 실패");
            yield break;
        }

        // 텍스처 생성
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
                Debug.Log($"[MultiCamera] 카메라 {cameraIndex} 텍스처 설정 완료: {width}x{height}");
            }
            else
            {
                Debug.LogError($"[MultiCamera] 카메라 {cameraIndex} Renderer 또는 Material이 없습니다!");
                yield break;
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[MultiCamera] 카메라 {cameraIndex} 텍스처 생성 실패: {e.Message}");
            yield break;
        }

        // 프레임 렌더링 코루틴 시작
        target.renderCoroutine = StartCoroutine(RenderCameraFrames(cameraIndex));
    }

    IEnumerator RenderCameraFrames(int cameraIndex)
    {
        CameraRenderTarget target = cameraTargets[cameraIndex];
        string cameraName = target.cameraName;

        int frameCount = 0;
        int errorCount = 0;
        const int maxErrors = 10;

        Debug.Log($"[MultiCamera] 카메라 {cameraIndex} 프레임 렌더링 시작");

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
                Debug.LogError($"[MultiCamera] 카메라 {cameraIndex} 프레임 데이터 에러 ({errorCount}/{maxErrors}): {e.Message}");
                frameSuccess = false;
            }

            if (frameSuccess && frameData != null && frameData.Length > 0)
            {
                try
                {
                    target.cameraTexture.LoadRawTextureData((byte[])(System.Array)frameData);
                    target.cameraTexture.Apply(false, false);

                    frameCount++;
                    if (frameCount % 60 == 0) // 60프레임마다 로그
                    {
                        Debug.Log($"[MultiCamera] 카메라 {cameraIndex} 프레임 {frameCount} 처리됨");
                    }

                    errorCount = 0; // 성공하면 에러 카운트 리셋
                }
                catch (Exception e)
                {
                    errorCount++;
                    Debug.LogError($"[MultiCamera] 카메라 {cameraIndex} 텍스처 업데이트 에러 ({errorCount}/{maxErrors}): {e.Message}");
                }
            }
            else if (frameSuccess)
            {
                errorCount++;
                Debug.LogWarning($"[MultiCamera] 카메라 {cameraIndex} 빈 프레임 데이터 ({errorCount}/{maxErrors})");
            }

            if (!frameSuccess || (frameData == null || frameData.Length == 0))
            {
                if (errorCount >= maxErrors)
                {
                    Debug.LogError($"[MultiCamera] 카메라 {cameraIndex} 너무 많은 프레임 에러. 중지.");
                    break;
                }
                yield return new WaitForSeconds(0.1f);
            }
            else
            {
                yield return null;
            }
        }

        Debug.LogError($"[MultiCamera] 카메라 {cameraIndex} 스트리밍 중단됨");
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
                        Debug.Log($"[MultiCamera] 카메라 {i} ({target.cameraName}) 중지 및 닫기 완료");
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"[MultiCamera] 카메라 {i} 정리 실패: {e.Message}");
                }
            }
        }
    }

    // 디버그용 메서드들
    [ContextMenu("Manual Permission Request All")]
    void ManualPermissionRequestAll()
    {
        for (int i = 0; i < cameraTargets.Length; i++)
        {
            CameraRenderTarget target = cameraTargets[i];
            if (plugin != null && !string.IsNullOrEmpty(target.cameraName))
            {
                plugin.Call("ObtainPermission", target.cameraName);
                Debug.Log($"[MultiCamera] 카메라 {i} 수동 권한 요청 전송");
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

    // 런타임에서 카메라 상태 확인
    public void GetCameraStatus()
    {
        Debug.Log("=== 카메라 상태 ===");
        for (int i = 0; i < cameraTargets.Length; i++)
        {
            CameraRenderTarget target = cameraTargets[i];
            Debug.Log($"카메라 {i}: {target.cameraName}, 활성: {target.isActive}, 해상도: {target.width}x{target.height}");
        }
        Debug.Log("==================");
    }
}