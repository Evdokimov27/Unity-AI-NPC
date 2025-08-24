using TMPro;
using UnityEngine;

public class UIControl : MonoBehaviour
{
    public TMP_InputField inputFieldCMD;
	public NPCAIHub hub;
	public void SendCMD()
	{
		hub.ExecuteCommand(inputFieldCMD.text);
	}

}
