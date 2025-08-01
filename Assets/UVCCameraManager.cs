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

        // CameraRenderTarget 배열 초기화
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
        UpdateStatusText("초기화 중...");
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
        UpdateStatusText("권한 요청 중...");
        RequestAndroidPermissions();
        yield return new WaitForSeconds(2f);

        if (!isInitialized)
        {
            UpdateStatusText("플러그인 초기화 중...");
            InitializePlugin();
            yield return new WaitForSeconds(1f);
            isInitialized = true;
        }

        UpdateStatusText("카메라 검색 중...");
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
                    UpdateStatusText($"발견된 카메라 수: {cameras.Length}");
                    availableCameras.Clear();
                    for (int i = 0; i < cameras.Length; i++)
                    {
                        Debug.Log($"[MultiCamera] 카메라 {i}: {cameras[i]}");
                        availableCameras.Add(cameras[i]);
                    }
                    break;
                }
                else
                {
                    UpdateStatusText($"카메라 검색 중... ({retryCount + 1}/{maxRetries})");
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[MultiCamera] GetUSBDevices failed: " + e.Message);
                UpdateStatusText($"카메라 검색 실패: {e.Message}");
            }

            retryCount++;
            yield return new WaitForSeconds(1f);
        }

        if (cameras == null || cameras.Length == 0)
        {
            UpdateStatusText("카메라를 찾을 수 없습니다. 재연결 버튼을 눌러주세요.");
            yield break;
        }

        int camerasToStart = Mathf.Min(cameras.Length, maxCameras);
        UpdateStatusText($"{camerasToStart}개 카메라 연결 시작...");

        int successCount = 0;
        for (int i = 0; i < camerasToStart; i++)
        {
            cameraTargets[i].cameraName = cameras[i];
            bool success = false;
            yield return StartCoroutine(SetupCamera(i, (result) => success = result));
            if (success) successCount++;
            yield return new WaitForSeconds(1f);
        }

        UpdateStatusText($"카메라 연결 완료: {successCount}/{camerasToStart}개 성공");
    }

    IEnumerator SetupCamera(int cameraIndex, System.Action<bool> onComplete = null)
    {
        CameraRenderTarget target = cameraTargets[cameraIndex];
        string cameraName = target.cameraName;

        Debug.Log($"[MultiCamera] 카메라 {cameraIndex} ({cameraName}) 설정 시작");
        UpdateStatusText($"카메라 {cameraIndex + 1} 설정 중...");

        yield return StartCoroutine(RequestPermissionAndCheck(cameraName, cameraIndex));

        if (!HasCameraPermission(cameraName))
        {
            Debug.LogError($"[MultiCamera] 카메라 {cameraIndex} ({cameraName}) 권한 획득 실패");
            UpdateStatusText($"카메라 {cameraIndex + 1} 권한 실패");
            if (onComplete != null) onComplete(false);
            yield break;
        }

        bool success = false;
        yield return StartCoroutine(RunCamera(cameraIndex, (result) => success = result));
        if (onComplete != null) onComplete(success);
    }

    IEnumerator RequestPermissionAndCheck(string cameraName, int cameraIndex)
    {
        Debug.Log($"[MultiCamera] 카메라 {cameraIndex} ({cameraName})에 대한 권한 요청 시작");

        try
        {
            plugin.Call("ObtainPermission", cameraName);
            Debug.Log($"[MultiCamera] 카메라 {cameraIndex} ({cameraName}) 권한 요청 완료");
        }
        catch (Exception e)
        {
            Debug.LogError($"[MultiCamera] 카메라 {cameraIndex} 권한 요청 실패: {e.Message}");
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
                    Debug.Log($"[MultiCamera] 카메라 {cameraIndex} ({cameraName}) 권한 획득 성공!");
                    yield break;
                }
                else
                {
                    Debug.Log($"[MultiCamera] 카메라 {cameraIndex} ({cameraName}) 권한 대기 중... ({permissionRetries + 1}/{maxPermissionRetries})");

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

    IEnumerator RunCamera(int cameraIndex, System.Action<bool> onComplete = null)
    {
        CameraRenderTarget target = cameraTargets[cameraIndex];
        string cameraName = target.cameraName;

        Debug.Log($"[MultiCamera] 카메라 {cameraIndex} ({cameraName}) 시작");

        try
        {
            string deviceInfo = plugin.Call<string>("GetUSBDeviceInfo", cameraName);
            Debug.Log($"[MultiCamera] 카메라 {cameraIndex} 디바이스 정보:\n{deviceInfo}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[MultiCamera] 카메라 {cameraIndex} 디바이스 정보 가져오기 실패: {e.Message}");
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
                Debug.Log($"[MultiCamera] 카메라 {cameraIndex} ({cameraName}) 열기 시도 {openRetries + 1}/{maxOpenRetries}");
                infos = plugin.Call<string[]>("Open", cameraName);
                openSuccess = (infos != null && infos.Length > 0);

                if (openSuccess)
                {
                    Debug.Log($"[MultiCamera] 카메라 {cameraIndex} Open 성공! 사용 가능한 포맷 수: {infos.Length}");
                }
                else
                {
                    Debug.LogWarning($"[MultiCamera] 카메라 {cameraIndex} Open 실패 - null 또는 빈 배열 반환");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[MultiCamera] 카메라 {cameraIndex} Open 시도 {openRetries + 1} 실패: {e.Message}");
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
            Debug.LogError($"[MultiCamera] 카메라 {cameraIndex} Open 실패");
            UpdateStatusText($"카메라 {cameraIndex + 1} 열기 실패");
            if (onComplete != null) onComplete(false);
            yield break;
        }

        bool streamSuccess = false;
        yield return StartCoroutine(StartCameraStream(cameraIndex, (result) => streamSuccess = result));

        if (streamSuccess)
        {
            UpdateStatusText($"카메라 {cameraIndex + 1} 연결 성공");
        }

        if (onComplete != null) onComplete(streamSuccess);
    }

    IEnumerator StartCameraStream(int cameraIndex, System.Action<bool> onComplete = null)
    {
        CameraRenderTarget target = cameraTargets[cameraIndex];
        string cameraName = target.cameraName;

        Debug.Log($"[MultiCamera] 카메라 {cameraIndex} 스트림 시작");

        bool startSuccess = false;
        int width = 1920, height = 1080, fps = 30;
        int format = 9; // UVC_FRAME_FORMAT_MJPEG
        float bandwidth = 0.3f;

        Debug.Log($"[MultiCamera] 카메라 {cameraIndex} MJPEG 1080p 시도: {width}x{height}@{fps}fps, Format {format}, BW {bandwidth}");

        int res = plugin.Call<int>("Start", cameraName, width, height, fps, format, bandwidth, true, false);

        if (res == 0)
        {
            Debug.Log($"[MultiCamera] 카메라 {cameraIndex} MJPEG 1080p 성공!");
            startSuccess = true;
        }
        else
        {
            Debug.LogError($"[MultiCamera] 카메라 {cameraIndex} MJPEG 1080p 실패 (에러: {res})");

            width = 1280; height = 720;
            Debug.Log($"[MultiCamera] 카메라 {cameraIndex} MJPEG 720p fallback 시도");

            res = plugin.Call<int>("Start", cameraName, width, height, fps, format, bandwidth, true, false);

            if (res == 0)
            {
                Debug.Log($"[MultiCamera] 카메라 {cameraIndex} MJPEG 720p 성공!");
                startSuccess = true;
            }
            else
            {
                Debug.LogError($"[MultiCamera] 카메라 {cameraIndex} MJPEG 720p 실패 (에러: {res})");

                // 480p로 최종 fallback
                width = 640; height = 480;
                Debug.Log($"[MultiCamera] 카메라 {cameraIndex} MJPEG 480p 최종 시도");

                res = plugin.Call<int>("Start", cameraName, width, height, fps, format, bandwidth, true, false);

                if (res == 0)
                {
                    Debug.Log($"[MultiCamera] 카메라 {cameraIndex} MJPEG 480p 성공!");
                    startSuccess = true;
                }
                else
                {
                    Debug.LogError($"[MultiCamera] 카메라 {cameraIndex} 모든 해상도 실패 (에러: {res})");
                }
            }
        }

        if (!startSuccess)
        {
            Debug.LogError($"[MultiCamera] 카메라 {cameraIndex} 스트림 시작 실패");
            if (onComplete != null) onComplete(false);
            yield break;
        }

        // 텍스처 생성
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
            Debug.Log($"[MultiCamera] 카메라 {cameraIndex} 텍스처 설정 완료: {width}x{height}");
        }
        else
        {
            Debug.LogError($"[MultiCamera] 카메라 {cameraIndex} RawImage가 할당되지 않았습니다!");
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
                // 60프레임마다 한 번만 데이터 상태 로그
                if (frameCount % 60 == 0)
                {
                    Debug.Log($"[MultiCamera] 카메라 {cameraIndex} 프레임 데이터: 크기={frameData.Length}, 첫 10바이트: [{string.Join(",", frameData.Take(10))}]");
                }

                try
                {
                    target.cameraTexture.LoadRawTextureData((byte[])(System.Array)frameData);
                    target.cameraTexture.Apply(false, false);

                    frameCount++;
                    if (frameCount % 60 == 0)
                    {
                        Debug.Log($"[MultiCamera] 카메라 {cameraIndex} 프레임 {frameCount} 처리됨");
                    }

                    errorCount = 0;
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
                Debug.LogWarning($"[MultiCamera] 카메라 {cameraIndex} 빈 프레임 데이터 ({errorCount}/{maxErrors}), 길이: {frameData?.Length ?? -1}");
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

    public void GetCameraStatus()
    {
        string statusMessage = "=== 카메라 상태 ===\n";

        for (int i = 0; i < cameraTargets.Length; i++)
        {
            CameraRenderTarget target = cameraTargets[i];
            statusMessage += $"카메라 {i}: {target.cameraName}, 활성: {target.isActive}, 해상도: {target.width}x{target.height}\n";

            if (!string.IsNullOrEmpty(target.cameraName))
            {
                try
                {
                    int frameNumber = plugin.Call<int>("GetFrameNumber", target.cameraName);
                    statusMessage += $"  프레임 번호: {frameNumber}\n";

                    sbyte[] testFrame = plugin.Call<sbyte[]>("GetFrameData", target.cameraName);
                    if (testFrame != null)
                    {
                        statusMessage += $"  프레임 크기: {testFrame.Length}\n";

                        bool hasNonZero = false;
                        for (int j = 0; j < Math.Min(100, testFrame.Length); j++)
                        {
                            if (testFrame[j] != 0)
                            {
                                hasNonZero = true;
                                break;
                            }
                        }
                        statusMessage += $"  비어있지 않은 데이터: {hasNonZero}\n";
                    }
                }
                catch (System.Exception e)
                {
                    statusMessage += $"  플러그인 에러: {e.Message}\n";
                }
            }
        }
        statusMessage += "==================";

        Debug.Log(statusMessage);
        UpdateStatusText("카메라 상태 확인됨");
    }


    public IEnumerator ReconnectCameras()
    {
        if (isReconnecting)
        {
            UpdateStatusText("이미 재연결 중입니다...");
            yield break;
        }

        isReconnecting = true;
        UpdateStatusText("비활성 카메라 재연결 중...");

        // 1. 현재 사용 가능한 카메라 목록 다시 가져오기
        string[] currentCameras = null;
        try
        {
            currentCameras = plugin.Call<string[]>("GetUSBDevices");
            if (currentCameras != null)
            {
                UpdateStatusText($"발견된 카메라 수: {currentCameras.Length}");
                availableCameras.Clear();
                availableCameras.AddRange(currentCameras);
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[MultiCamera] 카메라 목록 가져오기 실패: {e.Message}");
            UpdateStatusText("카메라 목록 가져오기 실패");
            isReconnecting = false;
            yield break;
        }

        if (currentCameras == null || currentCameras.Length == 0)
        {
            UpdateStatusText("연결 가능한 카메라가 없습니다.");
            isReconnecting = false;
            yield break;
        }

        int reconnectedCount = 0;
        for (int i = 0; i < cameraTargets.Length; i++)
        {
            CameraRenderTarget target = cameraTargets[i];

            if (target.isActive && target.cameraTexture != null && !string.IsNullOrEmpty(target.cameraName))
            {
                Debug.Log($"[MultiCamera] 카메라 {i} ({target.cameraName})는 이미 활성 상태 - 건너뜀");
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
                UpdateStatusText($"카메라 {i + 1} 재연결 시도: {target.cameraName}");

                bool success = false;
                yield return StartCoroutine(SetupCamera(i, (result) => success = result));

                if (success)
                {
                    reconnectedCount++;
                    UpdateStatusText($"카메라 {i + 1} 재연결 성공");
                }
                else
                {
                    UpdateStatusText($"카메라 {i + 1} 재연결 실패");
                }

                yield return new WaitForSeconds(1f);
            }
        }

        UpdateStatusText($"재연결 완료: {reconnectedCount}개 카메라 재연결됨");
        isReconnecting = false;
    }

    void StopSingleCamera(int cameraIndex)
    {
        if (cameraIndex < 0 || cameraIndex >= cameraTargets.Length) return;

        CameraRenderTarget target = cameraTargets[cameraIndex];
        if (target == null) return;

        Debug.Log($"[MultiCamera] 카메라 {cameraIndex} ({target.cameraName}) 중지 시작");

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
                Debug.Log($"[MultiCamera] 카메라 {cameraIndex} ({target.cameraName}) 플러그인 종료 완료");
            }
        }
        catch (Exception e)
        {
            Debug.Log($"[MultiCamera] 카메라 {cameraIndex} 플러그인 종료 시도: {e.Message}");
        }

        // 설정 초기화
        target.cameraName = "";
        target.width = 1920;
        target.height = 1080;
        target.fps = 30;

        Debug.Log($"[MultiCamera] 카메라 {cameraIndex} 중지 완료");
    }

    void StopAllCameras()
    {
        UpdateStatusText("모든 카메라 중지 중...");

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
                        Debug.Log($"[MultiCamera] 카메라 {i} ({target.cameraName}) 중지 완료");
                    }
                }
                catch (Exception e)
                {
                    Debug.Log($"[MultiCamera] 카메라 {i} 정리 시도: {e.Message}");
                }

                target.cameraName = "";
                target.width = 1920;
                target.height = 1080;
                target.fps = 30;
            }
        }

        UpdateStatusText("모든 카메라 중지 완료");
    }

    void OnDestroy()
    {
        StopAllCameras();
    }

}