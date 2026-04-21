using System;
using System.Collections.Generic;
using Design.Configs;
using Game.Sim;
using Game.View.Events;
using Game.View.Events.Vfx;
using Steamworks;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UI;
using Utils;

namespace Game.View.Mania
{
    [Serializable]
    public struct ManiaViewConfig
    {
        public float ScrollSpeed;
        public Transform[] Anchors;
        public GameObject[] Notes;
        public RectTransform BeatLineContainer;

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

        private AudioConfig _audioConfig;
        private List<RectTransform> _beatLinePool;

        public void Init(AudioConfig audioConfig)
        {
            _audioConfig = audioConfig;
            _activeNotes = new Dictionary<int, ManiaNoteView>();
            _beatLinePool = new List<RectTransform>();

            RectTransform rect = GetComponent<RectTransform>();
            float viewHeight = rect.rect.height;
            int framesVisible = Mathf.CeilToInt(viewHeight / Config.ScrollSpeed);
            int poolSize = framesVisible / _audioConfig.FramesPerBeat + 3;

            Transform lineParent = Config.BeatLineContainer != null ? Config.BeatLineContainer.transform : transform;

            for (int i = 0; i < poolSize; i++)
            {
                GameObject lineObj = new GameObject("BeatLine", typeof(RectTransform), typeof(Image));
                lineObj.transform.SetParent(lineParent, false);

                RectTransform lineRect = lineObj.GetComponent<RectTransform>();
                lineRect.sizeDelta = new Vector2(rect.rect.width, 1f);
                lineRect.anchorMin = new Vector2(0.5f, 0.5f);
                lineRect.anchorMax = new Vector2(0.5f, 0.5f);

                Image img = lineObj.GetComponent<Image>();
                img.color = new Color(1f, 1f, 1f, 0.15f);
                img.raycastTarget = false;

                lineObj.SetActive(false);
                _beatLinePool.Add(lineRect);
            }

            gameObject.SetActive(false);
        }

        public void DeInit()
        {
            foreach (var obj in _activeNotes.Values)
            {
                Destroy(obj.gameObject);
            }
            _activeNotes = null;

            if (_beatLinePool != null)
            {
                foreach (var line in _beatLinePool)
                {
                    Destroy(line.gameObject);
                }
                _beatLinePool = null;
            }
            _audioConfig = null;
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
                    sfx.AddDesired(SfxKind.ComboGood, realFrame, hash: i);
                    vfx.AddDesired(VfxKind.NoteHit, realFrame, hash: i, position: world);
                }
                else if (maniaState.ManiaEvents[i].Kind == ManiaEventKind.Missed)
                {
                    sfx.AddDesired(SfxKind.ComboMiss, realFrame, hash: i);
                    vfx.AddDesired(VfxKind.NoteMiss, realFrame, hash: i, position: world);
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
                // Once the player has latched a press on the active note,
                // hide it from the view — the hit is pending dispatch at
                // `noteTick + HitHalfRange`, and the note shouldn't keep
                // scrolling past the judgment anchor while we wait.
                int startIdx = state.Channels[i].NextActiveIdx;
                if (state.Channels[i].HitPending)
                {
                    startIdx++;
                }
                for (int j = startIdx; j < state.Channels[i].Notes.Count; j++)
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

            RenderBeatLines(frame);
        }

        private void RenderBeatLines(Frame frame)
        {
            if (_audioConfig == null || _beatLinePool == null)
                return;

            int framesPerBeat = _audioConfig.FramesPerBeat;
            int firstBeat = _audioConfig.FirstMusicalBeat.No;
            float anchorY = Config.Anchors[0].localPosition.y;
            float halfHeight = GetComponent<RectTransform>().rect.height / 2;

            // Find the first beat index that could be visible (below bottom of view)
            int minBeatIndex = Mathf.FloorToInt((float)(frame.No - firstBeat) / framesPerBeat) - 1;
            if (minBeatIndex < 0)
                minBeatIndex = 0;

            int poolIndex = 0;
            for (int b = minBeatIndex; poolIndex < _beatLinePool.Count; b++)
            {
                int beatFrame = firstBeat + _audioConfig.BeatsToFrame(b);
                float y = (beatFrame - frame) * Config.ScrollSpeed + anchorY;

                if (y > halfHeight)
                    break;

                // skip lines below the view
                if (y < -halfHeight)
                    continue;

                RectTransform line = _beatLinePool[poolIndex];
                line.gameObject.SetActive(true);
                line.localPosition = new Vector3(0f, y, 0f);
                poolIndex++;
            }

            // hide unused lines
            for (int i = poolIndex; i < _beatLinePool.Count; i++)
            {
                _beatLinePool[i].gameObject.SetActive(false);
            }
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
