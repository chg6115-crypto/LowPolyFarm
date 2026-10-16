using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// WASD movement on a CharacterController.
/// Hold Shift to jog, hold Ctrl to crawl. Walking into a <see cref="Pushable"/>
/// plays the push animation and shoves it along.
/// Every locomotion clip is in-place, so the motion comes from here, not root motion.
/// </summary>
[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(Animator))]
public class PlayerMovement : MonoBehaviour
{
    [Header("Speed")]
    [Tooltip("Base walking speed in m/s. Jog and crawl are multiples of this.")]
    public float walkSpeed = 1.2f;
    [Tooltip("Shift: jog speed as a multiple of walk speed.")]
    public float runMultiplier = 1.5f;
    [Tooltip("Ctrl: crawl speed as a multiple of walk speed.")]
    public float crawlMultiplier = 0.75f;

    [Header("Crawling")]
    // Ctrl 은 '누르고 있기'가 아니라 '토글'입니다.
    // 홀드 방식이면 이동 중 Ctrl 이 계속 눌려 있어서 에디터 플레이 중
    // Ctrl+S(저장) / Ctrl+D(복제) / Ctrl+A(전체선택) 같은 단축키가 같이 발동합니다.
    // 토글로 두면 이동할 때는 Ctrl 을 떼고 있으므로 그런 조합이 생기지 않습니다.

    [Tooltip("Capsule height while crawling. The crawl pose is about 0.59m tall, " +
             "so this is what lets the character fit under a fence rail.")]
    public float crawlHeight = 0.56f;
    [Tooltip("What counts as an obstacle when checking whether there is room to stand up.")]
    public LayerMask standUpBlockMask = ~0;

    [Tooltip("How fast the character turns toward its movement direction, in degrees per second.")]
    public float turnSpeed = 720f;

    [Header("Gravity")]
    public float gravity = -9.81f;
    [Tooltip("Downward speed kept while grounded, so the controller stays glued to the floor.")]
    public float groundedStick = -2f;

    [Header("Direction")]
    [Tooltip("Move relative to the camera's facing instead of world axes.")]
    public bool cameraRelative = true;
    public Transform cameraTransform;
    [Tooltip("Extra yaw if the model does not face +Z. This rig already does, so 0.")]
    public float modelYawOffset;

    [Header("Pushing")]
    [Tooltip("How long the push animation keeps playing after the last contact.")]
    public float pushHoldTime = 0.2f;

    [Header("Jog kick-off")]
    [Tooltip("The jog clip has no built-in acceleration, so the first strides are played " +
             "faster to read as speeding up. 1 = no boost.")]
    public float jogStartSpeed = 1.6f;
    [Tooltip("How long the boost takes to ease back to normal speed.")]
    public float jogStartDuration = 0.45f;
    [Tooltip("Float parameter on the controller that scales the Jogging state's speed.")]
    public string jogSpeedParameter = "JogSpeed";

    [Header("Animator state names")]
    public string idleState = "Idle";
    public string walkState = "Walk";
    public string runState = "Jogging";
    public string crawlState = "Crawl";
    public string pushState = "Push";
    public float crossFade = 0.12f;

    public float RunSpeed => walkSpeed * runMultiplier;
    public float CrawlSpeed => walkSpeed * crawlMultiplier;

    CharacterController _cc;
    Animator _animator;
    float _verticalVelocity;
    float _pushTimer;
    float _jogKickTimer;
    float _currentSpeed;
    Vector3 _moveDir;
    string _playing;
    int _jogSpeedHash;
    float _standHeight;
    Vector3 _standCenter;
    float _footLocalY;
    bool _crawlToggled;

    /// <summary>
    /// Set to false by the debug menu while it plays a one-off motion.
    /// Any movement input turns it straight back on.
    /// </summary>
    [HideInInspector] public bool locomotionEnabled = true;

    void Awake()
    {
        _cc = GetComponent<CharacterController>();
        _animator = GetComponent<Animator>();
        _jogSpeedHash = Animator.StringToHash(jogSpeedParameter);

        // 인스펙터에 설정된 값을 '서 있을 때'의 캡슐로 기억해 둡니다.
        _standHeight = _cc.height;
        _standCenter = _cc.center;
        _footLocalY = _cc.center.y - _cc.height * 0.5f;   // 발바닥의 로컬 높이 (보통 음수)

        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;
    }

