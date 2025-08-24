using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent]
public class NPCAI : MonoBehaviour
{
	public enum TaskMode { Auto, MoveOnly, MoveAndInteract }
	public enum HighLevelIntent { MoveOnly, MoveAndInteract, Dialogue, Wait }

	[Header("References")]
	public NavMeshAgent agent;

	public ResolveTargetAuto resolveStep;
	public WalkAction walkStep;
	public InteractAction interactStep;
	public WaitAction waitStep;
	public DialogueAction dialogueStep;

	public NPCDialogueManager dialogueManager;
	public NPCActionCommentator commentator;
	public TalkAction talkOnStartMove;
	public TalkAction talkDuringMove;
	public TalkAction talkOnFinish;

	[Header("Intent")]
	public TaskMode taskMode = TaskMode.Auto;

	public KeywordSet movementKeywords = new KeywordSet
	{
		entries = new[] {
			"go to","walk to","approach","move to","come to","head to",
			"идти","подойди","подойти","приблизься","пройди","перейди","иди к","иди"
		},
		minSimilarity = 0.78f
	};
	public KeywordSet interactionKeywords = new KeywordSet
	{
		entries = new[] {
			"open","close","press","toggle","switch","use","activate","deactivate","pull","push",
			"открой","закрой","нажми","используй","активируй","деактивируй","потяни","толкни"
		},
		minSimilarity = 0.78f
	};
	public KeywordSet dialogueKeywords = new KeywordSet
	{
		entries = new[] { "talk", "speak", "chat", "расскажи", "поговорим", "ответь", "скажи", "объясни" },
		minSimilarity = 0.78f
	};
	public KeywordSet waitKeywords = new KeywordSet
	{
		entries = new[] { "wait", "hold", "stay", "stand", "подожди", "жди", "стой", "постой", "замри" },
		minSimilarity = 0.78f
	};

	public bool useLLMIntent = true;
	public ChatGPTClient intentClient;
	public float llmTimeoutSeconds = 2.0f;

	[Header("Flow")]
	public bool stopOnFirstFail = true;
	public bool verboseLogs = true;

	[Header("Permissions (controlled by NPCAIHub)")]
	public bool allowDialogue = true;
	public bool allowMovement = true;

	private IActionStep _currentStep;
	private ActionContext _ctx;
	private HighLevelIntent _intent;
	private NPCWander _wander;

	private void Reset()
	{
		agent = GetComponent<NavMeshAgent>();
		walkStep = GetComponent<WalkAction>();
		resolveStep = GetComponent<ResolveTargetAuto>();
		interactStep = GetComponent<InteractAction>();
		commentator = GetComponent<NPCActionCommentator>();
		dialogueManager = GetComponent<NPCDialogueManager>();
		waitStep = GetComponent<WaitAction>();
		dialogueStep = GetComponent<DialogueAction>();
		_wander = GetComponent<NPCWander>();
	}

	private void Awake()
	{
		if (!_wander) _wander = GetComponent<NPCWander>();
	}

	private void Update()
	{
		if (_currentStep != null && _ctx != null) _currentStep.Tick(_ctx);
	}

	public void GoTo(string userCommand)
	{
		if (_currentStep is DialogueAction || _currentStep is TalkAction)
		{
			try { _currentStep.Cancel(_ctx); } catch { }
			_currentStep = null;
		}

		if (dialogueManager != null)
		{
			dialogueManager.ForceStopDialogue();
		}
		if (verboseLogs) Debug.Log($"NPCAI: start command — \"{userCommand}\"");
		_ctx = new ActionContext(gameObject);
		_ctx.userCommand = userCommand ?? string.Empty;

		StopAllCoroutines();
		StartCoroutine(RunPipeline());
	}

	private void SuspendWander(bool v)
	{
		if (_wander) _wander.SetAutonomySuspended(v);
	}

