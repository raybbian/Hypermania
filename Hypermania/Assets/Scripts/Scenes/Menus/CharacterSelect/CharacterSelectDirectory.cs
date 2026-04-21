using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Design.Configs;
using Game;
using Game.Sim;
using Netcode.P2P;
using Scenes.Menus.InputSelect;
using Scenes.Menus.MainMenu;
using Scenes.Online;
using Scenes.Session;
using Steamworks;
using UnityEngine;
using UnityEngine.InputSystem;
using Utils.EnumArray;

namespace Scenes.Menus.CharacterSelect
{
    /// <summary>
    /// Top-level controller for the CharacterSelect scene. Orchestrates local
    /// input → state mutation → (online) Steam broadcast → commit into
    /// <see cref="SessionDirectory.Options"/> → scene transition.
    /// </summary>
    [DisallowMultipleComponent]
    public class CharacterSelectDirectory : MonoBehaviour
    {
        [SerializeField]
        private GlobalConfig _globalConfig;

        [SerializeField]
        private ControlsConfig[] _controlsPresets;

        [SerializeField]
        [Tooltip(
            "Skin used for the \"Random\" grid tile, the Random preview on a player panel, and any ready marker tinting when a slot is on Random. When Portrait is null, no Random tile is shown."
        )]
        private SkinConfig _randomSkin;

        /// <summary>
        /// Character roster derived from <see cref="_globalConfig"/> at OnEnable.
        /// Order is determined by the sorted <see cref="Character"/> enum values
        /// (via <see cref="EnumIndexCache{T}"/>), so host and joiner see the
        /// same index ordering for online sync.
        /// </summary>
        private CharacterConfig[] _roster;

        [Header("Child views")]
        [SerializeField]
        private PlayerPanel[] _playerPanels = new PlayerPanel[2];

        [SerializeField]
        private CharacterCursor[] _playerCursors = new CharacterCursor[2];

        [SerializeField]
        private CharacterGrid _grid;

        [Header("Shown when both players are ready and a confirm press will start the match.")]
        [SerializeField]
        private FadeToggle _pressStartPrompt;

        private CharacterSelectState _state;
        private readonly LocalSelectionController[] _localControllers = new LocalSelectionController[2];
        private RemoteSelectionController _remoteController;
        private CharacterSelectNetSync _netSync;

        private readonly InputDevice[] _slotDevices = new InputDevice[2];
        private InputDevice _onlineLocalDevice;
        private int _onlineLocalPlayerIndex = -1;
        private bool _isOnline;
        private bool _committed;
        private bool _launchDispatched;
        private bool _exiting;
        private bool _singleDeviceMode;
        private int _activeLocalSlot = -1;

        private SteamMatchmakingClient _matchmakingSubscription;

        private void OnEnable()
        {
            _committed = false;
            _launchDispatched = false;
            _exiting = false;
            _state = new CharacterSelectState();
            _isOnline = SessionDirectory.Config == GameConfig.Online;
            _roster = BuildRoster(_globalConfig);

            if (_grid != null)
                _grid.Initialize(_roster, _randomSkin);

            if (_isOnline)
            {
                if (!TrySetupOnline())
                {
                    Debug.LogError("[CharacterSelect] Online setup failed.");
                    return;
                }
            }
            else
            {
                SetupLocal();
            }

            BindViews();
        }

        private void OnDisable()
        {
            if (_matchmakingSubscription != null)
            {
                _matchmakingSubscription.OnBackRequested -= OnRemoteBackRequested;
                _matchmakingSubscription.OnCharacterSelectLaunchRequested -= OnRemoteLaunchRequested;
                _matchmakingSubscription.OnCharacterSelectLaunch -= OnLaunchBroadcast;
                _matchmakingSubscription.OnPeerLeft -= OnRemotePeerLeft;
                _matchmakingSubscription = null;
            }
            _remoteController?.Dispose();
            _remoteController = null;
            _netSync?.Dispose();
            _netSync = null;
            for (int i = 0; i < _localControllers.Length; i++)
            {
                _localControllers[i] = null;
            }
        }

