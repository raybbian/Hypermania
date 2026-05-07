using Game;
using UnityEngine;
using Utils.EnumArray;
using Hypermania.Shared;
using Hypermania.Game;

namespace Game.View.Configs
{
    // View-side roster: maps each Character enum to its CharacterPresentation.
    // The sim equivalent (Character -> CharacterStats) lives on GlobalStats.
    // CharacterSelect uses this to populate previews/skins.
    [CreateAssetMenu(menuName = "Hypermania/View/Presentation Roster")]
    public class PresentationRoster : ScriptableObject
    {
        [SerializeField]
        EnumArray<Character, CharacterPresentation> _characters;

        public CharacterPresentation Get(Character character) => _characters[character];
    }
}