    void Update()
    {
        Vector2 input = ReadInput(out bool running, out bool crawling);

        // Ctrl 을 뗐어도 머리 위가 막혀 있으면 일어설 수 없으므로 계속 기어갑니다.
        if (!crawling && !CanStandUp()) crawling = true;
        ApplyCapsule(crawling);

        _moveDir = Vector3.zero;
        if (input.sqrMagnitude > 0.0001f)
        {
            locomotionEnabled = true;          // moving cancels a debug motion

            Vector3 fwd = Vector3.forward, right = Vector3.right;
            if (cameraRelative && cameraTransform != null)
            {
                fwd = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up);
                if (fwd.sqrMagnitude < 0.0001f) fwd = Vector3.forward;   // camera looking straight down
                fwd.Normalize();
                right = Vector3.Cross(Vector3.up, fwd);
            }

            _moveDir = (fwd * input.y + right * input.x).normalized;

            Quaternion target = Quaternion.LookRotation(_moveDir) * Quaternion.Euler(0f, modelYawOffset, 0f);
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation, target, turnSpeed * Time.deltaTime);
        }

        if (_cc.isGrounded && _verticalVelocity < 0f)
            _verticalVelocity = groundedStick;
        else
            _verticalVelocity += gravity * Time.deltaTime;

        // crawl wins over jog when both modifiers are held
        _currentSpeed = crawling ? CrawlSpeed : running ? RunSpeed : walkSpeed;

        if (_pushTimer > 0f) _pushTimer -= Time.deltaTime;

        Vector3 velocity = _moveDir * _currentSpeed;
        velocity.y = _verticalVelocity;
        _cc.Move(velocity * Time.deltaTime);     // may raise OnControllerColliderHit

        if (!locomotionEnabled) return;

        bool moving = _moveDir.sqrMagnitude > 0.0001f;
        if (!moving) Play(idleState);
        else if (_pushTimer > 0f) Play(pushState);
        else if (crawling) Play(crawlState);
        else if (running) Play(runState);
        else Play(walkState);

        UpdateJogKickOff();
    }

    /// <summary>
    /// The jog clip runs at a constant stride, so entering it looks like an instant
    /// top speed. Playing the first strides faster and easing back reads as accelerating.
    /// </summary>
    void UpdateJogKickOff()
    {
        if (string.IsNullOrEmpty(jogSpeedParameter)) return;

        if (_jogKickTimer > 0f)
            _jogKickTimer = Mathf.Max(0f, _jogKickTimer - Time.deltaTime);

        float speed = 1f;
        if (_playing == runState && jogStartDuration > 0f)
        {
            float k = _jogKickTimer / jogStartDuration;        // 1 -> 0
            speed = Mathf.Lerp(1f, jogStartSpeed, k * k);      // ease out
        }
        _animator.SetFloat(_jogSpeedHash, speed);
    }

    Vector2 ReadInput(out bool running, out bool crawling)
    {
        running = crawling = false;
        var kb = Keyboard.current;
        if (kb == null) return Vector2.zero;

        var v = new Vector2(
            (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f),
            (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f));

        running = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;

        // Ctrl 을 '누를 때마다' 기어가기 on/off 를 뒤집습니다 (홀드 아님).
        if (kb.leftCtrlKey.wasPressedThisFrame || kb.rightCtrlKey.wasPressedThisFrame)
            _crawlToggled = !_crawlToggled;
        crawling = _crawlToggled;

        return v;
    }

    /// <summary>
    /// 기는 동안 캡슐을 낮춥니다. 발바닥 위치는 그대로 두고 높이만 줄여야
    /// 바닥에 파묻히거나 뜨지 않습니다.
    /// </summary>
    void ApplyCapsule(bool crawling)
    {
        float h = crawling ? Mathf.Max(crawlHeight, _cc.radius * 2f) : _standHeight;
        if (Mathf.Approximately(_cc.height, h)) return;

        _cc.height = h;
        _cc.center = new Vector3(_standCenter.x, _footLocalY + h * 0.5f, _standCenter.z);
    }

    /// <summary>
    /// 서 있는 크기의 캡슐이 들어갈 공간이 있는지 검사합니다.
    /// 자기 자신의 콜라이더는 결과에서 걸러냅니다.
    /// </summary>
    bool CanStandUp()
    {
        if (Mathf.Approximately(_cc.height, _standHeight)) return true;   // 이미 서 있음

        float r = _cc.radius;
        Vector3 centre = transform.position + new Vector3(_standCenter.x, _footLocalY + _standHeight * 0.5f, _standCenter.z);
        Vector3 bottom = centre - Vector3.up * (_standHeight * 0.5f - r);
        Vector3 top = centre + Vector3.up * (_standHeight * 0.5f - r);

        var hits = Physics.OverlapCapsule(bottom, top, r - 0.01f, standUpBlockMask, QueryTriggerInteraction.Ignore);
        foreach (var h in hits)
            if (h != null && !h.transform.IsChildOf(transform)) return false;
        return true;
    }

    void OnControllerColliderHit(ControllerColliderHit hit)
    {
        if (_moveDir.sqrMagnitude < 0.0001f) return;

        var body = hit.collider.attachedRigidbody;
        if (body == null || body.isKinematic) return;

        var pushable = body.GetComponent<Pushable>();
        if (pushable == null) return;

        // only shove along the ground, and only when walking into the object
        Vector3 dir = new Vector3(hit.moveDirection.x, 0f, hit.moveDirection.z);
        if (dir.sqrMagnitude < 0.0001f) return;
        dir.Normalize();
        if (Vector3.Dot(dir, _moveDir) < 0.3f) return;

        Vector3 v = dir * (_currentSpeed * pushable.speedFactor);
        v.y = body.linearVelocity.y;              // let gravity keep doing its thing
        body.linearVelocity = v;

        _pushTimer = pushHoldTime;
    }

    void Play(string state)
    {
        if (_playing == state) return;
        if (state == runState) _jogKickTimer = jogStartDuration;   // restart the kick-off
        _animator.CrossFade(state, crossFade, 0, 0f);
        _playing = state;
    }

    /// <summary>Called by the debug menu so its motion is not overwritten next frame.</summary>
    public void SuspendLocomotion()
    {
        locomotionEnabled = false;
        _playing = null;
    }
}
