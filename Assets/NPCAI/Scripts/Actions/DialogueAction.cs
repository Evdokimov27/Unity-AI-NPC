using System;
using UnityEngine;

[DisallowMultipleComponent]
public class DialogueAction : MonoBehaviour, IActionStep
{
	public string StepName => "Dialogue";
	public NPCDialogueManager dialogueManager;

	void Reset()
	{
		if (!dialogueManager)
			dialogueManager = GetComponent<NPCDialogueManager>() ?? GetComponentInParent<NPCDialogueManager>();
	}

	public void Begin(ActionContext context, Action<bool> onComplete)
	{
		var mgr = dialogueManager ?? (GetComponent<NPCDialogueManager>() ?? GetComponentInParent<NPCDialogueManager>());
		if (!mgr) { onComplete?.Invoke(false); return; }

		string text = context?.dialogueText;
		if (string.IsNullOrWhiteSpace(text)) text = context?.userCommand ?? "";
		mgr.SendDialogue(text);
		onComplete?.Invoke(true); 
	}

	public void Tick(ActionContext context) { }
	public void Cancel(ActionContext context) { }
}