	private IEnumerator RunPipeline()
	{
		SuspendWander(true);

		bool gotLLM = false;
		if (taskMode == TaskMode.Auto && useLLMIntent && intentClient)
		{
			if (verboseLogs) Debug.Log("NPCAI: waiting for LLM intent...");
			HighLevelIntent? llm = null;
			yield return InferIntentLLM(_ctx.userCommand, _ctx.blackboard, r => llm = r);
			if (llm.HasValue)
			{
				_intent = llm.Value;
				gotLLM = true;
				if (verboseLogs) Debug.Log($"NPCAI: LLM intent -> {_intent} (wait={GetWait():0.##}s)");
			}
			else if (verboseLogs) Debug.Log("NPCAI: LLM intent not available (timeout/empty). Using keyword fallback.");
		}

		if (!gotLLM)
		{
			_intent = DecideIntent(_ctx.userCommand, out float waitSec, out string dialogText);
			_ctx.waitSeconds = waitSec;
			_ctx.dialogueText = dialogText;
			if (verboseLogs) Debug.Log($"NPCAI: Fallback intent -> {_intent} (wait={_ctx.waitSeconds:0.##}s)");
		}

		if (_intent == HighLevelIntent.Dialogue && !allowDialogue)
		{
			if (verboseLogs) Debug.Log("NPCAI: Dialogue intent blocked by Hub (featureDialogue=false).");
			SuspendWander(false); yield break;
		}
		if ((_intent == HighLevelIntent.MoveOnly || _intent == HighLevelIntent.MoveAndInteract) && !allowMovement)
		{
			if (verboseLogs) Debug.Log("NPCAI: Movement intent blocked by Hub (featureMovement=false).");
			SuspendWander(false); yield break;
		}

		if (_intent == HighLevelIntent.Dialogue)
		{
			if (dialogueStep) { bool ok = false; yield return RunStep(dialogueStep, r => ok = r); }
			else if (dialogueManager) dialogueManager.SendDialogue(_ctx.dialogueText ?? _ctx.userCommand);
			SuspendWander(false); yield break;
		}

		if (_intent == HighLevelIntent.Wait)
		{
			if (waitStep) { bool ok = false; yield return RunStep(waitStep, r => ok = r); }
			else
			{
				var a = agent; if (a && a.enabled) { a.isStopped = true; a.ResetPath(); }
				float endAt = Time.time + Mathf.Max(0f, GetWait());
				while (Time.time < endAt) yield return null;
			}
			SuspendWander(false); yield break;
		}

		if (!agent) { Debug.LogError("NPCAI: NavMeshAgent not set."); SuspendWander(false); yield break; }
		if (!walkStep) { Debug.LogError("NPCAI: WalkAction not set."); SuspendWander(false); yield break; }

		bool needInteract = (_intent == HighLevelIntent.MoveAndInteract);
		if (needInteract && !interactStep) { Debug.LogError("NPCAI: InteractAction missing."); SuspendWander(false); yield break; }

		if (resolveStep)
		{
			bool ok = false;
			if (verboseLogs) Debug.Log("NPCAI: ResolveTargetAuto...");
			yield return RunStep(resolveStep, r => ok = r);

			if (taskMode == TaskMode.Auto)
			{
				bool? refined = DecideFromBlackboard(_ctx.blackboard, _ctx.userCommand);
				if (refined.HasValue) needInteract = refined.Value;
			}

			if (!ok && stopOnFirstFail) { if (verboseLogs) Debug.LogWarning("NPCAI: target not resolved — stopping."); SuspendWander(false); yield break; }
			if (needInteract && !interactStep) { Debug.LogError("NPCAI: InteractAction required but missing."); SuspendWander(false); yield break; }
		}

		if (_ctx.explicitTarget && commentator) commentator.CommentOnAction("Walk", _ctx.explicitTarget);
		if (talkOnStartMove) SafeBegin(talkOnStartMove, "TalkOnStartMove");
		if (talkDuringMove && talkDuringMove.mode == TalkAction.TalkMode.GenerateLoop) SafeBegin(talkDuringMove, "TalkDuringMove loop");

		bool walkOk = false;
		if (verboseLogs) Debug.Log("NPCAI: WalkAction...");
		yield return RunStep(walkStep, r => walkOk = r);

		if (talkDuringMove && talkDuringMove.mode == TalkAction.TalkMode.GenerateLoop) talkDuringMove.Cancel(_ctx);
		if (!walkOk && stopOnFirstFail) { if (verboseLogs) Debug.LogWarning("NPCAI: failed to reach the target."); SuspendWander(false); yield break; }

		if (needInteract)
		{
			if (_ctx.explicitTarget && commentator) commentator.CommentOnAction("Interact", _ctx.explicitTarget);
			bool interOk = false;
			if (verboseLogs) Debug.Log("NPCAI: InteractAction...");
			yield return RunStep(interactStep, r => interOk = r);
			if (!interOk && stopOnFirstFail) { if (verboseLogs) Debug.LogWarning("NPCAI: interaction failed."); SuspendWander(false); yield break; }
			if (talkOnFinish) { bool fin = false; yield return RunStep(talkOnFinish, r => fin = r); }
		}

		if (verboseLogs) Debug.Log("NPCAI: pipeline finished.");
		SuspendWander(false);
	}

