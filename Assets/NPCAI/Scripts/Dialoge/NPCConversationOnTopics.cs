using System.Collections;
using UnityEngine;

public class NPCConversationOnTopics : MonoBehaviour
{
	[Header("Topics (edit in inspector or code)")]
	[TextArea]
	public string[] topics = new string[]
	{
		"weather and harvest",
		"fishing at the northern lake",
		"rumors about the castle lord",
		"iron prices",
		"ancient ruins in the forest"
	};

	[Tooltip("If true, pick a random topic. Otherwise, iterate in order.")]
	public bool randomize = true;

	[Header("Turns")]
	[Min(1)] public int pairsOfExchanges = 2;
	[Tooltip("If true, choose initiator smartly (e.g., 'Talkative' mood wins); otherwise random.")]
	public bool preferTalkativeInitiator = true;

	[Header("Timing")]
	public float startDelay = 1.0f;

	public enum BeatMode { Fixed, PerWord, PerCharacter }
	public BeatMode beatMode = BeatMode.PerWord;

	[Tooltip("Used when BeatMode = Fixed.")]
	public float fixedBeat = 2.0f;

	[Tooltip("Seconds per word when BeatMode = PerWord.")]
	public float secondsPerWord = 0.30f;

	[Tooltip("Seconds per character when BeatMode = PerCharacter.")]
	public float secondsPerChar = 0.04f;

	[Tooltip("Clamp for min/max wait between lines.")]
	public float minBeat = 1.0f;
	public float maxBeat = 4.0f;

	[Tooltip("Ensure a line stays visible at least this long.")]
	public float minLineShowTime = 1.5f;

	[Header("Safety")]
	public float hardTimeout = 20f;

	int _lastIndex = -1;

	public void StartConversationFromEvent(MonoBehaviour a, MonoBehaviour b)
	{
		if (!a || !b) return;
		StopAllCoroutines();

		var ga = a.gameObject;
		var gb = b.gameObject;
		var (initiator, responder) = ChooseRoles(ga, gb);

		StartCoroutine(RunDialogue(initiator, responder));
	}

	(GameObject initiator, GameObject responder) ChooseRoles(GameObject ga, GameObject gb)
	{
		if (!preferTalkativeInitiator) return (Random.value < 0.5f ? ga : gb, (ga == gb ? ga : (Random.value < 0.5f ? gb : ga)));

		var pa = ga.GetComponent<NPCProfile>();
		var pb = gb.GetComponent<NPCProfile>();
		string ma = (pa?.mood ?? pa?.npcName ?? "").ToLowerInvariant();
		string mb = (pb?.mood ?? pb?.npcName ?? "").ToLowerInvariant();

		bool aTalk = ma.Contains("talk") || ma.Contains("leader") || ma.Contains("chatty") || ma.Contains("bold");
		bool bTalk = mb.Contains("talk") || mb.Contains("leader") || mb.Contains("chatty") || mb.Contains("bold");

		if (aTalk && !bTalk) return (ga, gb);
		if (bTalk && !aTalk) return (gb, ga);

		return (Random.value < 0.5f ? ga : gb, Random.value < 0.5f ? gb : ga);
	}

