using TMPro;
using UnityEngine;
using Utils.Build;

namespace Game.View.Overlay.Build
{
    [RequireComponent(typeof(TMP_Text))]
    public class BuildVersionLabel : MonoBehaviour
    {
        private void Awake()
        {
            GetComponent<TMP_Text>().text = BuildInfo.BuildId;
        }
    }
}
