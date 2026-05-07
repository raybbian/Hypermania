using Hypermania.CPU.Featurization;
using Hypermania.Game;
using TorchSharp.Modules;
using static TorchSharp.torch;
using static TorchSharp.torch.nn;
using F = TorchSharp.torch.nn.functional;

namespace Hypermania.CPU.Training
{
    // Recurrent two-head policy + scalar value. Shared MLP trunk feeds a GRU
    // layer; the GRU's per-step output goes into three Linear heads (direction
    // logits, button logits, value scalar). Direction and button are sampled
    // independently each step, factorizing the joint policy into two
    // categoricals.
    //
    // Architecture:
    //   obs → Linear(obs, hidden) → ReLU
    //       → Linear(hidden, hidden) → ReLU
    //       → GRU(hidden, hidden, batch_first=true)
    //       → dir/btn/value heads
    //
    // The forward signature handles two shapes:
    //   - single-step rollout/inference: obs [B, ObsDim], hIn [1, B, hidden]
    //     → returns dir/btn [B, *], value [B], hOut [1, B, hidden]
    //   - sequence training (PPO update): obs [B, T, ObsDim], hIn [1, B, hidden]
    //     → returns dir/btn [B, T, *], value [B, T], hOut [1, B, hidden]
    //
    // Per-env hidden state is owned by callers (NeuralPolicy for inference,
    // BatchedSelfPlayEnv / SelfPlayEnv for rollout, PpoTrainer for PPO updates
    // where it always starts at zero since each trajectory is one episode).
    public sealed class PolicyNet
        : Module<Tensor, Tensor, (Tensor dirLogits, Tensor btnLogits, Tensor value, Tensor hOut)>
    {
        readonly Linear _fc1;
        readonly Linear _fc2;
        readonly GRU _gru;
        readonly Linear _dirHead;
        readonly Linear _btnHead;
        readonly Linear _valueHead;

        public int ObsDim { get; }
        public int Hidden { get; }

        public PolicyNet(int obsDim, int hidden = 256)
            : base(nameof(PolicyNet))
        {
            ObsDim = obsDim;
            Hidden = hidden;
            _fc1 = Linear(obsDim, hidden);
            _fc2 = Linear(hidden, hidden);
            _gru = GRU(hidden, hidden, numLayers: 1, batchFirst: true);
            _dirHead = Linear(hidden, ActionSpace.NumDirections);
            _btnHead = Linear(hidden, ActionSpace.NumButtons);
            _valueHead = Linear(hidden, 1);
            RegisterComponents();
        }

        // Build a fresh zero hidden tensor on the given device. Shape
        // [1, batch, hidden] - num_layers * num_directions = 1.
        public Tensor InitHidden(int batch, Device device) =>
            zeros(new long[] { 1, batch, Hidden }, ScalarType.Float32, device);

        public override (Tensor dirLogits, Tensor btnLogits, Tensor value, Tensor hOut) forward(
            Tensor obs,
            Tensor hIn
        )
        {
            // MLP trunk operates on the last dim, so 2-D and 3-D obs both work.
            Tensor h = F.relu(_fc1.forward(obs));
            h = F.relu(_fc2.forward(h));

            Tensor seq;
            bool wasSingleStep = h.dim() == 2;
            if (wasSingleStep)
            {
                // Single-step path: insert a length-1 time dim for the GRU,
                // squeeze it back off the output.
                seq = h.unsqueeze(1); // [B, 1, hidden]
            }
            else
            {
                seq = h; // [B, T, hidden]
            }

            (Tensor gruOut, Tensor hOut) = _gru.forward(seq, hIn);
            Tensor latent = wasSingleStep ? gruOut.squeeze(1) : gruOut;

            Tensor dirLogits = _dirHead.forward(latent);
            Tensor btnLogits = _btnHead.forward(latent);
            Tensor value = _valueHead.forward(latent).squeeze(-1);
            return (dirLogits, btnLogits, value, hOut);
        }

        // Mask non-neutral logits to a large-negative additive on rows where
        // the perspective's own fighter is not actionable (hitstun, blockstun,
        // knockdown, super freeze, etc). Index 0 is the "no input" choice for
        // both heads, so leaving column 0 alone collapses sampling and argmax
        // to the neutral action without burning gradient on illegal options.
        // Read from the obs at the published offset so rollout, PPO update,
        // and inference all use the same source of truth.
        //
        // Blockstun exception: the dir head stays open so the policy can hold
        // back-to-block. Buttons stay masked - pressing a button during
        // blockstun does nothing useful and we don't want gradient on it.
        //
        // Works on 2-D obs [B, ObsDim] and 3-D obs [B, T, ObsDim]: select(-1)
        // returns [B] or [B, T], and broadcasting against the [Nd] / [Nb]
        // gates lifts to [B, Nd] / [B, T, Nd] automatically.
        static (Tensor dirOut, Tensor btnOut) ApplyActionMask(
            Tensor obs,
            Tensor dirLogits,
            Tensor btnLogits
        )
        {
            int actOff = Featurizer.OwnActionableObsOffset;
            // BlockStand and BlockCrouch are mutually exclusive one-hot bits;
            // either one means the fighter is in blockstun. Sum reduces the
            // gather to a single broadcast.
            int blockStandOff = Featurizer.OwnStateObsOffset(CharacterState.BlockStand);
            int blockCrouchOff = Featurizer.OwnStateObsOffset(CharacterState.BlockCrouch);
            int Nd = (int)dirLogits.size(-1);
            int Nb = (int)btnLogits.size(-1);
            Device dev = dirLogits.device;
            using Tensor actBit = obs.select(-1, actOff); // 1 if can act
            using Tensor blockStandBit = obs.select(-1, blockStandOff);
            using Tensor blockCrouchBit = obs.select(-1, blockCrouchOff);
            using Tensor blockBit = blockStandBit + blockCrouchBit; // {0, 1}
            using Tensor inactBit = (1f - actBit).unsqueeze(-1); // [..., 1]
            // Dir is gated only when non-actionable AND not blocking - the
            // policy needs the dir head open in blockstun to hold back/down
            // for high/low block. Buttons stay gated whenever non-actionable.
            using Tensor inactDir = inactBit * (1f - blockBit).unsqueeze(-1);
            // [Nd] = [0, 1, 1, ..., 1] - column 0 is the "no input" choice for
            // both heads, so leaving it untouched collapses sampling/argmax to
            // neutral on masked rows. arange clamped to 1 is cheap.
            using Tensor dirGate = arange(Nd).to(ScalarType.Float32).to(dev).clamp_max(1f);
            using Tensor btnGate = arange(Nb).to(ScalarType.Float32).to(dev).clamp_max(1f);
            using Tensor dirAdd = inactDir * dirGate * -1e9f;
            using Tensor btnAdd = inactBit * btnGate * -1e9f;
            return (dirLogits + dirAdd, btnLogits + btnAdd);
        }

        // Greedy action for inference. obs is a single 1-D tensor of length ObsDim
        // (or 2-D [1, ObsDim]); hIn is [1, 1, hidden]. Packs (dir, btn, value)
        // into one tensor and pulls to host with a single .cpu(); hOut stays on
        // the device so callers can chain it into the next step without a
        // round-trip.
        public (int dir, int btn, float value, Tensor hOut) ActGreedy(Tensor obs, Tensor hIn)
        {
            using var _ = no_grad();
            Tensor batched = obs.dim() == 1 ? obs.unsqueeze(0) : obs;
            var (dirLogits, btnLogits, value, hOut) = forward(batched, hIn);
            (dirLogits, btnLogits) = ApplyActionMask(batched, dirLogits, btnLogits);
            Tensor packed = cat(
                new[]
                {
                    dirLogits.argmax(-1).flatten().to(ScalarType.Float32),
                    btnLogits.argmax(-1).flatten().to(ScalarType.Float32),
                    value.flatten(),
                }
            );
            using Tensor host = packed.cpu();
            int dir = (int)host[0].item<float>();
            int btn = (int)host[1].item<float>();
            float v = host[2].item<float>();
            return (dir, btn, v, hOut);
        }

        // Sample an action and return its log-prob and value, used during
        // PPO rollout collection. All GPU work is done first; the four scalar
        // outputs are packed into a single tensor and pulled to host with one
        // .cpu() call to avoid 4× per-step CUDA stream syncs. hOut stays on
        // device.
        public (int dir, int btn, float logProb, float value, Tensor hOut) Sample(
            Tensor obs,
            Tensor hIn,
            Generator generator = null
        )
        {
            using var _ = no_grad();
            Tensor batched = obs.dim() == 1 ? obs.unsqueeze(0) : obs;
            var (dirLogits, btnLogits, value, hOut) = forward(batched, hIn);
            (dirLogits, btnLogits) = ApplyActionMask(batched, dirLogits, btnLogits);
            Tensor dirLogProb = F.log_softmax(dirLogits, -1);
            Tensor btnLogProb = F.log_softmax(btnLogits, -1);
            // Gumbel-max trick: argmax(log_prob + Gumbel(0,1)) ~ Categorical(prob).
            // Replaces softmax + multinomial with a single log_softmax shared
            // with the gather below, plus a few pointwise ops for the noise.
            Tensor dirSample = SampleGumbel(dirLogProb, generator);
            Tensor btnSample = SampleGumbel(btnLogProb, generator);
            Tensor dirChosen = dirLogProb.gather(-1, dirSample.unsqueeze(-1)).flatten();
            Tensor btnChosen = btnLogProb.gather(-1, btnSample.unsqueeze(-1)).flatten();
            Tensor packed = cat(
                new[]
                {
                    dirSample.flatten().to(ScalarType.Float32),
                    btnSample.flatten().to(ScalarType.Float32),
                    (dirChosen + btnChosen),
                    value.flatten(),
                }
            );
            using Tensor host = packed.cpu();
            int dir = (int)host[0].item<float>();
            int btn = (int)host[1].item<float>();
            float lp = host[2].item<float>();
            float v = host[3].item<float>();
            return (dir, btn, lp, v, hOut);
        }

        // argmax(logProb + Gumbel(0,1)). logProb shape [..., K]; output [...].
        // clamp_min guards log(0) when uniform draws underflow to zero.
        static Tensor SampleGumbel(Tensor logProb, Generator generator)
        {
            Tensor noise = empty_like(logProb).uniform_(0.0, 1.0, generator);
            Tensor gumbel = noise.clamp_min(1e-20f).log().neg().log().neg();
            return (logProb + gumbel).argmax(-1);
        }

        // Batched stochastic sample for the vectorized rollout collector.
        // obs: [B, ObsDim], hIn: [1, B, hidden]. Returns per-batch arrays of
        // length B plus the device-side hOut. All GPU work runs to completion,
        // then a single packed [4, B] tensor is pulled to host - one CUDA
        // stream sync regardless of batch size. hOut stays on device.
        public (int[] dir, int[] btn, float[] logProb, float[] value, Tensor hOut) SampleBatch(
            Tensor obs,
            Tensor hIn,
            Generator generator = null
        )
        {
            using var _ = no_grad();
            int B = (int)obs.size(0);
            var (dirLogits, btnLogits, value, hOut) = forward(obs, hIn);
            (dirLogits, btnLogits) = ApplyActionMask(obs, dirLogits, btnLogits);
            Tensor dirLogProb = F.log_softmax(dirLogits, -1);
            Tensor btnLogProb = F.log_softmax(btnLogits, -1);
            Tensor dirSample = SampleGumbel(dirLogProb, generator); // [B]
            Tensor btnSample = SampleGumbel(btnLogProb, generator); // [B]
            Tensor dirChosen = dirLogProb.gather(-1, dirSample.unsqueeze(-1)).squeeze(-1); // [B]
            Tensor btnChosen = btnLogProb.gather(-1, btnSample.unsqueeze(-1)).squeeze(-1); // [B]
            Tensor logProb = dirChosen + btnChosen; // [B]

            // Pack as [4, B] -> single host transfer. Hidden stays on device.
            Tensor packed = stack(
                new[]
                {
                    dirSample.to(ScalarType.Float32),
                    btnSample.to(ScalarType.Float32),
                    logProb,
                    value,
                },
                dim: 0
            );
            using Tensor host = packed.cpu().contiguous();
            float[] flat = host.data<float>().ToArray();
            int[] dir = new int[B];
            int[] btn = new int[B];
            float[] lp = new float[B];
            float[] v = new float[B];
            for (int i = 0; i < B; i++)
            {
                dir[i] = (int)flat[0 * B + i];
                btn[i] = (int)flat[1 * B + i];
                lp[i] = flat[2 * B + i];
                v[i] = flat[3 * B + i];
            }
            return (dir, btn, lp, v, hOut);
        }

        // Run learner.SampleBatch and opponent.SampleBatch (or ActGreedyBatch)
        // back-to-back without syncing between them, then pull every output
        // down in a single host transfer. The two-call version was the
        // dominant cost in the rollout phase because each .cpu() pulled the
        // host thread to a halt while the stream drained; this halves the
        // host round-trips per sim step.
        //
        // learnerHIn / oppHIn are [1, B, hidden] and the per-fighter hidden
        // outputs come back on-device so the caller can scatter them back
        // into the per-env hidden ring without a host round-trip.
        public static (
            int[] lDir,
            int[] lBtn,
            float[] lLogp,
            float[] lValue,
            int[] oppDir,
            int[] oppBtn,
            Tensor lHOut,
            Tensor oppHOut
        ) SampleBatchPair(
            PolicyNet learner,
            PolicyNet opponent,
            Tensor learnerObs,
            Tensor oppObs,
            Tensor learnerHIn,
            Tensor oppHIn,
            bool greedyOpponent,
            Generator generator = null
        )
        {
            using var _ = no_grad();
            int B = (int)learnerObs.size(0);

            // Learner: full sample with log-prob and value.
            var (lDirLogits, lBtnLogits, lValueT, lHOut) = learner.forward(learnerObs, learnerHIn);
            (lDirLogits, lBtnLogits) = ApplyActionMask(learnerObs, lDirLogits, lBtnLogits);
            Tensor lDirLogProb = F.log_softmax(lDirLogits, -1);
            Tensor lBtnLogProb = F.log_softmax(lBtnLogits, -1);
            Tensor lDirSample = SampleGumbel(lDirLogProb, generator); // [B]
            Tensor lBtnSample = SampleGumbel(lBtnLogProb, generator); // [B]
            Tensor lDirChosen = lDirLogProb.gather(-1, lDirSample.unsqueeze(-1)).squeeze(-1);
            Tensor lBtnChosen = lBtnLogProb.gather(-1, lBtnSample.unsqueeze(-1)).squeeze(-1);
            Tensor lLogProb = lDirChosen + lBtnChosen;

            // Opponent: argmax (greedy) or sampled, no log-prob/value needed.
            var (oDirLogits, oBtnLogits, _, oppHOut) = opponent.forward(oppObs, oppHIn);
            (oDirLogits, oBtnLogits) = ApplyActionMask(oppObs, oDirLogits, oBtnLogits);
            Tensor oDir;
            Tensor oBtn;
            if (greedyOpponent)
            {
                oDir = oDirLogits.argmax(-1);
                oBtn = oBtnLogits.argmax(-1);
            }
            else
            {
                Tensor oDirLogProb = F.log_softmax(oDirLogits, -1);
                Tensor oBtnLogProb = F.log_softmax(oBtnLogits, -1);
                oDir = SampleGumbel(oDirLogProb, generator);
                oBtn = SampleGumbel(oBtnLogProb, generator);
            }

            // Pack [6, B] -> single sync. Cast int outputs to float so they
            // can share a tensor with the float log-prob/value rows. Hidden
            // tensors stay on device.
            Tensor packed = stack(
                new[]
                {
                    lDirSample.to(ScalarType.Float32),
                    lBtnSample.to(ScalarType.Float32),
                    lLogProb,
                    lValueT,
                    oDir.to(ScalarType.Float32),
                    oBtn.to(ScalarType.Float32),
                },
                dim: 0
            );
            using Tensor host = packed.cpu().contiguous();
            float[] flat = host.data<float>().ToArray();
            int[] lDir = new int[B];
            int[] lBtn = new int[B];
            float[] lLogp = new float[B];
            float[] lValue = new float[B];
            int[] oppDirOut = new int[B];
            int[] oppBtnOut = new int[B];
            for (int i = 0; i < B; i++)
            {
                lDir[i] = (int)flat[0 * B + i];
                lBtn[i] = (int)flat[1 * B + i];
                lLogp[i] = flat[2 * B + i];
                lValue[i] = flat[3 * B + i];
                oppDirOut[i] = (int)flat[4 * B + i];
                oppBtnOut[i] = (int)flat[5 * B + i];
            }
            return (lDir, lBtn, lLogp, lValue, oppDirOut, oppBtnOut, lHOut, oppHOut);
        }

        // Batched greedy action for the vectorized opponent path. obs: [B, ObsDim].
        // hOut returned on device.
        public (int[] dir, int[] btn, float[] value, Tensor hOut) ActGreedyBatch(
            Tensor obs,
            Tensor hIn
        )
        {
            using var _ = no_grad();
            int B = (int)obs.size(0);
            var (dirLogits, btnLogits, value, hOut) = forward(obs, hIn);
            (dirLogits, btnLogits) = ApplyActionMask(obs, dirLogits, btnLogits);
            Tensor packed = stack(
                new[]
                {
                    dirLogits.argmax(-1).to(ScalarType.Float32),
                    btnLogits.argmax(-1).to(ScalarType.Float32),
                    value,
                },
                dim: 0
            );
            using Tensor host = packed.cpu().contiguous();
            float[] flat = host.data<float>().ToArray();
            int[] dir = new int[B];
            int[] btn = new int[B];
            float[] v = new float[B];
            for (int i = 0; i < B; i++)
            {
                dir[i] = (int)flat[0 * B + i];
                btn[i] = (int)flat[1 * B + i];
                v[i] = flat[2 * B + i];
            }
            return (dir, btn, v, hOut);
        }

        // Evaluate stored actions under the current policy. Used in the PPO
        // optimization step. obs: [B, T, ObsDim], hIn: [1, B, hidden],
        // dirActions/btnActions: [B, T] long. Returns log-probs [B, T],
        // entropy [B, T], value [B, T]. Caller is responsible for padding
        // and masking out invalid steps.
        public (Tensor logProb, Tensor entropy, Tensor value) Evaluate(
            Tensor obs,
            Tensor hIn,
            Tensor dirActions,
            Tensor btnActions
        )
        {
            var (dirLogits, btnLogits, value, _) = forward(obs, hIn);
            // Same mask as rollout - keeps the PPO ratio numerator/denominator
            // consistent (oldLogp was computed with this mask, newLogp must
            // match). On non-actionable rows the loss collapses to "neutral
            // is the only valid action" with zero entropy contribution.
            (dirLogits, btnLogits) = ApplyActionMask(obs, dirLogits, btnLogits);
            Tensor dirLogProb = F.log_softmax(dirLogits, -1);
            Tensor btnLogProb = F.log_softmax(btnLogits, -1);

            Tensor dirChosen = dirLogProb.gather(-1, dirActions.unsqueeze(-1)).squeeze(-1);
            Tensor btnChosen = btnLogProb.gather(-1, btnActions.unsqueeze(-1)).squeeze(-1);
            Tensor logProb = dirChosen + btnChosen;

            Tensor dirProbs = F.softmax(dirLogits, -1);
            Tensor btnProbs = F.softmax(btnLogits, -1);
            Tensor dirEnt = -(dirProbs * dirLogProb).sum(-1);
            Tensor btnEnt = -(btnProbs * btnLogProb).sum(-1);
            Tensor entropy = dirEnt + btnEnt;

            return (logProb, entropy, value);
        }
    }
}
