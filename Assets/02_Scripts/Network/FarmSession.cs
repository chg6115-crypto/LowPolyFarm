using System;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;

/// <summary>
/// 세션(방) 만들기 / 참가 / 나가기를 담당합니다.
///
/// 두 가지 경로를 지원합니다.
///  - 온라인: Unity Multiplayer Services 로 방을 만들고 참가 코드를 발급합니다.
///            Unity Cloud 프로젝트 연결이 필요합니다.
///  - 로컬  : 같은 PC에서 127.0.0.1 로 바로 붙습니다.
///            클라우드 설정 없이 2개 창으로 테스트할 때 씁니다.
/// </summary>
public class FarmSession : MonoBehaviour
{
    [Header("세션")]
    [Tooltip("호스트 포함 최대 인원. 기획 2~4인.")]
    [SerializeField] int maxPlayers = 4;

    [Header("로컬 테스트")]
    [SerializeField] string localAddress = "127.0.0.1";
    [SerializeField] ushort localPort = 7777;

    /// <summary>현재 참가 중인 온라인 세션. 로컬 모드면 null.</summary>
    public ISession Session { get; private set; }

    /// <summary>UI에 그대로 띄우는 상태 문자열.</summary>
    public string Status { get; private set; } = "준비됨";

    /// <summary>온라인 세션의 참가 코드. 없으면 빈 문자열.</summary>
    public string JoinCode => Session?.Code ?? string.Empty;

    public bool Busy { get; private set; }
    public bool Connected => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;

    bool _servicesReady;

    // ------------------------------------------------------------------
    // 온라인
    // ------------------------------------------------------------------

    /// <summary>
    /// UGS 초기화 + 익명 로그인. 클라우드 프로젝트가 연결돼 있지 않으면 여기서 실패합니다.
    /// </summary>
    async Task<bool> EnsureServicesAsync()
    {
        if (_servicesReady) return true;
        try
        {
            Status = "서비스 초기화 중...";
            if (UnityServices.State != ServicesInitializationState.Initialized)
                await UnityServices.InitializeAsync();

            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();

            _servicesReady = true;
            Status = "서비스 준비 완료";
            return true;
        }
        catch (Exception e)
        {
            // 가장 흔한 원인: Edit > Project Settings > Services 에서 Unity Cloud 프로젝트 미연결
            Status = "온라인 사용 불가 (Unity Cloud 프로젝트 연결 필요)\n" + e.Message;
            Debug.LogWarning("[FarmSession] " + Status);
            return false;
        }
    }

    /// <summary>방을 만들고 참가 코드를 발급받습니다.</summary>
    public async void HostOnline()
    {
        if (Busy) return;
        Busy = true;
        try
        {
            if (!await EnsureServicesAsync()) return;

            Status = "방 만드는 중...";
            var options = new SessionOptions { MaxPlayers = maxPlayers }.WithRelayNetwork();
            Session = await MultiplayerService.Instance.CreateSessionAsync(options);
            Status = $"방 생성됨. 참가 코드: {Session.Code}";
        }
        catch (Exception e)
        {
            Status = "방 생성 실패: " + e.Message;
            Debug.LogError("[FarmSession] " + e);
        }
        finally { Busy = false; }
    }

    /// <summary>참가 코드로 방에 들어갑니다.</summary>
    public async void JoinOnline(string code)
    {
        if (Busy || string.IsNullOrWhiteSpace(code)) return;
        Busy = true;
        try
        {
            if (!await EnsureServicesAsync()) return;

            Status = "참가 중...";
            Session = await MultiplayerService.Instance.JoinSessionByCodeAsync(code.Trim());
            Status = "참가 완료: " + Session.Code;
        }
        catch (Exception e)
        {
            Status = "참가 실패: " + e.Message;
            Debug.LogError("[FarmSession] " + e);
        }
        finally { Busy = false; }
    }

    // ------------------------------------------------------------------
    // 로컬 (클라우드 없이 테스트)
    // ------------------------------------------------------------------

    public void HostLocal()
    {
        if (!ConfigureLocalTransport()) return;
        Status = NetworkManager.Singleton.StartHost()
            ? $"로컬 호스트 시작 ({localAddress}:{localPort})"
            : "로컬 호스트 시작 실패";
    }

    public void JoinLocal()
    {
        if (!ConfigureLocalTransport()) return;
        Status = NetworkManager.Singleton.StartClient()
            ? $"로컬 클라이언트 접속 중 ({localAddress}:{localPort})"
            : "로컬 클라이언트 시작 실패";
    }

    bool ConfigureLocalTransport()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) { Status = "씬에 NetworkManager 가 없습니다."; return false; }

        var utp = nm.GetComponent<UnityTransport>();
        if (utp == null) { Status = "NetworkManager 에 UnityTransport 가 없습니다."; return false; }

        utp.SetConnectionData(localAddress, localPort);
        return true;
    }

    // ------------------------------------------------------------------

    public async void Leave()
    {
        try
        {
            if (Session != null)
            {
                await Session.LeaveAsync();
                Session = null;
            }
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
                NetworkManager.Singleton.Shutdown();

            Status = "나감";
        }
        catch (Exception e)
        {
            Status = "나가기 실패: " + e.Message;
        }
    }
}