	private float GetWait() => (_ctx != null && _ctx.waitSeconds > 0f) ? _ctx.waitSeconds : 3f;

	private IEnumerator RunStep(IActionStep step, Action<bool> onDone)
	{
		bool finished = false, result = false;
		_currentStep = step;
		step.Begin(_ctx, ok => { result = ok; finished = true; });
		while (!finished) yield return null;
		_currentStep = null;
		onDone?.Invoke(result);
	}

	private void SafeBegin(IActionStep step, string label)
	{
		try { step.Begin(_ctx, _ => { }); }
		catch { Debug.LogWarning($"NPCAI: error while starting {label}"); }
	}

	private HighLevelIntent DecideIntent(string text, out float waitSeconds, out string dialogText)
	{
		waitSeconds = 0f; dialogText = null;
		text = text ?? string.Empty;

		if (waitKeywords != null && waitKeywords.Matches(text))
		{
			if (!TryParseWaitSeconds(text, out waitSeconds)) waitSeconds = 3f;
			return HighLevelIntent.Wait;
		}

		bool hasMove = movementKeywords != null && movementKeywords.Matches(text);
		bool hasAct = interactionKeywords != null && interactionKeywords.Matches(text);
		bool isDialog = text.Contains("?") || (dialogueKeywords != null && dialogueKeywords.Matches(text));
		if (isDialog && !(hasMove || hasAct)) { dialogText = text; return HighLevelIntent.Dialogue; }

		if (taskMode == TaskMode.MoveOnly) return HighLevelIntent.MoveOnly;
		if (taskMode == TaskMode.MoveAndInteract) return HighLevelIntent.MoveAndInteract;

		if (hasAct && hasMove) return HighLevelIntent.MoveAndInteract;
		if (hasAct) return HighLevelIntent.MoveAndInteract;
		if (hasMove) return HighLevelIntent.MoveOnly;

		var norm = KeywordMatcher.Normalize(text);
		if (norm.StartsWith("open ") || norm.StartsWith("use ") || norm.StartsWith("press ") ||
			norm.StartsWith("открой ") || norm.StartsWith("нажми ") || norm.StartsWith("используй "))
			return HighLevelIntent.MoveAndInteract;

		if (norm.StartsWith("иди ") || norm.StartsWith("иди к ")) return HighLevelIntent.MoveOnly;

		if (isDialog) { dialogText = text; return HighLevelIntent.Dialogue; }
		return HighLevelIntent.MoveOnly;
	}

	private bool TryParseWaitSeconds(string text, out float seconds)
	{
		seconds = 0f;
		if (string.IsNullOrWhiteSpace(text)) return false;
		string norm = KeywordMatcher.Normalize(text);

		var mSec = Regex.Match(norm, @"\b(\d{1,4})\s*(s|sec|second|sek|сек|секунд|секунды|секунду|c)\b");
		if (mSec.Success && int.TryParse(mSec.Groups[1].Value, out int vSec)) { seconds = Mathf.Max(0, vSec); return true; }

		var mMin = Regex.Match(norm, @"\b(\d{1,3})\s*(m|min|minute|мин|минут|минуту|минуты)\b");
		if (mMin.Success && int.TryParse(mMin.Groups[1].Value, out int vMin)) { seconds = Mathf.Max(0, vMin * 60); return true; }

		if (Regex.IsMatch(norm, @"\bминут(у|ы)?\b")) { seconds = 60f; return true; }
		if (Regex.IsMatch(norm, @"\bполминут(ы|ы)?\b")) { seconds = 30f; return true; }

		var lone = Regex.Match(norm, @"\b(\d{1,4})\b");
		if (lone.Success && int.TryParse(lone.Groups[1].Value, out int vv)) { seconds = vv; return true; }

		return false;
	}

	private bool? DecideFromBlackboard(object bb, string fallback)
	{
		if (bb == null) return null;
		string a = GetFieldString(bb, "actionText");
		string c = GetFieldString(bb, "commandText");
		string combo = string.Join(" ", new[] { a, c, fallback }.Where(s => !string.IsNullOrWhiteSpace(s)));
		bool hasMove = movementKeywords != null && movementKeywords.Matches(combo);
		bool hasAct = interactionKeywords != null && interactionKeywords.Matches(combo);
		if (hasAct) return true;
		if (hasMove) return false;
		return null;
	}