        private void SetupLocal()
        {
            _slotDevices[0] = FindDevice(DeviceAssignment.Player1);
            _slotDevices[1] = FindDevice(DeviceAssignment.Player2);

            for (int i = 0; i < 2; i++)
            {
                if (_slotDevices[i] == null)
                    continue;
                _localControllers[i] = new LocalSelectionController(_slotDevices[i]);
            }

            bool hasP1 = _slotDevices[0] != null;
            bool hasP2 = _slotDevices[1] != null;
            _singleDeviceMode = hasP1 ^ hasP2;
            _activeLocalSlot = hasP1 ? 0 : (hasP2 ? 1 : -1);

            TryAutoConfirmAbsentRandom();
        }

        /// <summary>
        /// Convenience for single-device mode: if the grid exposes a Random
        /// tile, drop the absent slot straight into <see cref="SelectPhase.Confirmed"/>
        /// pointing at Random so the active player can confirm three times and
        /// land in a match against a stationary dummy. The player can still
        /// press Back to un-ready the absent slot and customize it — once the
        /// active slot is Confirmed, <see cref="ResolveTargetSlot"/> routes
        /// edges to the absent slot for full navigation.
        /// </summary>
        private void TryAutoConfirmAbsentRandom()
        {
            if (!_singleDeviceMode || _activeLocalSlot < 0 || _grid == null || !_grid.HasRandomSlot)
                return;
            int absent = 1 - _activeLocalSlot;
            PlayerSelectionState absentSlot = _state.Players[absent];
            absentSlot.CharacterIndex = _roster != null ? _roster.Length : 0;
            absentSlot.SkinIndex = 0;
            absentSlot.Phase = SelectPhase.Confirmed;
        }

        /// <summary>
        /// Rolls a random non-colliding (CharacterIndex, SkinIndex) onto the
        /// given slot. Used both by the Random grid tile at confirm time and
        /// by single-device mode to pick the absent slot's auto-confirmed
        /// character at scene load.
        /// </summary>
        private void ResolveRandom(PlayerSelectionState slot)
        {
            if (_roster == null || _roster.Length == 0)
                return;
            PlayerSelectionState other = _state.Players[0] == slot ? _state.Players[1] : _state.Players[0];
            int otherChar = other.CharacterIndex;
            int otherSkin = other.SkinIndex;

            // Build the list of valid (char, skin) pairs that don't collide.
            List<(int, int)> candidates = new List<(int, int)>(_roster.Length * 2);
            for (int c = 0; c < _roster.Length; c++)
            {
                CharacterConfig cfg = _roster[c];
                int skinCount = cfg != null && cfg.Skins != null ? cfg.Skins.Length : 0;
                for (int s = 0; s < skinCount; s++)
                {
                    if (c == otherChar && s == otherSkin)
                        continue;
                    candidates.Add((c, s));
                }
            }
            if (candidates.Count == 0)
            {
                slot.CharacterIndex = 0;
                slot.SkinIndex = 0;
                return;
            }
            (int rc, int rs) = candidates[UnityEngine.Random.Range(0, candidates.Count)];
            slot.CharacterIndex = rc;
            slot.SkinIndex = rs;
        }

        /// <summary>
        /// Resolves any slot whose CharacterIndex points to the Random tile
        /// (index == <c>_roster.Length</c>) into a concrete non-colliding
        /// (CharacterIndex, SkinIndex) pair. Called at commit time so that
        /// "Random" stays as a live option throughout selection. Slot 0 is
        /// resolved first; if slot 1 was also random, its roll sees slot 0's
        /// concrete choice and avoids it.
        /// </summary>
        private void ResolveRandomSlotsForCommit()
        {
            for (int i = 0; i < 2; i++)
            {
                PlayerSelectionState slot = _state.Players[i];
                if (IsRandomSlot(slot))
                    ResolveRandom(slot);
            }
        }

        private bool IsRandomSlot(PlayerSelectionState slot)
        {
            return _roster != null && slot.CharacterIndex == _roster.Length;
        }

