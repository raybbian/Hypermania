using System.Collections.Generic;
using MemoryPack;
using Utils;
using Utils.SoftFloat;

namespace Game.Sim
{
    [MemoryPackable]
    public partial struct ManiaNote
    {
        public int Id;
        public Frame Tick;
        public int Length;
        public InputFlags HitInput;
    }

    // Each channel for ManiaView (up, down, left, right)
    [MemoryPackable]
    public partial struct ManiaNoteChannel
    {
        public Deque<ManiaNote> Notes;
        public int NextActiveIdx;
        public bool Pressed;

        /// <summary>
        /// Latched true when the player presses the channel's key inside the
        /// active note's hit window. On latch, <see cref="ManiaState.Tick"/>
        /// emits a view-facing <see cref="ManiaEventKind.Hit"/> event
        /// immediately (so SFX/VFX are not delayed) but defers the
        /// mechanic-facing <see cref="ManiaEventKind.Input"/> event to the
        /// last frame of the hit window (<c>noteTick + HitHalfRange</c>).
        /// This makes the effective input frame deterministic: every hit,
        /// regardless of press timing within the window, resolves at the
        /// same frame, so downstream fighter state no longer needs
        /// offset-compensating guards to stay in sync with the combo
        /// generator's predictions.
        /// </summary>
        public bool HitPending;

        /// <summary>
        /// Offset (<c>pressFrame - noteTick</c>) captured at the moment
        /// <see cref="HitPending"/> latches. Preserved for the view layer's
        /// timing grade (e.g. Perfect/Great); not consulted by sim logic.
        /// </summary>
        public int HitPendingOffset;
    }

    public enum ManiaEventKind
    {
        /// <summary>
        /// Emitted on the frame the player latches a press inside a note's
        /// hit window. View-only: drives SFX/VFX so feedback is immediate,
        /// not delayed to the dispatch frame.
        /// </summary>
        Hit,

        /// <summary>
        /// Emitted at the end of a latched note's hit window
        /// (<c>noteTick + HitHalfRange</c>). This is the mechanic-facing
        /// event: <see cref="GameState.DoManiaStep"/> injects the note's
        /// <see cref="ManiaNote.HitInput"/> into the attacker's input and
        /// raises the rhythm-cancel flag only on this event.
        /// </summary>
        Input,

        Missed,
        End,
    }

    [MemoryPackable]
    public partial struct ManiaEvent
    {
        public ManiaEventKind Kind;

        public ManiaNote Note;

        /// <summary>
        /// If the note was hit, how many ticks away from the exact timing it was hit
        /// </summary>
        public int Offset;

        /// <summary>
        /// If the note was missed, if it was hit early or late
        /// </summary>
        public bool Early;

        public static ManiaEvent EndEvent()
        {
            return new ManiaEvent { Kind = ManiaEventKind.End };
        }

        public static ManiaEvent HitEvent(in ManiaNote note, int offset)
        {
            return new ManiaEvent
            {
                Note = note,
                Offset = offset,
                Kind = ManiaEventKind.Hit,
            };
        }

        public static ManiaEvent InputEvent(in ManiaNote note, int offset)
        {
            return new ManiaEvent
            {
                Note = note,
                Offset = offset,
                Kind = ManiaEventKind.Input,
            };
        }

        public static ManiaEvent MissEvent(in ManiaNote note, bool early)
        {
            return new ManiaEvent
            {
                Note = note,
                Early = early,
                Kind = ManiaEventKind.Missed,
            };
        }
    }

    [MemoryPackable]
    public partial struct ManiaConfig
    {
        public int NumKeys;

        /// <summary>
        /// The half window of when the note is able to be hit. In other words, there are 2 * HitHalfRange + 1 ticks
        /// in which a note may be hit (and not missed)
        /// </summary>
        public int HitHalfRange;

        /// <summary>
        /// The additional window outside of the hit window in which a note can be actively missed. This is used to
        /// prevent notes from being hit wayyyy to early.
        /// </summary>
        public int MissHalfRange;

        public int MissTotalRange => HitHalfRange + MissHalfRange;
    }

    [MemoryPackable]
    public partial struct ManiaState
    {
        /// <summary>
        /// Used to initialized the deque with capacity, not necessarily a hard cap
        /// </summary>
        const int MAX_NOTES = 100;
        public int TotalNoteCount;
        public ManiaConfig Config;
        public ManiaNoteChannel[] Channels;
        public Frame EndFrame;
        public List<ManiaEvent> ManiaEvents;

        public bool Enabled(Frame frame) => frame <= EndFrame;

