using System;
using Game.Sim;
using Netcode.Rollback;

namespace Hypermania.CPU.Hosting
{
    // Headless wrapper around GameState that exposes a step API for trainers.
    // Bypasses SyncTestSession on purpose - training does not need rollback,
    // and pulling Steamworks-touching session code into netstandard is more
    // trouble than it is worth. Takes SimOptions directly: presentation and
    // input options live on the Unity-side GameOptions and are not relevant
    // to a headless trainer.
    public sealed class TrainingEnv
    {
        readonly SimOptions _options;
        readonly (GameInput input, InputStatus status)[] _scratch;

        public GameState State { get; private set; }

        public TrainingEnv(SimOptions options)
        {
            if (options == null)
                throw new ArgumentNullException(nameof(options));
            _options = options;
            _scratch = new (GameInput, InputStatus)[options.Players.Length];
            Reset();
        }

        public void Reset()
        {
            State = GameState.Create(_options);
        }

        // Returns true once the match is over (GameMode.End).
        public bool Step(InputFlags p1, InputFlags p2)
        {
            _scratch[0] = (new GameInput(p1), InputStatus.Confirmed);
            _scratch[1] = (new GameInput(p2), InputStatus.Confirmed);
            State.Advance(_options, _scratch);
            return State.GameMode == GameMode.End;
        }
    }
}
