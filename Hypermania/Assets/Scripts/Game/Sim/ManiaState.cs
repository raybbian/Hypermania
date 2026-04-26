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

        // Latches on press inside the hit window. The mechanic-facing Input
        // event then fires at noteTick + HitHalfRange instead of the press
        // frame, so every hit resolves on the same frame no matter when the
        // press came in. Keeps the sim aligned with the combo generator.
        public bool HitPending;

        // pressFrame - noteTick at latch time. View-only (timing grade).
        public int HitPendingOffset;
    }

    public enum ManiaEventKind
    {
        Hit,    // View-only: fires on press for immediate SFX/VFX.
        Input,  // Mechanic-facing: fires at noteTick + HitHalfRange; injects HitInput and raises rhythm-cancel.
        Missed,
        End,
    }

    [MemoryPackable]
    public partial struct ManiaEvent
    {
        public ManiaEventKind Kind;

        public ManiaNote Note;

        public int Offset;
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

        // Half-width of the hit window, so 2 * HitHalfRange + 1 ticks total.
        public int HitHalfRange;

        // Extra window outside the hit window where a press still counts as
        // a miss instead of being ignored. Stops players from hitting notes
        // way too early.
        public int MissHalfRange;

        public int MissTotalRange => HitHalfRange + MissHalfRange;
    }

    [MemoryPackable]
    public partial struct ManiaState
    {
        // Initial deque capacity, not a hard cap.
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
                    // Way too early to count as anything. Ignore.
                }
                else if (hasInput && frame < noteTick - Config.HitHalfRange)
                {
                    // Early miss. Misses fire immediately, no withholding.
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
                        // Late press, hit window already closed. Miss.
                        ManiaEvents.Add(ManiaEvent.MissEvent(note, false));
                        advance = true;
                    }
                    else if (frame > noteTick + Config.MissTotalRange)
                    {
                        // No press ever arrived. Auto-miss.
                        ManiaEvents.Add(ManiaEvent.MissEvent(note, false));
                        advance = true;
                    }
                    // Otherwise we're inside (noteTick + HitHalfRange, noteTick + MissTotalRange]
                    // with no press yet. Leave the note active so a late press can still register.
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