        private bool TrySetupOnline()
        {
            if (OnlineBaseDirectory.Matchmaking == null)
            {
                Debug.LogError("[CharacterSelect] Online mode but OnlineBaseDirectory.Matchmaking is null.");
                return false;
            }

            IReadOnlyList<CSteamID> players = OnlineDirectory.Players;
            if (players == null || players.Count < 2)
            {
                Debug.LogError("[CharacterSelect] Online mode but fewer than 2 players in OnlineDirectory.Players.");
                return false;
            }

            CSteamID localId = SteamUser.GetSteamID();
            _onlineLocalPlayerIndex = -1;
            for (int i = 0; i < players.Count; i++)
            {
                if (players[i] == localId)
                {
                    _onlineLocalPlayerIndex = i;
                    break;
                }
            }
            if (_onlineLocalPlayerIndex < 0)
            {
                Debug.LogError("[CharacterSelect] Local Steam ID not found in OnlineDirectory.Players.");
                return false;
            }

            _onlineLocalDevice = FindDevice(DeviceAssignment.Player1);
            if (_onlineLocalDevice == null)
            {
                Debug.LogError("[CharacterSelect] No local input device assigned for online play.");
                return false;
            }

            _localControllers[_onlineLocalPlayerIndex] = new LocalSelectionController(_onlineLocalDevice);

            _netSync = new CharacterSelectNetSync(OnlineBaseDirectory.Matchmaking);
            int remoteIndex = 1 - _onlineLocalPlayerIndex;
            CSteamID remoteId = players[remoteIndex];
            _remoteController = new RemoteSelectionController(_netSync, remoteId, _state.Players[remoteIndex]);
            _remoteController.OnProtocolError += OnRemoteProtocolError;

            _matchmakingSubscription = OnlineBaseDirectory.Matchmaking;
            _matchmakingSubscription.OnBackRequested += OnRemoteBackRequested;
            _matchmakingSubscription.OnCharacterSelectLaunchRequested += OnRemoteLaunchRequested;
            _matchmakingSubscription.OnCharacterSelectLaunch += OnLaunchBroadcast;
            _matchmakingSubscription.OnPeerLeft += OnRemotePeerLeft;
            return true;
        }

        /// <summary>
        /// Remote peer vanished from the Steam lobby without sending a Back
        /// message — most commonly because they quit the process outright.
        /// Bail to the Online lobby locally; we can't round-trip a message
        /// to someone who's already gone, so unlike <see cref="Back"/> we
        /// skip the SendBackRequest step. OnlineBase stays loaded so the
        /// local Steam lobby survives and the player lands back on the
        /// Online screen still in their lobby.
        /// </summary>
        private void OnRemotePeerLeft(CSteamID departed)
        {
            if (_committed || _exiting)
                return;
            if (departed == SteamUser.GetSteamID())
                return;
            _exiting = true;
            ExitToPreviousScene();
        }

        /// <summary>
        /// Remote peer sent a payload we couldn't parse. The lobby version
        /// gate should prevent this in practice; if we see it anyway, abort
        /// the session rather than proceed with stale remote state.
        /// </summary>
        private void OnRemoteProtocolError()
        {
            if (!_isOnline || _committed || _exiting)
                return;
            Debug.LogError("[CharacterSelect] Remote protocol error — returning to Online lobby.");
            Back();
        }

        private void BindViews()
        {
            for (int i = 0; i < _playerPanels.Length; i++)
            {
                PlayerPanel panel = _playerPanels[i];
                if (panel == null)
                    continue;
                bool isLocal = _isOnline ? (i == _onlineLocalPlayerIndex) : true;
                panel.Bind(_state.Players[i], _state.Players[1 - i], _roster, _controlsPresets, _randomSkin, isLocal);
            }

            for (int i = 0; i < _playerCursors.Length; i++)
            {
                CharacterCursor cursor = _playerCursors[i];
                if (cursor == null)
                    continue;
                cursor.Bind(_state.Players[i], _grid);
            }
        }