        internal static readonly InputFlags[] CHANNEL_INPUT =
        {
            InputFlags.Mania1,
            InputFlags.Mania2,
            InputFlags.Mania3,
            InputFlags.Mania4,
            InputFlags.Mania5,
            InputFlags.Mania6,
        };

        public static ManiaState Create(in ManiaConfig config)
        {
            ManiaState sim = new ManiaState();
            sim.Config = config;
            sim.TotalNoteCount = 0;
            sim.Channels = new ManiaNoteChannel[config.NumKeys];
            for (int i = 0; i < config.NumKeys; i++)
            {
                sim.Channels[i] = new ManiaNoteChannel
                {
                    Notes = new Deque<ManiaNote>(MAX_NOTES),
                    NextActiveIdx = 0,
                    Pressed = false,
                    HitPending = false,
                    HitPendingOffset = 0,
                };
            }
            sim.EndFrame = Frame.NullFrame;
            sim.ManiaEvents = new();
            return sim;
        }

        public void Enable(Frame endFrame)
        {
            EndFrame = endFrame;
        }

        public void End()
        {
            EndFrame = Frame.NullFrame;
            for (int i = 0; i < Channels.Length; i++)
            {
                Channels[i].Pressed = false;
                Channels[i].Notes.Clear();
                Channels[i].NextActiveIdx = 0;
                Channels[i].HitPending = false;
                Channels[i].HitPendingOffset = 0;
            }
            TotalNoteCount = 0;
        }

        public void QueueNote(int channel, ManiaNote note)
        {
            note.Id = TotalNoteCount++;
            Channels[channel].Notes.PushBack(note);
        }

        public void Tick(Frame frame, GameInput input)
        {
            if (frame > EndFrame)
                return;
            for (int i = 0; i < Channels.Length; i++)
            {
                bool hasInput = input.HasInput(CHANNEL_INPUT[i]);
                Channels[i].Pressed = hasInput;
                if (Channels[i].NextActiveIdx >= Channels[i].Notes.Count)
                {
                    continue;
                }
                ManiaNote note = Channels[i].Notes[Channels[i].NextActiveIdx];
                Frame noteTick = note.Tick;

                // Latch a press inside the hit window. Emit the view-facing
                // Hit event immediately (so SFX/VFX land on the press frame),
                // but defer the mechanic-facing Input event to the dispatch
                // condition below so every hit resolves at the same frame
                // regardless of press timing within the window.
                if (
                    hasInput
                    && !Channels[i].HitPending
                    && frame >= noteTick - Config.HitHalfRange
                    && frame <= noteTick + Config.HitHalfRange
                )
                {
                    Channels[i].HitPending = true;
                    Channels[i].HitPendingOffset = frame - noteTick;
                    ManiaEvents.Add(ManiaEvent.HitEvent(note, Channels[i].HitPendingOffset));
                }

                bool advance = false;
                if (hasInput && frame < noteTick - Config.MissTotalRange)
                {
                    // tried to hit note way too early — no event
                }
                else if (hasInput && frame < noteTick - Config.HitHalfRange)
                {
                    // early miss (fires immediately — misses do not withhold)
                    ManiaEvents.Add(ManiaEvent.MissEvent(note, true));
                    advance = true;
                }
                else if (frame >= noteTick + Config.HitHalfRange)
                {
                    // Dispatch at / after the last frame of the hit window.
                    // Use >= to stay robust against frames skipped by
                    // SpeedRatio or hitstop gating.
                    if (Channels[i].HitPending)
                    {
                        ManiaEvents.Add(ManiaEvent.InputEvent(note, Channels[i].HitPendingOffset));
                        advance = true;
                    }
                    else if (hasInput && frame <= noteTick + Config.MissTotalRange)
                    {
                        // late press after hit window closed — miss
                        ManiaEvents.Add(ManiaEvent.MissEvent(note, false));
                        advance = true;
                    }
                    else if (frame > noteTick + Config.MissTotalRange)
                    {
                        // no press arrived in time — auto-miss
                        ManiaEvents.Add(ManiaEvent.MissEvent(note, false));
                        advance = true;
                    }
                    // else: inside (noteTick + HitHalfRange, noteTick + MissTotalRange]
                    // with no press yet — keep the note active to catch a late press.
                }

                if (advance)
                {
                    Channels[i].NextActiveIdx++;
                    Channels[i].HitPending = false;
                    Channels[i].HitPendingOffset = 0;
                }
            }
            if (frame == EndFrame)
            {
                ManiaEvents.Add(ManiaEvent.EndEvent());
                End();
            }
        }
    }
}
