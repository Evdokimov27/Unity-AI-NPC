using System.Text.RegularExpressions;
using UnityEngine;

[DisallowMultipleComponent]
public class NPCActionCommentator : MonoBehaviour
{
	[SerializeField] private NPCDialogueManager dialogueManager;

	public void CommentOnAction(string actionName, GameObject target, string userCommand)
	{
		if (!dialogueManager || !dialogueManager.npc || !target) return;

		bool ru = ContainsCyrillic(userCommand);

		string contextLine = $"{actionName} :: {target.name}";

		string systemPrompt =
$@"You are an NPC performing an action. Follow these rules strictly:
• One short sentence, present tense, first person.
• Describe what I am doing to the TARGET, not what the player does; no observations or meta (“I see/observe”, “I am going to”, “I will”, “as an NPC”, etc.).
• Be concrete and purposeful (include how/why if it fits in one short sentence).
• If action == Movement: ignore the user's command and briefly state my movement intention (one short sentence).
• Never ask questions. No quotes. No emojis.";

		string fewShot =
@"Bad: ""I watch you interact with the table.""
Good: ""Stacking the books in alphabetical order.""

Bad: ""I observe that you opened the door.""
Good: ""Closing the door softly.""

Bad: ""Я наблюдаю, как ты двигаешь стул.""
Good: ""Ставлю стул к рабочему столу.""
";

		string userPrompt =
$@"Action: {actionName}
Target: {PrettifyName(target.name)}
NPC mood: {dialogueManager.npc.mood}
NPC backstory: {Safe(dialogueManager.npc.backstory)}
User command: {(actionName == "Movement" ? "<ignore>" : userCommand)}

Examples:
{fewShot}

Answer:";

		dialogueManager.ClientAsk(systemPrompt, userPrompt, reply =>
		{
			string clean = PostProcess(reply, ru);
			Debug.Log(clean);
		});
	}


	static bool ContainsCyrillic(string s) => !string.IsNullOrEmpty(s) && Regex.IsMatch(s, "[\\p{IsCyrillic}]");

	static string PrettifyName(string s)
	{
		if (string.IsNullOrWhiteSpace(s)) return "object";
		s = Regex.Replace(s, "\\(Clone\\)$", "");
		s = s.Replace('_', ' ').Trim();
		s = Regex.Replace(s, "\\s{2,}", " ");
		return s;
	}

	static string Safe(string s) => string.IsNullOrWhiteSpace(s) ? "<empty>" : s;

	static string PostProcess(string s, bool ru)
	{
		if (string.IsNullOrWhiteSpace(s)) return ru ? "Выполняю действие." : "Performing the action.";

		s = s.Trim();
		if (s.Length > 2 && ((s.StartsWith("\"") && s.EndsWith("\"")) || (s.StartsWith("“") && s.EndsWith("”"))))
			s = s.Substring(1, s.Length - 2);
		s = s.Trim();

		string[] badStarts = ru
			? new[] { "я наблюдаю", "наблюдаю", "я вижу", "вижу", "я собираюсь", "собираюсь", "как npc", "как персонаж" }
			: new[] { "i observe", "i see", "observing", "watching", "i am going to", "as an npc", "as a character" };

		string lower = s.ToLowerInvariant();
		foreach (var bad in badStarts)
		{
			if (lower.StartsWith(bad))
			{
				s = Regex.Replace(s, $"^{Regex.Escape(bad)}[:,]?\\s*", "", RegexOptions.IgnoreCase).Trim();
				break;
			}
		}

		if (ru)
		{
			s = Regex.Replace(s, @"\b(наблюдаю|смотрю|вижу)\b.*", "", RegexOptions.IgnoreCase).Trim();
			if (string.IsNullOrEmpty(s)) s = "Делаю это.";
		}
		else
		{
			s = Regex.Replace(s, @"\b(I\s*(?:watch|observe|see))\b.*", "", RegexOptions.IgnoreCase).Trim();
			if (string.IsNullOrEmpty(s)) s = "Doing it.";
		}

		s = s.Replace("\n", " ").Replace("  ", " ").Trim();
		int dot = s.IndexOfAny(new[] { '.', '!', '?' });
		if (dot > 0) s = s.Substring(0, dot + 1).Trim(); 
		if (!Regex.IsMatch(s, "[.!?]$")) s += ".";

		if (s.Length > 140) s = s.Substring(0, 140).TrimEnd() + "...";

		return s;
	}
}
