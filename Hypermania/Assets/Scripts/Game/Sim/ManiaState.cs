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
    }

    public enum ManiaEventKind
    {
        Hit,
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

        private static readonly InputFlags[] CHANNEL_INPUT =
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
                if (hasInput && frame < noteTick - Config.MissTotalRange)
                {
                    // tried to hit note way too early
                }
                else if (hasInput && frame < noteTick - Config.HitHalfRange)
                {
                    // missed note early
                    ManiaEvents.Add(ManiaEvent.MissEvent(note, true));
                    Channels[i].NextActiveIdx++;
                }
                else if (hasInput && frame <= noteTick + Config.HitHalfRange)
                {
                    // hit note
                    int diff = frame - noteTick;
                    ManiaEvents.Add(ManiaEvent.HitEvent(note, diff));
                    Channels[i].NextActiveIdx++;
                }
                else if (hasInput && frame <= noteTick + Config.MissTotalRange)
                {
                    // missed note late
                    ManiaEvents.Add(ManiaEvent.MissEvent(note, false));
                    Channels[i].NextActiveIdx++;
                }
                else if (frame > noteTick + Config.MissTotalRange)
                {
                    // note was missed automatically (too late)
                    ManiaEvents.Add(ManiaEvent.MissEvent(note, false));
                    Channels[i].NextActiveIdx++;
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
