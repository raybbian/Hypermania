using UnityEngine;
using Utils;
using Hypermania.Game;
using Hypermania.Shared;

namespace Game.View.Overlay
{
    [RequireComponent(typeof(Animator))]
    [RequireComponent(typeof(AudioSource))]
    public class IntroView : MonoBehaviour
    {
        [SerializeField]
        private IntroCharacterPanel[] _characterPanels;

        [SerializeField]
        private AudioClip _sfx;

        [SerializeField]
        private int _sfxStartTicks = 40;

        private Animator _animator;
        private AudioSource _audioSource;

        public void Awake()
        {
            _animator = GetComponent<Animator>();
            _audioSource = GetComponent<AudioSource>();
        }

        public void Init(GameOptions options)
        {
            if (_characterPanels == null)
                return;
            int count = Mathf.Min(_characterPanels.Length, options.Presentation.Players.Length);
            for (int i = 0; i < count; i++)
            {
                if (_characterPanels[i] != null)
                    _characterPanels[i].SetCharacter(
                        options.Presentation.Players[i].Character,
                        options.Presentation.Players[i].SkinIndex
                    );
            }
        }

        public void DisplayIntro(Frame currentFrame, GameOptions options)
        {
            int delay = options.Sim.Global.PreGameDelayTicks;
            bool visible = delay > 0 && currentFrame.No < delay;
            _animator.SetBool("Show", visible);
            if (currentFrame == _sfxStartTicks)
            {
                _audioSource.PlayOneShot(_sfx);
            }
        }
    }
}
