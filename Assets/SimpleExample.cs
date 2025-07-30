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

        // UI 버튼 이벤트 연결
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

        // 권한 요청
        RequestAndroidPermissions();
        yield return new WaitForSeconds(2f);

        // 플러그인 초기화
        if (!isInitialized)
        {
            UpdateStatusText("플러그인 초기화 중...");
            InitializePlugin();
            yield return new WaitForSeconds(1f);
            isInitialized = true;
        }

        // 카메라 찾기 및 시작
        UpdateStatusText("카메라 검색 중...");
        yield return StartCoroutine(FindCamerasAndStart());
    }

    // 카메라 재연결 메서드 - 연결 끊어진 카메라 정리 후 새 카메라 연결
    public IEnumerator ReconnectCameras()
    {
        if (isReconnecting)
        {
            UpdateStatusText("이미 재연결 중입니다...");
            yield break;
        }

        isReconnecting = true;
        UpdateStatusText("카메라 상태 확인 중...");

        // 1단계: 현재 연결된 실제 카메라 목록 가져오기
        string[] allCameras = null;
        try
        {
            allCameras = plugin.Call<string[]>("GetUSBDevices");
        }
        catch (Exception e)
        {
            UpdateStatusText($"카메라 검색 실패: {e.Message}");
            isReconnecting = false;
            yield break;
        }

        List<string> activeCameraList = new List<string>();
        if (allCameras != null)
        {
            activeCameraList.AddRange(allCameras);
        }

        // 2단계: 연결이 끊어진 카메라 정리
        int cleanedCameras = 0;
        for (int i = 0; i < cameraTargets.Length; i++)
        {
            CameraRenderTarget target = cameraTargets[i];

            // 활성화되어 있지만 실제로는 연결되지 않은 카메라 찾기
            if (target.isActive && !string.IsNullOrEmpty(target.cameraName))
            {
                if (!activeCameraList.Contains(target.cameraName))
                {
                    // 연결이 끊어진 카메라 정리
                    UpdateStatusText($"카메라 {i + 1} 연결 끊어짐 감지, 정리 중...");

                    target.isActive = false;
                    if (target.renderCoroutine != null)
                    {
                        StopCoroutine(target.renderCoroutine);
                        target.renderCoroutine = null;
                    }

                    // 텍스처 정리
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
            UpdateStatusText($"{cleanedCameras}개의 끊어진 카메라 정리 완료");
            yield return new WaitForSeconds(1f);
        }

        // 3단계: 현재 연결된 카메라들의 이름을 다시 저장
        List<string> currentActiveCameras = new List<string>();
        for (int i = 0; i < cameraTargets.Length; i++)
        {
            if (cameraTargets[i].isActive && !string.IsNullOrEmpty(cameraTargets[i].cameraName))
            {
                currentActiveCameras.Add(cameraTargets[i].cameraName);
            }
        }

        UpdateStatusText("새 카메라 검색 중...");

        // 4단계: 새로 연결된 카메라 찾기
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
            UpdateStatusText("새로 연결된 카메라가 없습니다.");
            isReconnecting = false;
            yield break;
        }

        UpdateStatusText($"{newCameras.Count}개의 새 카메라 발견!");

        // 5단계: 빈 슬롯에 새 카메라 연결
        int connectedNewCameras = 0;
        for (int i = 0; i < cameraTargets.Length && connectedNewCameras < newCameras.Count; i++)
        {
            // 비어있는 슬롯 찾기
            if (!cameraTargets[i].isActive || string.IsNullOrEmpty(cameraTargets[i].cameraName))
            {
                cameraTargets[i].cameraName = newCameras[connectedNewCameras];
                UpdateStatusText($"카메라 {i + 1}에 새 카메라 연결 시도...");

                bool success = false;
                yield return StartCoroutine(SetupCamera(i, (result) => success = result));

                if (success)
                {
                    UpdateStatusText($"카메라 {i + 1} 새 카메라 연결 성공!");
                    connectedNewCameras++;
                }
                else
                {
                    UpdateStatusText($"카메라 {i + 1} 새 카메라 연결 실패");
                    cameraTargets[i].cameraName = ""; // 실패시 이름 초기화
                }

                yield return new WaitForSeconds(1f);
            }
        }

        // 6단계: 결과 보고
        string resultMessage = "";
        if (cleanedCameras > 0 && connectedNewCameras > 0)
        {
            resultMessage = $"정리 {cleanedCameras}개, 신규연결 {connectedNewCameras}개";
        }
        else if (cleanedCameras > 0)
        {
            resultMessage = $"끊어진 카메라 {cleanedCameras}개 정리완료";
        }
        else if (connectedNewCameras > 0)
        {
            resultMessage = $"새 카메라 {connectedNewCameras}개 연결완료";
        }
        else
        {
            resultMessage = "변경사항 없음 - 모든 카메라 정상";
        }

        UpdateStatusText(resultMessage);
        isReconnecting = false;
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
                    UpdateStatusText($"발견된 카메라 수: {cameras.Length}");
                    availableCameras.Clear(); // 기존 리스트 클리어
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

        // 최대 maxCameras개까지 카메라 시작
        int camerasToStart = Mathf.Min(cameras.Length, maxCameras);
        UpdateStatusText($"{camerasToStart}개 카메라 연결 시작...");

        int successCount = 0;
        for (int i = 0; i < camerasToStart; i++)
        {
            cameraTargets[i].cameraName = cameras[i];
            bool success = false;
            yield return StartCoroutine(SetupCamera(i, (result) => success = result));
            if (success) successCount++;
            yield return new WaitForSeconds(1f); // 카메라 간 초기화 간격
        }

        UpdateStatusText($"카메라 연결 완료: {successCount}/{camerasToStart}개 성공");
    }

    IEnumerator SetupCamera(int cameraIndex, System.Action<bool> onComplete = null)
    {
        CameraRenderTarget target = cameraTargets[cameraIndex];
        string cameraName = target.cameraName;

        Debug.Log($"[MultiCamera] 카메라 {cameraIndex} ({cameraName}) 설정 시작");
        UpdateStatusText($"카메라 {cameraIndex + 1} 설정 중...");

        // 권한 요청 및 확인
        yield return StartCoroutine(RequestPermissionAndCheck(cameraName, cameraIndex));

        if (!HasCameraPermission(cameraName))
        {
            Debug.LogError($"[MultiCamera] 카메라 {cameraIndex} ({cameraName}) 권한 획득 실패");
            UpdateStatusText($"카메라 {cameraIndex + 1} 권한 실패");
            onComplete?.Invoke(false);
            yield break;
        }

        // 카메라 시작
        bool success = false;
        yield return StartCoroutine(RunCamera(cameraIndex, (result) => success = result));

        onComplete?.Invoke(success);
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

    IEnumerator RunCamera(int cameraIndex, System.Action<bool> onComplete = null)
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
            // Java 플러그인에는 Stop 메서드가 없고, Close가 closeCamera로 구현되어 있음
            // 하지만 Unity에서는 Close를 직접 호출할 수 없으므로 건너뜀
            Debug.Log($"[MultiCamera] 카메라 {cameraIndex} 기존 연결 정리 시도");
        }
        catch (Exception closeEx)
        {
            Debug.Log($"[MultiCamera] 카메라 {cameraIndex} Close 시도 (무시됨): {closeEx.Message}");
        }

        yield return new WaitForSeconds(1f);

        // 카메라 열기 (재시도 로직 추가)
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
                Debug.Log($"[MultiCamera] 카메라 {cameraIndex} {openRetries + 1}초 후 재시도...");
                yield return new WaitForSeconds(openRetries); // 점진적 대기 시간 증가
            }
        }

        if (!openSuccess || infos == null)
        {
            Debug.LogError($"[MultiCamera] 카메라 {cameraIndex} Open 실패");
            UpdateStatusText($"카메라 {cameraIndex + 1} 열기 실패");
            onComplete?.Invoke(false);
            yield break;
        }

        Debug.Log($"[MultiCamera] 카메라 {cameraIndex} Open 성공! 사용 가능한 포맷 수: {infos.Length}");

        // MJPEG 포맷 찾기
        int goodIndex = FindBestMJPEGFormat(infos, cameraIndex);
        if (goodIndex < 0)
        {
            Debug.LogError($"[MultiCamera] 카메라 {cameraIndex} MJPEG 포맷을 찾을 수 없습니다.");
            UpdateStatusText($"카메라 {cameraIndex + 1} 포맷 없음");
            onComplete?.Invoke(false);
            yield break;
        }

        // 카메라 설정 파싱 및 시작
        bool streamSuccess = false;
        yield return StartCoroutine(StartCameraStream(cameraIndex, infos, goodIndex, (result) => streamSuccess = result));

        if (streamSuccess)
        {
            UpdateStatusText($"카메라 {cameraIndex + 1} 연결 성공");
        }

        onComplete?.Invoke(streamSuccess);
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

    IEnumerator StartCameraStream(int cameraIndex, string[] infos, int formatIndex, System.Action<bool> onComplete = null)
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
            onComplete?.Invoke(false);
            yield break;
        }

        // 기존 텍스처가 있다면 삭제
        if (target.cameraTexture != null)
        {
            Destroy(target.cameraTexture);
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
                onComplete?.Invoke(false);
                yield break;
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[MultiCamera] 카메라 {cameraIndex} 텍스처 생성 실패: {e.Message}");
            onComplete?.Invoke(false);
            yield break;
        }

        // 프레임 렌더링 코루틴 시작
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

                try
                {
                    if (plugin != null && !string.IsNullOrEmpty(target.cameraName))
                    {
                        // Java 플러그인의 실제 메서드명에 맞춰서 호출
                        // Stop 메서드는 없고, Close는 closeCamera로 구현됨
                        plugin.Call("Close", target.cameraName); // 이 부분은 에러가 날 수 있지만 시도
                        Debug.Log($"[MultiCamera] 카메라 {i} ({target.cameraName}) 중지 시도 완료");
                    }
                }
                catch (Exception e)
                {
                    // 메서드가 없어서 에러가 나는 것은 정상 - 무시
                    Debug.Log($"[MultiCamera] 카메라 {i} 정리 시도 (메서드 없음): {e.Message}");
                }

                // 텍스처 정리
                if (target.cameraTexture != null)
                {
                    if (target.targetRenderer != null && target.targetRenderer.material != null)
                    {
                        target.targetRenderer.material.mainTexture = null;
                    }
                    Destroy(target.cameraTexture);
                    target.cameraTexture = null;
                }

                // 카메라 이름도 초기화 (중요!)
                target.cameraName = "";
                target.width = 640;
                target.height = 480;
                target.fps = 30;
            }
        }

        UpdateStatusText("모든 카메라 중지 완료");
    }

    void OnDestroy()
    {
        StopAllCameras();
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
        if (!isReconnecting)
        {
            StartCoroutine(ReconnectCameras());
        }
    }

    // 런타임에서 카메라 상태 확인
    public void GetCameraStatus()
    {
        string statusMessage = "=== 카메라 상태 ===\n";
        for (int i = 0; i < cameraTargets.Length; i++)
        {
            CameraRenderTarget target = cameraTargets[i];
            statusMessage += $"카메라 {i}: {target.cameraName}, 활성: {target.isActive}, 해상도: {target.width}x{target.height}\n";
        }
        statusMessage += "==================";

        UpdateStatusText("카메라 상태 확인됨");
        Debug.Log(statusMessage);
    }

    // 개별 카메라 재연결
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
        UpdateStatusText($"카메라 {cameraIndex + 1} 재연결 중...");

        // 해당 카메라만 중지
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
            Debug.LogError($"[MultiCamera] 카메라 {cameraIndex} 정리 실패: {e.Message}");
        }

        yield return new WaitForSeconds(1f);

        // 사용 가능한 카메라 다시 검색
        string[] cameras = null;
        bool getDevicesSuccess = false;

        try
        {
            cameras = plugin.Call<string[]>("GetUSBDevices");
            getDevicesSuccess = true;
        }
        catch (Exception e)
        {
            UpdateStatusText($"카메라 {cameraIndex + 1} 재연결 에러: {e.Message}");
            getDevicesSuccess = false;
        }

        if (getDevicesSuccess && cameras != null && cameras.Length > cameraIndex)
        {
            target.cameraName = cameras[cameraIndex];
            bool success = false;
            yield return StartCoroutine(SetupCamera(cameraIndex, (result) => success = result));

            if (success)
            {
                UpdateStatusText($"카메라 {cameraIndex + 1} 재연결 성공");
            }
            else
            {
                UpdateStatusText($"카메라 {cameraIndex + 1} 재연결 실패");
            }
        }
        else if (getDevicesSuccess)
        {
            UpdateStatusText($"카메라 {cameraIndex + 1} 찾을 수 없음");
        }
    }
}