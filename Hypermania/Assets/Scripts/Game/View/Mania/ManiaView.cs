using System;
using System.Collections.Generic;
using Game.Sim;
using Game.View.Events;
using Game.View.Events.Vfx;
using Steamworks;
using Unity.VisualScripting;
using UnityEngine;
using Utils;

namespace Game.View.Mania
{
    [Serializable]
    public struct ManiaViewConfig
    {
        public float ScrollSpeed;
        public Transform[] Anchors;
        public GameObject[] Notes;

        public void Validate()
        {
            if (ScrollSpeed == 0f)
            {
                throw new InvalidOperationException("Scroll speed in ManiaViewConfig cannot be 0");
            }
            if (Anchors == null || Anchors.Length == 0)
            {
                throw new InvalidOperationException("Must set note anchors");
            }
            if (Notes == null || Notes.Length == 0)
            {
                throw new InvalidOperationException("Must set note prefabs");
            }
        }
    }

    public class ManiaView : MonoBehaviour
    {
        [SerializeField]
        public ManiaViewConfig Config;
        private Dictionary<int, GameObject> _activeNotes;
        private Frame _rollbackStart;

        public void Init()
        {
            _activeNotes = new Dictionary<int, GameObject>();
            gameObject.SetActive(false);
            _rollbackStart = Frame.NullFrame;
        }

        public void DeInit()
        {
            foreach (var obj in _activeNotes.Values)
            {
                Destroy(obj);
            }
            _activeNotes = null;
        }

        public void OnValidate()
        {
            Config.Validate();
        }
        public void RollbackRender(in GameState state, VfxManager vfx, SfxManager sfx, List<ManiaEvent> maniaEvents, FighterState fighterState)
        {
            // gather all sfx from states in the current rollback process
            if (_rollbackStart == Frame.NullFrame)
            {
                _rollbackStart = state.SimFrame;
            }
            DoViewEvents(state, vfx, sfx, maniaEvents, fighterState);
        }
        public void DoViewEvents(in GameState state, VfxManager vfx, SfxManager sfx, List<ManiaEvent> maniaEvents, FighterState fighterState)
        {
            for (int i = 0; i < maniaEvents.Count; i++) {
                // // TODO DELETE THESE
                 float x = Config.Anchors[2].localPosition.x;
                 float y = Config.Anchors[2].localPosition.y;
                if (maniaEvents[i].Kind == ManiaEventKind.Hit)
                {
                    
                    sfx.AddDesired(
                        new ViewEvent<SfxEvent>
                        {
                            Event = new SfxEvent { Kind = SfxKind.comboGood, },
                            StartFrame = state.RealFrame,
                            Hash = i,
                        }
                    );

                    vfx.AddDesired(
                        new ViewEvent<VfxEvent>
                        {
                            Event = new VfxEvent { Kind = VfxKind.NoteHit, Position = new Vector2((float) fighterState.Position.x, (float) fighterState.Position.y)},
                            StartFrame = state.RealFrame,
                            Hash = i,
                            
                        }
                    );
                }
                else if (maniaEvents[i].Kind == ManiaEventKind.Missed) {
                    // if (maniaEvents[i].Offset > 50) { // test value change later
                    //     sfx.AddDesired(
                    //     new ViewEvent<SfxEvent>
                    //     {
                    //         Event = new SfxEvent { Kind = SfxKind.comboOk },
                    //         StartFrame = state.RealFrame,
                    //         Hash = i,
                    //     }
                    // );
                    // } else {
                        sfx.AddDesired(
                        new ViewEvent<SfxEvent>
                        {
                            Event = new SfxEvent { Kind = SfxKind.comboMiss },
                            StartFrame = state.RealFrame,
                            Hash = i,
                        }
                    );
                    
                    vfx.AddDesired(
                        new ViewEvent<VfxEvent>
                        {
                            Event = new VfxEvent { Kind = VfxKind.NoteMiss, Position = new Vector3((float) fighterState.Position.x, (float) fighterState.Position.y, 0)},
                            StartFrame = state.RealFrame,
                            Hash = i,
                        }
                    );
                }
            }
            
        }
            

        public void Render(Frame frame, in ManiaState state)
        {
            gameObject.SetActive(state.EndFrame != Frame.NullFrame);

            Dictionary<int, GameObject> renderedNow = new Dictionary<int, GameObject>();

            for (int i = 0; i < state.Channels.Length; i++)
            {
                Config
                    .Anchors[i]
                    .gameObject.GetComponent<ManiaSpriteSwitcher>()
                    .ChangeSprite(state.Channels[i].Pressed);
            }

            for (int i = 0; i < state.Config.NumKeys; i++)
            {
                for (int j = 0; j < state.Channels[i].Notes.Count; j++)
                {
                    if (!RenderNote(frame, i, state.Channels[i].Notes[j], out var noteView))
                    {
                        break;
                    }
                    renderedNow[state.Channels[i].Notes[j].Id] = noteView;
                }
            }

            // for all notes not rendered now but in the active set, destroy
            foreach ((var id, var obj) in _activeNotes)
            {
                if (!renderedNow.ContainsKey(id))
                {
                    Destroy(obj);
                }
            }
            _activeNotes = renderedNow;
        }

        private bool RenderNote(Frame frame, int channel, in ManiaNote note, out GameObject noteView)
        {
            noteView = null;
            float x = Config.Anchors[channel].localPosition.x;
            float y = (note.Tick - frame) * Config.ScrollSpeed + Config.Anchors[channel].localPosition.y;
            // dont render nodes that are way too far outside
            if (y > GetComponent<RectTransform>().rect.height / 2)
            {
                return false;
            }

            if (!_activeNotes.ContainsKey(note.Id))
            {
                noteView = Instantiate(Config.Notes[channel], transform, false);
            }
            else
            {
                noteView = _activeNotes[note.Id];
            }

            noteView.transform.SetLocalPositionAndRotation(new Vector3(x, y, -1), Quaternion.identity);
            return true;
        }
    }
}
