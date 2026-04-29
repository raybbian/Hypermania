using System;
using TorchSharp;
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
    public sealed class PolicyNet : Module<Tensor, (Tensor dirLogits, Tensor btnLogits, Tensor value)>
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
        public (int dir, int btn, float value) ActGreedy(Tensor obs)
        {
            using var _ = no_grad();
            Tensor batched = obs.dim() == 1 ? obs.unsqueeze(0) : obs;
            var (dirLogits, btnLogits, value) = forward(batched);
            int dir = (int)dirLogits.argmax(-1).cpu().item<long>();
            int btn = (int)btnLogits.argmax(-1).cpu().item<long>();
            float v = value.cpu().item<float>();
            return (dir, btn, v);
        }

        // Sample an action and return its log-prob and value, used during
        // PPO rollout collection.
        public (int dir, int btn, float logProb, float value) Sample(Tensor obs, Generator generator = null)
        {
            using var _ = no_grad();
            Tensor batched = obs.dim() == 1 ? obs.unsqueeze(0) : obs;
            var (dirLogits, btnLogits, value) = forward(batched);
            Tensor dirProbs = F.softmax(dirLogits, -1);
            Tensor btnProbs = F.softmax(btnLogits, -1);
            Tensor dirSample = generator != null
                ? multinomial(dirProbs, 1, generator: generator)
                : multinomial(dirProbs, 1);
            Tensor btnSample = generator != null
                ? multinomial(btnProbs, 1, generator: generator)
                : multinomial(btnProbs, 1);
            int dir = (int)dirSample.cpu().item<long>();
            int btn = (int)btnSample.cpu().item<long>();
            Tensor dirLogProb = F.log_softmax(dirLogits, -1);
            Tensor btnLogProb = F.log_softmax(btnLogits, -1);
            float lp = dirLogProb.cpu()[0, dir].item<float>() + btnLogProb.cpu()[0, btn].item<float>();
            float v = value.cpu().item<float>();
            return (dir, btn, lp, v);
        }

        // Evaluate stored actions under the current policy. Used in the PPO
        // optimization step. obs: [B, ObsDim], dirActions/btnActions: [B] long,
        // returns log-probs [B], entropy [B], value [B].
        public (Tensor logProb, Tensor entropy, Tensor value) Evaluate(
            Tensor obs, Tensor dirActions, Tensor btnActions
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
