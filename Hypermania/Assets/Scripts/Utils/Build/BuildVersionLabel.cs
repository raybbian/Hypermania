using TMPro;
using UnityEngine;

namespace Utils.Build
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