	private IEnumerator InferIntentLLM(string userCommand, object blackboard, Action<HighLevelIntent?> onDone)
	{
		if (!intentClient) { onDone?.Invoke(null); yield break; }

		string a = GetFieldString(blackboard, "actionText");
		string c = GetFieldString(blackboard, "commandText");
		string payload = string.Join(" | ", new[] { userCommand, a, c }.Where(s => !string.IsNullOrWhiteSpace(s)));

		string systemPrompt =
			"You are an intent classifier for NPC commands in a video game. " +
			"Classify the player's input (any language) into EXACTLY one intent. " +
			"Respond ONLY with JSON in this schema:\n" +
			"{ \"intent\":\"move\"|\"interact\"|\"wait\"|\"dialogue\", \"waitSeconds\":<number or 0> }\n" +
			"Rules:\n" +
			"- 'move' = navigation only (e.g., 'иди к двери', 'go to the circle').\n" +
			"- 'interact' = act on something (e.g., 'открой дверь', 'press the button').\n" +
			"- 'wait' = commands to pause/stand still (e.g., 'подожди 10 секунд', 'стой минуту'). Fill waitSeconds if given.\n" +
			"- 'dialogue' = questions or talking to the player.\n" +
			"- Always output JSON only.\n\n" +
			"Examples:\n" +
			"Player: \"иди к двери\" → {\"intent\":\"move\",\"waitSeconds\":0}\n" +
			"Player: \"открой дверь\" → {\"intent\":\"interact\",\"waitSeconds\":0}\n" +
			"Player: \"подожди 10 секунд\" → {\"intent\":\"wait\",\"waitSeconds\":10}\n" +
			"Player: \"стой минуту\" → {\"intent\":\"wait\",\"waitSeconds\":60}\n" +
			"Player: \"поговорим?\" → {\"intent\":\"dialogue\",\"waitSeconds\":0}\n";

		string userPrompt = "Command: \"" + payload + "\"";

		bool done = false;
		HighLevelIntent? result = null;

		intentClient.Ask(systemPrompt, userPrompt, reply =>
		{
			try
			{
				var parsed = MiniParseIntentExtended(reply, out float waitSecs);
				switch (parsed)
				{
					case "move": result = HighLevelIntent.MoveOnly; break;
					case "interact": result = HighLevelIntent.MoveAndInteract; break;
					case "wait":
						_ctx.waitSeconds = waitSecs > 0 ? waitSecs : 3f;
						result = HighLevelIntent.Wait;
						break;
					case "dialogue": result = HighLevelIntent.Dialogue; break;
				}
			}
			catch { result = null; }
			done = true;
		});

		float t = 0f;
		while (!done && t < llmTimeoutSeconds) { t += Time.deltaTime; yield return null; }
		onDone?.Invoke(result);
	}

	private static string MiniParseIntentExtended(string json, out float waitSeconds)
	{
		waitSeconds = 0f;
		if (string.IsNullOrEmpty(json)) return null;

		var s = json.ToLowerInvariant();
		string intent = ExtractJsonValue(s, "intent");
		string waitStr = ExtractJsonValue(s, "waitseconds");
		if (!string.IsNullOrEmpty(waitStr))
		{
			if (float.TryParse(waitStr, System.Globalization.NumberStyles.Float,
				System.Globalization.CultureInfo.InvariantCulture, out float w))
				waitSeconds = w;
		}
		return intent;
	}

	private static string ExtractJsonValue(string text, string key)
	{
		int k = text.IndexOf("\"" + key.ToLower() + "\"");
		if (k < 0) return null;
		int colon = text.IndexOf(':', k);
		if (colon < 0) return null;

		int start = colon + 1;
		while (start < text.Length && (text[start] == ' ' || text[start] == '\"')) start++;

		int end = start;
		while (end < text.Length && text[end] != '\"' && text[end] != ',' && text[end] != '}') end++;

		return text.Substring(start, end - start).Trim();
	}

	private static string GetFieldString(object obj, string field)
	{
		if (obj == null) return "";
		try
		{
			var f = obj.GetType().GetField(field, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			var v = f?.GetValue(obj);
			return v as string ?? v?.ToString() ?? "";
		}
		catch { return ""; }
	}
}
