using Unity.Netcode.Components;

/// <summary>
/// NetworkAnimator 는 기본이 '서버 권한'이라, 애니메이션을 서버가 정해야 합니다.
/// 하지만 우리 플레이어는 이동도 애니메이션도 본인(Owner)이 정하므로
/// 권한을 Owner 로 바꿔줍니다. NGO 에서 권장하는 방식입니다.
/// </summary>
public class OwnerNetworkAnimator : NetworkAnimator
{
    protected override bool OnIsServerAuthoritative() => false;
}
