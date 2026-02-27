using UnityEngine;
using Utils.EnumArray;

namespace Game.View.Overlay
{
    public enum VictoryKind
    {
        Empty,
        Normal,
        Perfect,
    }

    public class VictoryMarkView : MonoBehaviour
    {
        [SerializeField]
        private EnumArray<VictoryKind, GameObject> _assets;
        private VictoryKind[] _victoryData;
        private GameObject[] _victories;

        public void Awake() { }

        private void CreateSprites(VictoryKind[] playerVictories, int direction)
        {
            _victoryData = playerVictories;
            if (_victories != null)
            {
                foreach (GameObject gO in _victories)
                {
                    Destroy(gO);
                }
            }
            _victories = new GameObject[_victoryData.Length];
            for (int i = 0; i < _victories.Length; i++)
            {
                if (_victories[i] != null)
                {
                    _victories[i] = Instantiate(_assets[_victoryData[i]]);
                }
                else
                {
                    _victories[i] = Instantiate(_assets[_victoryData[0]]);
                }
                _victories[i].transform.SetParent(gameObject.transform);
                _victories[i].transform.localScale = Vector3.one;
            }
        }

        public void SetVictories(VictoryKind[] playerVictories, int direction)
        {
            if (_victoryData == null || _victoryData.Length != playerVictories.Length)
            {
                CreateSprites(playerVictories, direction);
            }

            for (int i = 0; i < playerVictories.Length; i++)
            {
                if (_victoryData[i] != playerVictories[i])
                {
                    Destroy(_victories[i]);
                    _victoryData[i] = playerVictories[i];
                    if (_victories[i] != null)
                    {
                        _victories[i] = Instantiate(_assets[_victoryData[i]]);
                    }
                    else
                    {
                        _victories[i] = Instantiate(_assets[0]);
                    }
                    _victories[i].transform.SetParent(gameObject.transform);
                    _victories[i].transform.localScale = Vector3.one;
                    _victories[i].transform.localPosition = new Vector3(direction * i * 56, 0, 0);
                }
            }
        }
    }
}