        private void Update()
        {
            if (_state == null)
                return;

            bool bothReadyBefore = _state.BothConfirmed;
            bool anyConfirmEdge = TickLocalControllers();

            if (_isOnline && _netSync != null && _onlineLocalPlayerIndex >= 0)
            {
                string payload = _state.Players[_onlineLocalPlayerIndex].ToPayload().Serialize();
                _netSync.Broadcast(payload);
            }

            bool bothReady = _state.BothConfirmed;
            if (_pressStartPrompt != null)
            {
                _pressStartPrompt.SetVisible(bothReady && !_committed);
            }

            if (!_committed && bothReady && bothReadyBefore && anyConfirmEdge)
            {
                if (_isOnline)
                {
                    TryDispatchOnlineLaunch();
                }
                else
                {
                    _committed = true;
                    Commit();
                }
            }
        }

        /// <summary>
        /// Online launch trigger. Any player whose view shows both-ready +
        /// local confirm press routes through the host: the host is the
        /// single authority that rolls any Random slots and broadcasts
        /// the final resolved selections, so both clients commit from an
        /// identical snapshot and cannot desync. <see cref="_launchDispatched"/>
        /// guards against re-sending while waiting for the echo.
        /// </summary>
        private void TryDispatchOnlineLaunch()
        {
            if (_launchDispatched || _matchmakingSubscription == null)
                return;

            CSteamID lobby = _matchmakingSubscription.CurrentLobby;
            if (!lobby.IsValid())
                return;

            _launchDispatched = true;

            bool isHost = SteamMatchmaking.GetLobbyOwner(lobby) == SteamUser.GetSteamID();
            if (isHost)
            {
                string[] args = BuildResolvedLaunchArgs();
                _matchmakingSubscription.SendCharacterSelectLaunch(args);
            }
            else
            {
                _matchmakingSubscription.SendCharacterSelectLaunchRequest();
            }
        }

        /// <summary>
        /// Host-only: a non-host asked us to launch. Re-validate against
        /// our own view at receipt time; if still both-confirmed and not
        /// yet committed, roll any Random slots and broadcast the
        /// authoritative launch with the resolved selections.
        /// </summary>
        private void OnRemoteLaunchRequested()
        {
            if (!_isOnline || _committed || _matchmakingSubscription == null)
                return;
            CSteamID lobby = _matchmakingSubscription.CurrentLobby;
            if (!lobby.IsValid())
                return;
            bool isHost = SteamMatchmaking.GetLobbyOwner(lobby) == SteamUser.GetSteamID();
            if (!isHost)
                return;
            if (_state == null || !_state.BothConfirmed)
                return;

            string[] args = BuildResolvedLaunchArgs();
            _matchmakingSubscription.SendCharacterSelectLaunch(args);
        }

        /// <summary>
        /// Host broadcast the authoritative launch. Apply the resolved
        /// selections to local state (overwriting any Random slot the
        /// non-host rolled locally) and commit — both clients hit this in
        /// response to the same inbound message, so the sim starts from a
        /// byte-identical <see cref="GameOptions"/> snapshot.
        /// </summary>
        private void OnLaunchBroadcast(string[] args)
        {
            if (!_isOnline || _committed)
                return;
            if (!TryApplyLaunchArgs(args))
            {
                Debug.LogError(
                    $"[CharacterSelect] Malformed CsLaunch args ({args?.Length ?? 0}): [{string.Join(",", args ?? System.Array.Empty<string>())}] — treating as protocol error."
                );
                OnRemoteProtocolError();
                return;
            }
            _committed = true;
            Commit();
        }

