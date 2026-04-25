// PLATFORM — IStepProvider implementation backed by Unity's New Input System.
// See docs/design/stage2_game_manager_v1.md §2.2.2.
//
// Roles:
//   1. Poll the BJJInputActions asset each Update and assemble a
//      RawHardwareSnapshot.
//   2. Run LayerAOps.Assemble → LayerB(Defense)Ops.Transform per role.
//   3. Implement IStepProvider so FixedStepOps.Advance can pull a
//      (frame, intent, defense) tuple per fixed step plus a
//      Technique?/CounterTechnique? commit hook.
//
// AI is invoked here because the technique/counter commit decisions need to
// be ready at the moment FixedStepOps calls Sample() — main.ts §622 stashes
// them via pendingAi* and we follow the same pattern.

using BJJSimulator.Platform;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BJJSimulator.Platform
{
    public class BJJInputProvider : MonoBehaviour, IStepProvider
    {
        [SerializeField] private InputActionAsset actionsAsset;

        // Set by BJJGameManager from BJJSessionLifecycle.SelectedRole each
        // Update so role transitions take effect at the next fixed step.
        public PlayerRole CurrentRole { get; set; } = PlayerRole.Bottom;

        // ---------------------------------------------------------------------
        // Read-only state for HUD / debug
        // ---------------------------------------------------------------------

        public InputFrame?    LastFrame   { get; private set; }
        public Intent?        LastIntent  { get; private set; }
        public DefenseIntent? LastDefense { get; private set; }

        // ---------------------------------------------------------------------
        // Private state
        // ---------------------------------------------------------------------

        private LayerAState        _layerA   = LayerAState.Initial;
        private LayerBState        _bState   = LayerBState.Initial;
        private LayerBDefenseState _bDefState = LayerBDefenseState.Initial;
        private LayerDState        _dState   = LayerDState.Initial;
        private LayerDDefenseState _dDefState = LayerDDefenseState.Initial;

        // Stashed AI decisions for the current fixed step. Populated in Sample,
        // read in ResolveCommit / ResolveCounterCommit. Mirrors main.ts §170.
        private AIOutput? _pendingAiBottom;
        private AIOutput? _pendingAiTop;

        // Most recent hardware snapshot. Refreshed in PollHardware() each
        // Update (BJJGameManager calls this before FixedStepOps.Advance).
        private RawHardwareSnapshot _snapshot;

        // Keyboard activity tracking — ports the lastEventMs / anyKeyHeld
        // bookkeeping from src/prototype/web/src/input/keyboard.ts so the
        // assembler's noisy-gamepad arbitration (LayerAOps.KbRecentMs) has
        // a real signal to read.
        private KeyboardSnapshot _kbPrev;
        private long _kbLastEventMs = BJJConst.SentinelTimeMs;

        private struct KeyboardSnapshot
        {
            public ButtonBit Buttons;
            public bool LsUp, LsDown, LsLeft, LsRight;
            public bool RsUp, RsDown, RsLeft, RsRight;
            public bool LTrigger, RTrigger;

            public bool AnyHeld =>
                Buttons != ButtonBit.None ||
                LsUp     || LsDown   || LsLeft   || LsRight   ||
                RsUp     || RsDown   || RsLeft   || RsRight   ||
                LTrigger || RTrigger;

            public bool Equals(KeyboardSnapshot o) =>
                Buttons  == o.Buttons  &&
                LsUp     == o.LsUp     && LsDown   == o.LsDown   &&
                LsLeft   == o.LsLeft   && LsRight  == o.LsRight  &&
                RsUp     == o.RsUp     && RsDown   == o.RsDown   &&
                RsLeft   == o.RsLeft   && RsRight  == o.RsRight  &&
                LTrigger == o.LTrigger && RTrigger == o.RTrigger;
        }

        // Cached InputAction references resolved from the asset.
        private InputAction _aLeftStick, _aRightStick;
        private InputAction _aLTrigger, _aRTrigger;
        private InputAction _aLBumper, _aRBumper;
        private InputAction _aBtnBase, _aBtnRelease, _aBtnBreath, _aBtnReserved, _aBtnPause;

        // ---------------------------------------------------------------------
        // Unity lifecycle
        // ---------------------------------------------------------------------

        void OnEnable()
        {
            if (actionsAsset == null)
            {
                Debug.LogError("BJJInputProvider: actionsAsset is not assigned. " +
                               "Drag BJJInputActions.inputactions into the Inspector slot.");
                return;
            }

            var map = actionsAsset.FindActionMap("Player", throwIfNotFound: false);
            if (map == null)
            {
                Debug.LogError("BJJInputProvider: 'Player' action map not found in asset.");
                return;
            }

            _aLeftStick   = map.FindAction("LeftStick",    throwIfNotFound: true);
            _aRightStick  = map.FindAction("RightStick",   throwIfNotFound: true);
            _aLTrigger    = map.FindAction("LeftTrigger",  throwIfNotFound: true);
            _aRTrigger    = map.FindAction("RightTrigger", throwIfNotFound: true);
            _aLBumper     = map.FindAction("LeftBumper",   throwIfNotFound: true);
            _aRBumper     = map.FindAction("RightBumper",  throwIfNotFound: true);
            _aBtnBase     = map.FindAction("BtnBase",      throwIfNotFound: true);
            _aBtnRelease  = map.FindAction("BtnRelease",   throwIfNotFound: true);
            _aBtnBreath   = map.FindAction("BtnBreath",    throwIfNotFound: true);
            _aBtnReserved = map.FindAction("BtnReserved",  throwIfNotFound: true);
            _aBtnPause    = map.FindAction("BtnPause",     throwIfNotFound: true);

            map.Enable();
        }

        void OnDisable()
        {
            actionsAsset?.FindActionMap("Player", throwIfNotFound: false)?.Disable();
        }

        // ---------------------------------------------------------------------
        // Hardware polling — called by BJJGameManager once per Update before
        // FixedStepOps.Advance. We do not poll in Sample() because Sample may
        // run multiple times per Update (catch-up steps) and we want all those
        // sub-steps to share the same hardware snapshot.
        // ---------------------------------------------------------------------

        public void PollHardware(long nowMs)
        {
            if (_aLeftStick == null) return; // OnEnable failed; stay idle.

            // Detect a connected gamepad. Stage 1 keeps a separate boolean so
            // the assembler can fall back to keyboard. Unity exposes this via
            // Gamepad.current.
            var pad = Gamepad.current;
            bool padConnected = pad != null;

            // Read raw gamepad values directly (not via InputAction) so we can
            // tell apart "pad activity" from "key activity" — InputAction merges
            // both bindings into one Vector2, losing the device-of-origin.
            Vec2  padLs = padConnected ? V2(pad.leftStick.ReadValue())  : Vec2.Zero;
            Vec2  padRs = padConnected ? V2(pad.rightStick.ReadValue()) : Vec2.Zero;
            float padLT = padConnected ? pad.leftTrigger.ReadValue()    : 0f;
            float padRT = padConnected ? pad.rightTrigger.ReadValue()   : 0f;

            ButtonBit padBtns = ButtonBit.None;
            if (padConnected)
            {
                if (pad.leftShoulder.isPressed)   padBtns |= ButtonBit.LBumper;
                if (pad.rightShoulder.isPressed)  padBtns |= ButtonBit.RBumper;
                if (pad.buttonSouth.isPressed)    padBtns |= ButtonBit.BtnBase;
                if (pad.buttonEast.isPressed)     padBtns |= ButtonBit.BtnRelease;
                if (pad.buttonNorth.isPressed)    padBtns |= ButtonBit.BtnBreath;
                if (pad.buttonWest.isPressed)     padBtns |= ButtonBit.BtnReserved;
                if (pad.startButton.isPressed)    padBtns |= ButtonBit.BtnPause;
            }

            DeviceKind devKind = DeviceKind.Xbox;
            if (padConnected)
            {
                string id = pad.name?.ToLowerInvariant() ?? "";
                if (id.Contains("dualsense") || id.Contains("playstation") || id.Contains("054c"))
                    devKind = DeviceKind.DualSense;
            }

            // Keyboard: read directly from Keyboard.current so we know which
            // keys are physically down (independent of the gamepad merging).
            var kb = Keyboard.current;
            bool wDown = kb != null && kb.wKey.isPressed;
            bool sDown = kb != null && kb.sKey.isPressed;
            bool aDown = kb != null && kb.aKey.isPressed;
            bool dDown = kb != null && kb.dKey.isPressed;
            bool upDown    = kb != null && kb.upArrowKey.isPressed;
            bool downDown  = kb != null && kb.downArrowKey.isPressed;
            bool leftDown  = kb != null && kb.leftArrowKey.isPressed;
            bool rightDown = kb != null && kb.rightArrowKey.isPressed;

            ButtonBit kbBtns = ButtonBit.None;
            if (kb != null)
            {
                if (kb.rKey.isPressed)      kbBtns |= ButtonBit.LBumper;
                if (kb.uKey.isPressed)      kbBtns |= ButtonBit.RBumper;
                if (kb.spaceKey.isPressed)  kbBtns |= ButtonBit.BtnBase;
                if (kb.xKey.isPressed)      kbBtns |= ButtonBit.BtnRelease;
                if (kb.cKey.isPressed)      kbBtns |= ButtonBit.BtnBreath;
                if (kb.vKey.isPressed)      kbBtns |= ButtonBit.BtnReserved;
                if (kb.escapeKey.isPressed) kbBtns |= ButtonBit.BtnPause;
            }

            var kbNow = new KeyboardSnapshot
            {
                Buttons  = kbBtns,
                LsUp     = wDown, LsDown   = sDown,    LsLeft   = aDown,    LsRight  = dDown,
                RsUp     = upDown, RsDown  = downDown, RsLeft   = leftDown, RsRight  = rightDown,
                LTrigger = kb != null && kb.fKey.isPressed,
                RTrigger = kb != null && kb.jKey.isPressed,
            };
            if (!kbNow.Equals(_kbPrev))
            {
                _kbLastEventMs = nowMs;
                _kbPrev = kbNow;
            }

            _snapshot = new RawHardwareSnapshot
            {
                GamepadConnected = padConnected,
                GamepadLs        = padLs,
                GamepadRs        = padRs,
                GamepadLTrigger  = padLT,
                GamepadRTrigger  = padRT,
                GamepadButtons   = padBtns,
                DeviceKind       = devKind,
                LsUp = kbNow.LsUp, LsDown = kbNow.LsDown, LsLeft = kbNow.LsLeft, LsRight = kbNow.LsRight,
                RsUp = kbNow.RsUp, RsDown = kbNow.RsDown, RsLeft = kbNow.RsLeft, RsRight = kbNow.RsRight,
                KbLTrigger     = kbNow.LTrigger,
                KbRTrigger     = kbNow.RTrigger,
                KbButtons      = kbNow.Buttons,
                KbAnyHeld      = kbNow.AnyHeld,
                KbLastEventMs  = _kbLastEventMs,
            };
        }

        // ---------------------------------------------------------------------
        // EditMode test hook
        // ---------------------------------------------------------------------

        internal void SetSnapshotForTest(RawHardwareSnapshot snap) => _snapshot = snap;
        internal void ResetForTest()
        {
            _layerA    = LayerAState.Initial;
            _bState    = LayerBState.Initial;
            _bDefState = LayerBDefenseState.Initial;
            _dState    = LayerDState.Initial;
            _dDefState = LayerDDefenseState.Initial;
            _pendingAiBottom = null;
            _pendingAiTop    = null;
            _kbPrev          = default;
            _kbLastEventMs   = BJJConst.SentinelTimeMs;
        }

        // ---------------------------------------------------------------------
        // IStepProvider
        // ---------------------------------------------------------------------

        public (InputFrame Frame, Intent Intent, DefenseIntent? Defense) Sample(long nowMs)
        {
            var (frame, nextLayerA) = LayerAOps.Assemble(_layerA, _snapshot, nowMs);
            _layerA = nextLayerA;
            LastFrame = frame;

            // Per-role branching — same shape as main.ts §622-697.
            switch (CurrentRole)
            {
                case PlayerRole.Bottom:
                {
                    var (intent, nextB) = LayerBOps.Transform(frame, _bState);
                    _bState = nextB;
                    LastIntent = intent;

                    // Fetch fresh AI Top decision (so it's ready in ResolveCounterCommit).
                    var aiTop = OpponentAI.OpponentIntentFor(_currentGame, AIOutputRole.Top);
                    _pendingAiTop    = aiTop;
                    _pendingAiBottom = null;
                    var defense = aiTop.Role == AIOutputRole.Top ? aiTop.Defense : DefenseIntent.Zero;
                    LastDefense = defense;
                    return (frame, intent, defense);
                }
                case PlayerRole.Top:
                {
                    var (defense, nextBd) = LayerBDefenseOps.Transform(frame, _bDefState);
                    _bDefState = nextBd;
                    LastDefense = defense;

                    var aiBottom = OpponentAI.OpponentIntentFor(_currentGame, AIOutputRole.Bottom);
                    _pendingAiBottom = aiBottom;
                    _pendingAiTop    = null;
                    var intent = aiBottom.Role == AIOutputRole.Bottom ? aiBottom.Intent : Intent.Zero;
                    LastIntent = intent;
                    return (frame, intent, defense);
                }
                default: // Spectate
                {
                    var aiBottom = OpponentAI.OpponentIntentFor(_currentGame, AIOutputRole.Bottom);
                    var aiTop    = OpponentAI.OpponentIntentFor(_currentGame, AIOutputRole.Top);
                    _pendingAiBottom = aiBottom;
                    _pendingAiTop    = aiTop;
                    var intent  = aiBottom.Role == AIOutputRole.Bottom ? aiBottom.Intent  : Intent.Zero;
                    var defense = aiTop.Role    == AIOutputRole.Top    ? aiTop.Defense    : DefenseIntent.Zero;
                    LastIntent  = intent;
                    LastDefense = defense;
                    return (frame, intent, defense);
                }
            }
        }

        public Technique? ResolveCommit(InputFrame frame, Intent intent, GameState game, float dtMs)
        {
            if (CurrentRole != PlayerRole.Bottom)
            {
                // Top or Spectate → Bottom is AI; use the stash from Sample().
                return _pendingAiBottom?.Role == AIOutputRole.Bottom
                    ? _pendingAiBottom.Value.ConfirmedTechnique
                    : null;
            }

            bool windowIsOpen = game.JudgmentWindow.State == JudgmentWindowState.Open;
            var (next, confirmed) = LayerDOps.Resolve(_dState, new LayerDInputs
            {
                NowMs        = frame.Timestamp,
                DtMs         = dtMs,
                Frame        = frame,
                Hip          = intent.Hip,
                Candidates   = game.JudgmentWindow.Candidates,
                WindowIsOpen = windowIsOpen,
            });
            _dState = next;
            return confirmed;
        }

        public CounterTechnique? ResolveCounterCommit(InputFrame frame, GameState game, float dtMs)
        {
            if (CurrentRole != PlayerRole.Top)
            {
                return _pendingAiTop?.Role == AIOutputRole.Top
                    ? _pendingAiTop.Value.ConfirmedCounter
                    : null;
            }

            bool windowIsOpen = game.CounterWindow.State == CounterWindowState.Open;
            var (next, confirmed) = LayerDDefenseOps.Resolve(_dDefState, new LayerDDefenseInputs
            {
                NowMs                    = frame.Timestamp,
                DtMs                     = dtMs,
                Frame                    = frame,
                Candidates               = game.CounterWindow.Candidates,
                WindowIsOpen             = windowIsOpen,
                AttackerSweepLateralSign = game.AttackerSweepLateralSign,
            });
            _dDefState = next;
            return confirmed;
        }

        // ---------------------------------------------------------------------
        // GameState injection — BJJGameManager calls this before each Advance
        // so OpponentAI.OpponentIntentFor sees the right snapshot.
        // ---------------------------------------------------------------------

        private GameState _currentGame;
        public void SetCurrentGameState(GameState game) => _currentGame = game;

        // ---------------------------------------------------------------------

        private static Vec2 V2(Vector2 v) => new Vec2(v.x, v.y);
    }
}
