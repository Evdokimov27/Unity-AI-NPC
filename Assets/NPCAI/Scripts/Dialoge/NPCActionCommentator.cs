using UnityEngine;

[DisallowMultipleComponent]
public class NPCActionCommentator : MonoBehaviour
{
	[SerializeField] private NPCDialogueManager dialogueManager;

	public void CommentOnAction(string actionName, GameObject target)
	{
		if (!dialogueManager || !target) return;

		string contextLine = $"I am performing action: {actionName} towards {target.name}";

		string systemPrompt =
			"You are roleplaying as an NPC in a game. " +
			"Speak in character with one short sentence commenting on what you are doing.";

		string userPrompt = $"Context: {contextLine}\nNPC response:";

		dialogueManager.ClientAsk(systemPrompt, userPrompt, (reply) =>
		{
			Debug.Log($"{dialogueManager.npc.npcName}: {reply}");
		});
	}
}