        /// <summary>
        /// Host-side: resolve any Random slots once on the authoritative
        /// side and serialize the final per-slot selection into launch
        /// args. The host then applies these same args on chat echo, so
        /// the host and non-host commit from identical state. Fields
        /// carried: character index, skin index, combo mode, mania
        /// difficulty, beat-cancel window — the set that feeds into
        /// <see cref="PlayerOptions"/> at commit time.
        /// </summary>
        private string[] BuildResolvedLaunchArgs()
        {
            ResolveRandomSlotsForCommit();
            string[] args = new string[10];
            for (int i = 0; i < 2; i++)
            {
                PlayerSelectionState slot = _state.Players[i];
                int baseIdx = i * 5;
                args[baseIdx + 0] = slot.CharacterIndex.ToString(CultureInfo.InvariantCulture);
                args[baseIdx + 1] = slot.SkinIndex.ToString(CultureInfo.InvariantCulture);
                args[baseIdx + 2] = ((int)slot.ComboMode).ToString(CultureInfo.InvariantCulture);
                args[baseIdx + 3] = ((int)slot.ManiaDifficulty).ToString(CultureInfo.InvariantCulture);
                args[baseIdx + 4] = ((int)slot.BeatCancelWindow).ToString(CultureInfo.InvariantCulture);
            }
            return args;
        }

