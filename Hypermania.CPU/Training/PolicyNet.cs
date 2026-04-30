using System;
using TorchSharp;
using TorchSharp.Modules;
using static TorchSharp.torch;
using static TorchSharp.torch.nn;
using F = TorchSharp.torch.nn.functional;

namespace Hypermania.CPU.Training
{
    // Two-head policy + scalar value. Shared MLP trunk feeds three Linear heads
    // (direction logits, button logits, value scalar). Direction and button are
    // sampled independently each step, factorizing the joint policy into two
    // categoricals.
    //
    // Forward returns raw logits and an unsqueezed value tensor; the trainer
    // applies softmax / log_softmax / cross-entropy where it needs them.
    public sealed class PolicyNet
        : Module<Tensor, (Tensor dirLogits, Tensor btnLogits, Tensor value)>
    {
        readonly Linear _fc1;
        readonly Linear _fc2;
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
            _dirHead = Linear(hidden, ActionSpace.NumDirections);
            _btnHead = Linear(hidden, ActionSpace.NumButtons);
            _valueHead = Linear(hidden, 1);
            RegisterComponents();
        }

        public override (Tensor dirLogits, Tensor btnLogits, Tensor value) forward(Tensor obs)
        {
            Tensor h = F.relu(_fc1.forward(obs));
            h = F.relu(_fc2.forward(h));
            Tensor dirLogits = _dirHead.forward(h);
            Tensor btnLogits = _btnHead.forward(h);
            Tensor value = _valueHead.forward(h).squeeze(-1);
            return (dirLogits, btnLogits, value);
        }

        // Greedy action for inference. obs is a single 1-D tensor of length ObsDim.
        // Packs (dir, btn, value) into one tensor and pulls to host with a single
        // .cpu() to amortize the device sync.
        public (int dir, int btn, float value) ActGreedy(Tensor obs)
        {
            using var _ = no_grad();
            Tensor batched = obs.dim() == 1 ? obs.unsqueeze(0) : obs;
            var (dirLogits, btnLogits, value) = forward(batched);
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
            return (dir, btn, v);
        }

        // Sample an action and return its log-prob and value, used during
        // PPO rollout collection. All GPU work is done first; the four scalar
        // outputs are packed into a single tensor and pulled to host with one
        // .cpu() call to avoid 4× per-step CUDA stream syncs.
        public (int dir, int btn, float logProb, float value) Sample(
            Tensor obs,
            Generator generator = null
        )
        {
            using var _ = no_grad();
            Tensor batched = obs.dim() == 1 ? obs.unsqueeze(0) : obs;
            var (dirLogits, btnLogits, value) = forward(batched);
            Tensor dirProbs = F.softmax(dirLogits, -1);
            Tensor btnProbs = F.softmax(btnLogits, -1);
            Tensor dirSample =
                generator != null
                    ? multinomial(dirProbs, 1, generator: generator)
                    : multinomial(dirProbs, 1);
            Tensor btnSample =
                generator != null
                    ? multinomial(btnProbs, 1, generator: generator)
                    : multinomial(btnProbs, 1);
            Tensor dirLogProb = F.log_softmax(dirLogits, -1);
            Tensor btnLogProb = F.log_softmax(btnLogits, -1);
            // Gather the chosen-action log-probs without a host round-trip.
            Tensor dirChosen = dirLogProb.gather(-1, dirSample).flatten();
            Tensor btnChosen = btnLogProb.gather(-1, btnSample).flatten();
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
            return (dir, btn, lp, v);
        }

        // Batched stochastic sample for the vectorized rollout collector.
        // obs: [B, ObsDim]. Returns per-batch arrays of length B. All GPU work
        // runs to completion, then a single packed [4, B] tensor is pulled to
        // host - one CUDA stream sync regardless of batch size.
        public (int[] dir, int[] btn, float[] logProb, float[] value) SampleBatch(
            Tensor obs,
            Generator generator = null
        )
        {
            using var _ = no_grad();
            int B = (int)obs.size(0);
            var (dirLogits, btnLogits, value) = forward(obs);
            Tensor dirProbs = F.softmax(dirLogits, -1);
            Tensor btnProbs = F.softmax(btnLogits, -1);
            Tensor dirSample =
                generator != null
                    ? multinomial(dirProbs, 1, generator: generator)
                    : multinomial(dirProbs, 1); // [B, 1]
            Tensor btnSample =
                generator != null
                    ? multinomial(btnProbs, 1, generator: generator)
                    : multinomial(btnProbs, 1); // [B, 1]
            Tensor dirLogProb = F.log_softmax(dirLogits, -1);
            Tensor btnLogProb = F.log_softmax(btnLogits, -1);
            Tensor dirChosen = dirLogProb.gather(-1, dirSample).squeeze(-1); // [B]
            Tensor btnChosen = btnLogProb.gather(-1, btnSample).squeeze(-1); // [B]
            Tensor logProb = dirChosen + btnChosen; // [B]

            // Pack as [4, B] -> single host transfer.
            Tensor packed = stack(
                new[]
                {
                    dirSample.squeeze(-1).to(ScalarType.Float32),
                    btnSample.squeeze(-1).to(ScalarType.Float32),
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
            return (dir, btn, lp, v);
        }

        // Batched greedy action for the vectorized opponent path. obs: [B, ObsDim].
        public (int[] dir, int[] btn, float[] value) ActGreedyBatch(Tensor obs)
        {
            using var _ = no_grad();
            int B = (int)obs.size(0);
            var (dirLogits, btnLogits, value) = forward(obs);
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
            return (dir, btn, v);
        }

        // Evaluate stored actions under the current policy. Used in the PPO
        // optimization step. obs: [B, ObsDim], dirActions/btnActions: [B] long,
        // returns log-probs [B], entropy [B], value [B].
        public (Tensor logProb, Tensor entropy, Tensor value) Evaluate(
            Tensor obs,
            Tensor dirActions,
            Tensor btnActions
        )
        {
            var (dirLogits, btnLogits, value) = forward(obs);
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
