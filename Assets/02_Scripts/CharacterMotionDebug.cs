using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Numpad 0 toggles a debug menu; while it is open Numpad 1-9 plays the matching
/// Animator state. The top-row digits work too, for keyboards without a numpad.
/// </summary>
[RequireComponent(typeof(Animator))]
public class CharacterMotionDebug : MonoBehaviour
{
    [Tooltip("Animator state names, in Numpad 1-9 order.")]
    public string[] motions =
    {
        "Idle", "Walk", "Jogging", "Crawl", "Hi", "Push", "Scratching"
    };

    [Tooltip("Blend time between motions, in seconds.")]
    public float crossFade = 0.15f;

    [Tooltip("Show the menu as soon as the scene starts.")]
    public bool openOnStart;

    Animator _animator;
    PlayerMovement _movement;
    bool _menuOpen;
    int _current = -1;

    void Awake()
    {
        _animator = GetComponent<Animator>();
        _movement = GetComponent<PlayerMovement>();
        _menuOpen = openOnStart;
        if (motions.Length > 0) _current = 0;
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null) return;

        if (kb.numpad0Key.wasPressedThisFrame)
            _menuOpen = !_menuOpen;

        if (!_menuOpen) return;

        for (int i = 0; i < 9; i++)
        {
            if (Pressed(kb, i)) Play(i);
        }
    }

    static bool Pressed(Keyboard kb, int index)
    {
        // 넘패드만 받습니다. 상단 숫자열 1~4 는 도구 전환(PlayerFarming)이 씁니다.
        switch (index)
        {
            case 0: return kb.numpad1Key.wasPressedThisFrame;
            case 1: return kb.numpad2Key.wasPressedThisFrame;
            case 2: return kb.numpad3Key.wasPressedThisFrame;
            case 3: return kb.numpad4Key.wasPressedThisFrame;
            case 4: return kb.numpad5Key.wasPressedThisFrame;
            case 5: return kb.numpad6Key.wasPressedThisFrame;
            case 6: return kb.numpad7Key.wasPressedThisFrame;
            case 7: return kb.numpad8Key.wasPressedThisFrame;
            case 8: return kb.numpad9Key.wasPressedThisFrame;
        }
        return false;
    }

    public void Play(int index)
    {
        if (index < 0 || index >= motions.Length) return;
        string state = motions[index];
        if (string.IsNullOrEmpty(state)) return;

        // stop locomotion from overwriting this on the next frame; any WASD input resumes it
        if (_movement != null) _movement.SuspendLocomotion();

        _animator.CrossFade(state, crossFade, 0, 0f);
        _current = index;
    }

    void OnGUI()
    {
        if (!_menuOpen) return;

        const float w = 260f;
        float h = 54f + motions.Length * 22f;
        GUILayout.BeginArea(new Rect(12f, 12f, w, h), GUI.skin.box);
        GUILayout.Label("<b>DEBUG MOTION</b>  (Numpad 0 to close)",
                        new GUIStyle(GUI.skin.label) { richText = true });
        GUILayout.Label("WASD move / Shift jog / Ctrl crawl (toggle)",
                        new GUIStyle(GUI.skin.label) { fontSize = 10 });

        for (int i = 0; i < motions.Length && i < 9; i++)
        {
            bool on = i == _current;
            string line = (on ? "> " : "   ") + "[" + (i + 1) + "] " + motions[i];
            if (GUILayout.Button(line, new GUIStyle(GUI.skin.label)
                {
                    fontStyle = on ? FontStyle.Bold : FontStyle.Normal
                }))
            {
                Play(i);
            }
        }

        GUILayout.EndArea();
    }
}