        private bool TryApplyLaunchArgs(string[] args)
        {
            if (args == null || args.Length != 10)
                return false;
            int[] parsed = new int[10];
            for (int i = 0; i < 10; i++)
            {
                if (!int.TryParse(args[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed[i]))
                    return false;
            }
            for (int i = 0; i < 2; i++)
            {
                PlayerSelectionState slot = _state.Players[i];
                int baseIdx = i * 5;
                slot.CharacterIndex = parsed[baseIdx + 0];
                slot.SkinIndex = parsed[baseIdx + 1];
                slot.ComboMode = (ComboMode)parsed[baseIdx + 2];
                slot.ManiaDifficulty = (ManiaDifficulty)parsed[baseIdx + 3];
                slot.BeatCancelWindow = (BeatCancelWindow)parsed[baseIdx + 4];
            }
            return true;
        }

        /// <summary>
        /// Polls each local controller once and routes its edges to a target
        /// slot (see <see cref="ResolveTargetSlot"/>). In single-device mode,
        /// once the active player's slot is Confirmed the same device drives
        /// the absent slot through its normal phase flow. Returns true if any
        /// local controller registered a Confirm edge this frame (used by the
        /// both-ready start gate).
        /// </summary>
        private bool TickLocalControllers()
        {
            bool anyConfirm = false;
            for (int i = 0; i < _localControllers.Length; i++)
            {
                LocalSelectionController ctrl = _localControllers[i];
                if (ctrl == null)
                    continue;

                EdgeSet edges = ctrl.PollEdges();
                int targetSlot = ResolveTargetSlot(i);
                SelectPhase targetPhase = _state.Players[targetSlot].Phase;

                // Back from Character-phase either exits the scene or, in
                // single-device mode when we're driving the absent slot,
                // un-confirms the active slot so the player can edit their
                // own options again (next frame routes back to active).
                if (edges.Back && targetPhase == SelectPhase.Character)
                {
                    if (_singleDeviceMode && targetSlot != _activeLocalSlot)
                    {
                        _state.Players[_activeLocalSlot].Phase = SelectPhase.Options;
                    }
                    else
                    {
                        Back();
                        return anyConfirm;
                    }
                    continue;
                }

                if (edges.Confirm)
                    anyConfirm = true;

                ApplyEdgesToSlot(ctrl, targetSlot, edges);
            }
            return anyConfirm;
        }

        /// <summary>
        /// Single-device rule: if the active player has Confirmed, route edges
        /// to the absent slot; otherwise route to the active slot. In online
        /// or two-device mode, a controller always drives its own slot.
        /// </summary>
        private int ResolveTargetSlot(int controllerSlot)
        {
            if (!_singleDeviceMode || controllerSlot != _activeLocalSlot)
                return controllerSlot;
            return _state.Players[_activeLocalSlot].Phase == SelectPhase.Confirmed
                ? 1 - _activeLocalSlot
                : _activeLocalSlot;
        }

        private void ApplyEdgesToSlot(LocalSelectionController ctrl, int slotIndex, in EdgeSet edges)
        {
            PlayerSelectionState slot = _state.Players[slotIndex];
            PlayerSelectionState otherSlot = _state.Players[1 - slotIndex];
            CharacterConfig selected = GetSelectedCharacter(slot.CharacterIndex);
            int skinCount = selected != null && selected.Skins != null ? selected.Skins.Length : 0;
            bool isLocal = _isOnline ? (slotIndex == _onlineLocalPlayerIndex) : true;
            // Treat the other slot as not blocking anything while it's still
            // browsing the character grid — IsTaken short-circuits on
            // OtherCharacter < 0, so this propagates to FirstFreeSkin during
            // character nav and to CycleRow's skin-skip in Options.
            int otherCharacterIdx = otherSlot.Phase == SelectPhase.Character ? -1 : otherSlot.CharacterIndex;
            SelectionContext ctx = new SelectionContext(
                characterCount: _roster != null ? _roster.Length : 0,
                controlsPresetCount: _controlsPresets != null ? _controlsPresets.Length : 0,
                skinCount: skinCount,
                isRowInteractable: row =>
                    OptionsRows.IsInteractable(slot, isLocal, row, _roster != null ? _roster.Length : int.MaxValue),
                otherCharacter: otherCharacterIdx,
                otherSkin: otherSlot.SkinIndex,
                skinCountForChar: SkinCountForChar,
                hasRandomSlot: _grid != null && _grid.HasRandomSlot
            );
            ctrl.Apply(slot, ctx, edges);
        }

        private int SkinCountForChar(int charIdx)
        {
            if (_roster == null || charIdx < 0 || charIdx >= _roster.Length)
                return 0;
            CharacterConfig cfg = _roster[charIdx];
            return cfg != null && cfg.Skins != null ? cfg.Skins.Length : 0;
        }

        private CharacterConfig GetSelectedCharacter(int index)
        {
            if (_roster == null || _roster.Length == 0)
                return null;
            int clamped = Mathf.Clamp(index, 0, _roster.Length - 1);
            return _roster[clamped];
        }

        private static CharacterConfig[] BuildRoster(GlobalConfig config)
        {
            if (config == null)
                return System.Array.Empty<CharacterConfig>();
            List<CharacterConfig> list = new List<CharacterConfig>(EnumIndexCache<Character>.Count);
            for (int i = 0; i < EnumIndexCache<Character>.Count; i++)
            {
                Character key = EnumIndexCache<Character>.Keys[i];
                CharacterConfig entry = config.CharacterConfig(key);
                if (entry != null && entry.Enabled)
                    list.Add(entry);
            }
            return list.ToArray();
        }

        private static InputDevice FindDevice(DeviceAssignment assignment)
        {
            return SessionDirectory.RegisteredDevices.FirstOrDefault(kvp => kvp.Value == assignment).Key;
        }

        /// <summary>
        /// Public entry point for leaving CharacterSelect. Local/training:
        /// returns to InputSelect. Online: broadcasts a back-request so the
        /// remote peer also returns to the Online lobby screen, then
        /// transitions locally. Safe to call from a UI button or from code.
        /// </summary>
        public void Back()
        {
            if (_committed || _exiting)
                return;
            _exiting = true;

            if (_isOnline && OnlineBaseDirectory.Matchmaking != null)
            {
                OnlineBaseDirectory.Matchmaking.SendBackRequest();
            }

            ExitToPreviousScene();
        }

        /// <summary>
        /// Remote peer broadcast a back-request. Transition without
        /// re-broadcasting (avoids an echo loop — we'd receive our own
        /// SendBackRequest callback too).
        /// </summary>
        private void OnRemoteBackRequested()
        {
            if (_committed || _exiting)
                return;
            _exiting = true;
            ExitToPreviousScene();
        }

        private void ExitToPreviousScene()
        {
            if (_isOnline)
            {
                SceneLoader
                    .Instance.LoadNewScene()
                    .Load(SceneID.Online, SceneDatabase.ONLINE)
                    .Unload(SceneID.CharacterSelect)
                    .WithOverlay()
                    .Execute();
            }
            else
            {
                // MenuBase stays loaded across the InputSelect → CharacterSelect
                // transition, so no need to reload it here.
                SceneLoader
                    .Instance.LoadNewScene()
                    .Load(SceneID.InputSelect, SceneDatabase.INPUT_SELECT)
                    .Unload(SceneID.CharacterSelect)
                    .WithOverlay()
                    .Execute();
            }
        }

        private void Commit()
        {
            ResolveRandomSlotsForCommit();
            GameOptions options = BuildGameOptions();
            SessionDirectory.Options = options;

            if (_isOnline)
            {
                // OnlineBase stays loaded through LiveConnection → Battle →
                // BattleEnd so the Steam lobby survives the match; that lets
                // Restart drop both players straight back into the same lobby.
                SceneLoader
                    .Instance.LoadNewScene()
                    .Load(SceneID.LiveConnection, SceneDatabase.LIVE_CONNECTION)
                    .Unload(SceneID.CharacterSelect)
                    .WithOverlay()
                    .Execute();
            }
            else
            {
                SceneLoader
                    .Instance.LoadNewScene()
                    .Load(SceneID.Battle, SceneDatabase.BATTLE)
                    .Unload(SceneID.CharacterSelect)
                    .Unload(SceneID.MenuBase)
                    .WithOverlay()
                    .Execute();
            }
        }

        private GameOptions BuildGameOptions()
        {
            GameOptions scaffold = SessionDirectory.Options ?? new GameOptions();
            bool training = SessionDirectory.Config == GameConfig.Training;

            GameOptions options = new GameOptions
            {
                Global = _globalConfig != null ? _globalConfig : scaffold.Global,
                InfoOptions =
                    scaffold.InfoOptions
                    ?? new InfoOptions
                    {
                        ShowFrameData = training,
                        ShowBoxes = training,
                        VerifyComboPrediction = false,
                    },
                Players = new PlayerOptions[2],
                AlwaysRhythmCancel = false,
            };

            for (int i = 0; i < 2; i++)
            {
                PlayerSelectionState slot = _state.Players[i];
                CharacterConfig character = GetSelectedCharacter(slot.CharacterIndex);
                options.Players[i] = new PlayerOptions
                {
                    HealOnActionable = training,
                    SuperMaxOnActionable = training,
                    BurstMaxOnActionable = training,
                    Immortal = false,
                    Character = character,
                    SkinIndex = ClampSkinIndex(character, slot.SkinIndex),
                    ComboMode = slot.ComboMode,
                    ManiaDifficulty = slot.ManiaDifficulty,
                    BeatCancelWindow = slot.BeatCancelWindow,
                };
            }

            options.LocalPlayers = BuildLocalPlayers();
            return options;
        }

        private LocalPlayerOptions[] BuildLocalPlayers()
        {
            if (_isOnline)
            {
                int idx = _onlineLocalPlayerIndex >= 0 ? _onlineLocalPlayerIndex : 0;
                return new[]
                {
                    new LocalPlayerOptions
                    {
                        InputDevice = _onlineLocalDevice,
                        Controls = SafeControls(_state.Players[idx].ControlsIndex),
                    },
                };
            }

            LocalPlayerOptions[] result = new LocalPlayerOptions[2];
            for (int i = 0; i < 2; i++)
            {
                result[i] = new LocalPlayerOptions
                {
                    InputDevice = _slotDevices[i],
                    Controls = SafeControls(_state.Players[i].ControlsIndex),
                };
            }
            return result;
        }

        private ControlsConfig SafeControls(int index)
        {
            if (_controlsPresets == null || _controlsPresets.Length == 0)
                return null;
            int clamped = Mathf.Clamp(index, 0, _controlsPresets.Length - 1);
            return _controlsPresets[clamped];
        }

        private static int ClampSkinIndex(CharacterConfig config, int skinIndex)
        {
            if (config == null || config.Skins == null || config.Skins.Length == 0)
                return 0;
            return Mathf.Clamp(skinIndex, 0, config.Skins.Length - 1);
        }
    }
}
