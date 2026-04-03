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

        private Dictionary<int, ManiaNoteView> _activeNotes;

        public void Init()
        {
            _activeNotes = new Dictionary<int, ManiaNoteView>();
            gameObject.SetActive(false);
        }

        public void DeInit()
        {
            foreach (var obj in _activeNotes.Values)
            {
                Destroy(obj.gameObject);
            }
            _activeNotes = null;
        }

        public void OnValidate()
        {
            Config.Validate();
        }

        public void RollbackRender(Frame realFrame, in ManiaState maniaState, VfxManager vfx, SfxManager sfx)
        {
            Vector3 world = GetComponent<RectTransform>().position;
            for (int i = 0; i < maniaState.ManiaEvents.Count; i++)
            {
                if (maniaState.ManiaEvents[i].Kind == ManiaEventKind.Hit)
                {
                    sfx.AddDesired(
                        new ViewEvent<SfxEvent>
                        {
                            Event = new SfxEvent { Kind = SfxKind.ComboGood },
                            StartFrame = realFrame,
                            Hash = i,
                        }
                    );

                    vfx.AddDesired(
                        new ViewEvent<VfxEvent>
                        {
                            Event = new VfxEvent { Kind = VfxKind.NoteHit, Position = world },
                            StartFrame = realFrame,
                            Hash = i,
                        }
                    );
                }
                else if (maniaState.ManiaEvents[i].Kind == ManiaEventKind.Missed)
                {
                    sfx.AddDesired(
                        new ViewEvent<SfxEvent>
                        {
                            Event = new SfxEvent { Kind = SfxKind.ComboMiss },
                            StartFrame = realFrame,
                            Hash = i,
                        }
                    );

                    vfx.AddDesired(
                        new ViewEvent<VfxEvent>
                        {
                            Event = new VfxEvent { Kind = VfxKind.NoteMiss, Position = world },
                            StartFrame = realFrame,
                            Hash = i,
                        }
                    );
                }
            }
        }

        public void Render(Frame frame, in ManiaState state)
        {
            gameObject.SetActive(state.EndFrame != Frame.NullFrame);

            Dictionary<int, ManiaNoteView> renderedNow = new Dictionary<int, ManiaNoteView>();

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
                    Destroy(obj.gameObject);
                }
            }
            _activeNotes = renderedNow;
        }

        private bool RenderNote(Frame frame, int channel, in ManiaNote note, out ManiaNoteView noteView)
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
                GameObject noteObj = Instantiate(Config.Notes[channel], transform, false);
                noteView = noteObj.GetComponent<ManiaNoteView>();
            }
            else
            {
                noteView = _activeNotes[note.Id];
            }

            noteView.Render(x, y, note, Config.ScrollSpeed);
            return true;
        }
    }
}
