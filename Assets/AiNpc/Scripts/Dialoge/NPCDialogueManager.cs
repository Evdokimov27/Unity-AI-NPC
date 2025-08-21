using UnityEngine;

public class NPCDialogueManager : MonoBehaviour
{
	[SerializeField] private ChatGPTClient client;
	[SerializeField] public NPCProfile npc;

	[TextArea][SerializeField] public string playerQuestion;

	public void SendDialogue(string playerQuestion)
	{
		if (!client || !npc)
		{
			Debug.LogError("Missing ChatGPTClient or NPCProfile.");
			return;
		}

		string description =
			$"The NPC has the following attributes:\n" +
			$"Name: {(string.IsNullOrEmpty(npc.npcName) ? "<empty>" : npc.npcName)}\n" +
			$"Mood: {(string.IsNullOrEmpty(npc.mood) ? "<empty>" : npc.mood)}\n" +
			$"Backstory: {(string.IsNullOrEmpty(npc.backstory) ? "<empty>" : npc.backstory)}\n\n" +
			"If any field is <empty>, roleplay a reason why this info is missing (make it part of the NPC's personality or story).";

		string systemPrompt =
			"You are roleplaying as an NPC in a video game. " +
			"Always stay in character and answer like a person would. " +
			"Keep answers short (1-3 sentences).";

		string userPrompt = description + "\n\nPlayer: " + playerQuestion;

		client.Ask(systemPrompt, userPrompt, (reply) =>
		{
			Debug.Log($"NPC: {reply}");
		});
	}
	public void ClientAsk(string systemPrompt, string userPrompt, System.Action<string> onReply)
	{
		if (!client || !npc) return;

		string description =
			$"Name: {npc.npcName}\n" +
			$"Mood: {npc.mood}\n" +
			$"Backstory: {(string.IsNullOrEmpty(npc.backstory) ? "<empty>" : npc.backstory)}\n";

		client.Ask(systemPrompt, description + "\n\n" + userPrompt, onReply);
	}


}