	IEnumerator RunDialogue(GameObject gInitiator, GameObject gResponder)
	{
		var dmI = gInitiator.GetComponent<NPCDialogueManager>();
		var dmR = gResponder.GetComponent<NPCDialogueManager>();
		if (!dmI || !dmR) yield break;

		var busyI = gInitiator.GetComponent<INPCBusy>();
		var busyR = gResponder.GetComponent<INPCBusy>();
		busyI?.SetBusy(true);
		busyR?.SetBusy(true);

		var wa = gInitiator.GetComponent<NPCWander>();
		var wb = gResponder.GetComponent<NPCWander>();
		wa?.OnDialogueStarted(gResponder.transform, 1f);
		wb?.OnDialogueStarted(gInitiator.transform, 1f);

		string topic = PickTopic();

		var pI = gInitiator.GetComponent<NPCProfile>();
		var pR = gResponder.GetComponent<NPCProfile>();

		string sysInitiator =
			"You are roleplaying as an NPC in a video game. " +
			$"Your name is {pI?.npcName ?? "Unknown"}. " +
			(!string.IsNullOrEmpty(pI?.mood) ? $"Your mood is {pI.mood}. " : "") +
			(!string.IsNullOrEmpty(pI?.backstory) ? $"Your backstory: {pI.backstory}. " : "") +
			"Speak in character with ONE short sentence. Lead the conversation strictly on the given topic. You may ask a brief question.";

		string sysResponder =
			"You are roleplaying as an NPC in a video game. " +
			$"Your name is {pR?.npcName ?? "Unknown"}. " +
			(!string.IsNullOrEmpty(pR?.mood) ? $"Your mood is {pR.mood}. " : "") +
			(!string.IsNullOrEmpty(pR?.backstory) ? $"Your backstory: {pR.backstory}. " : "") +
			"Speak in character with ONE short sentence. Support the conversation strictly on the given topic. React consistently with your personality. Do not change the topic.";

		float t0 = Time.time;
		yield return new WaitForSeconds(startDelay);

		string lastLine = null;

		// Initiator starts
		dmI.ClientAsk(sysInitiator,
			$"Conversation topic: {topic}.\nStart with one short line on this topic. NPC:",
			reply => { lastLine = reply;
				dmI.ShowBubble(reply);
			});

		yield return new WaitUntil(() => lastLine != null || Time.time - t0 > hardTimeout);
		if (lastLine == null) goto END;
		yield return new WaitForSeconds(ComputeBeat(lastLine));

		for (int i = 0; i < pairsOfExchanges && Time.time - t0 < hardTimeout; i++)
		{
			string partnerLine = lastLine; lastLine = null;
			dmR.ClientAsk(sysResponder,
				$"Topic: {topic}.\nPartner said: \"{partnerLine}\"\nReply in one short in-character sentence. NPC:",
				reply => { lastLine = reply;
					dmR.ShowBubble(reply);
				});
			yield return new WaitUntil(() => lastLine != null || Time.time - t0 > hardTimeout);
			if (lastLine == null) break;
			yield return new WaitForSeconds(ComputeBeat(lastLine));

			partnerLine = lastLine; lastLine = null;
			dmI.ClientAsk(sysInitiator,
				$"Topic: {topic}.\nPartner said: \"{partnerLine}\"\nReply in one short in-character sentence, keep leading. NPC:",
				reply => { lastLine = reply;
					dmI.ShowBubble(reply);
				});
			yield return new WaitUntil(() => lastLine != null || Time.time - t0 > hardTimeout);
			if (lastLine == null) break;
			yield return new WaitForSeconds(ComputeBeat(lastLine));
		}

	END:
		busyI?.SetBusy(false);
		busyR?.SetBusy(false);
		wa?.OnDialogueEnded();
		wb?.OnDialogueEnded();
	}



	string PickTopic()
	{
		if (topics == null || topics.Length == 0) return "small talk";
		if (randomize) return topics[Random.Range(0, topics.Length)];
		_lastIndex = (_lastIndex + 1) % topics.Length;
		return topics[_lastIndex];
	}

	float ComputeBeat(string line)
	{
		float wait = fixedBeat;

		if (beatMode == BeatMode.PerWord)
		{
			int words = string.IsNullOrWhiteSpace(line) ? 0 : line.Split(' ').Length;
			wait = secondsPerWord * Mathf.Max(1, words);
		}
		else if (beatMode == BeatMode.PerCharacter)
		{
			int chars = string.IsNullOrEmpty(line) ? 0 : line.Length;
			wait = secondsPerChar * Mathf.Max(1, chars);
		}

		wait = Mathf.Clamp(wait, minBeat, maxBeat);
		if (minLineShowTime > 0f) wait = Mathf.Max(wait, minLineShowTime);
		return wait;
	}
}
