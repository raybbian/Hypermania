using UnityEngine;

namespace Game.View.Overlay
{
    public class VictoryMarkView : MonoBehaviour
    {
        public GameObject[] assets;
        private byte[] victoryData;
        private GameObject[] victories;

        public void Awake() { }

        private void createSprites(byte[] playerVictories, int direction)
        {
            victoryData = playerVictories;
            if (victories != null)
            {
                foreach (GameObject gO in victories)
                {
                    Destroy(gO);
                }
            }
            victories = new GameObject[victoryData.Length];
            for (int i = 0; i < victories.Length; i++)
            {
                if (victories[i] != null)
                {
                    victories[i] = Instantiate(assets[victoryData[i]]);
                }
                else
                {
                    victories[i] = Instantiate(assets[victoryData[0]]);
                }
                victories[i].transform.SetParent(gameObject.transform);
                victories[i].transform.localScale = Vector3.one;
                victories[i].transform.localPosition = new Vector3(direction * i * 56, 0, 0);
            }
        }

        public void SetVictories(byte[] playerVictories, int direction)
        {
            if (victoryData == null || victoryData.Length != playerVictories.Length)
            {
                createSprites(playerVictories, direction);
            }
            for (int i = 0; i < playerVictories.Length; i++)
            {
                if (victoryData[i] != playerVictories[i])
                {
                    Destroy(victories[i]);
                    victoryData[i] = playerVictories[i];
                    if (victories[i] != null)
                    {
                        victories[i] = Instantiate(assets[victoryData[i]]);
                    }
                    else
                    {
                        victories[i] = Instantiate(assets[0]);
                    }
                    victories[i].transform.SetParent(gameObject.transform);
                    victories[i].transform.localScale = Vector3.one;
                    victories[i].transform.localPosition = new Vector3(direction * i * 56, 0, 0);
                }
            }
        }
    }
}
