using TMPro;
using UnityEngine;

public class UIControl : MonoBehaviour
{
    public TMP_InputField inputFieldCMD;
    public TMP_InputField inputFieldMSG;
    public NPCAI npc;
	public void SendCMD()
	{
		npc.GoTo(inputFieldCMD.text);
	}
	public void SendDialogue()
	{
		npc.dialogueManager.SendDialogue(inputFieldMSG.text);
	}
}
