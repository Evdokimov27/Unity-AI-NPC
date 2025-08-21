using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent]
public class NPCAI : MonoBehaviour
{
	public enum TaskMode { Auto, MoveOnly, MoveAndInteract }

	[Header("References")]
	public NavMeshAgent agent;
	public ResolveTargetAuto resolveStep;
	public WalkAction walkStep;
	public InteractAction interactStep;
	public NPCDialogueManager dialogueManager;
	public NPCActionCommentator commentator;
	public TalkAction talkOnStartMove;
	public TalkAction talkDuringMove;
	public TalkAction talkOnFinish;

	[Header("Intent")]
	public TaskMode taskMode = TaskMode.Auto;
	public KeywordSet movementKeywords = new KeywordSet
	{
		entries = new[] { "go to", "walk to", "approach", "move to", "come to", "head to" },
		minSimilarity = 0.78f
	};
	public KeywordSet interactionKeywords = new KeywordSet
	{
		entries = new[] { "open", "close", "press", "toggle", "switch", "use", "activate", "deactivate", "pull", "push", "talk", "speak" },
		minSimilarity = 0.78f
	};

	[Tooltip("If true and client is set, intent will be refined by LLM (any language).")]
	public bool useLLMIntent = true;
	public ChatGPTClient intentClient;
	public float llmTimeoutSeconds = 2.0f;

	[Header("Flow")]
	public bool stopOnFirstFail = true;
	public bool verboseLogs = true;

	private IActionStep _currentStep;
	private ActionContext _ctx;
	private bool _shouldInteract;

	private void Reset()
	{
		agent = GetComponent<NavMeshAgent>();
		walkStep = GetComponent<WalkAction>();
		resolveStep = GetComponent<ResolveTargetAuto>();
		interactStep = GetComponent<InteractAction>();
		commentator = GetComponent<NPCActionCommentator>();
		dialogueManager = GetComponent<NPCDialogueManager>();
	}

	private void Update()
	{
		if (_currentStep != null && _ctx != null) _currentStep.Tick(_ctx);
	}

	public void GoTo(string userCommand)
	{
		if (verboseLogs) Debug.Log($"NPCAI: start command — \"{userCommand}\"");

		_ctx = new ActionContext(gameObject);
		_ctx.userCommand = userCommand;

		_shouldInteract = DecideFromText(userCommand);
		if (verboseLogs) Debug.Log($"NPCAI: initial intent => {(_shouldInteract ? "MoveAndInteract" : "MoveOnly")}");

		StopAllCoroutines();
		StartCoroutine(RunPipeline());
	}

	private IEnumerator RunPipeline()
	{
		if (!agent) { Debug.LogError("NPCAI: NavMeshAgent not set."); yield break; }
		if (!walkStep) { Debug.LogError("NPCAI: WalkAction not set."); yield break; }
		if (_shouldInteract && !interactStep) { Debug.LogError("NPCAI: intent requires InteractAction, but it's not set."); yield break; }

		if (resolveStep)
		{
			if (verboseLogs) Debug.Log("NPCAI: ResolveTargetAuto...");
			bool ok = false;
			yield return RunStep(resolveStep, r => ok = r);
			if (!ok && stopOnFirstFail) { if (verboseLogs) Debug.LogWarning("NPCAI: target not resolved — stopping."); yield break; }

			if (taskMode == TaskMode.Auto)
			{
				bool? refinedByBB = DecideFromBlackboard(_ctx.blackboard, _ctx.userCommand);
				if (refinedByBB.HasValue) _shouldInteract = refinedByBB.Value;
				if (verboseLogs) Debug.Log($"NPCAI: refined by blackboard => {(_shouldInteract ? "MoveAndInteract" : "MoveOnly")}");

				if (useLLMIntent && intentClient)
				{
					bool? llm = null;
					yield return InferIntentLLM(_ctx.userCommand, _ctx.blackboard, v => llm = v);
					if (llm.HasValue) _shouldInteract = llm.Value;
					if (verboseLogs) Debug.Log($"NPCAI: refined by LLM => {(_shouldInteract ? "MoveAndInteract" : "MoveOnly")}");
				}

				if (_shouldInteract && !interactStep) { Debug.LogError("NPCAI: intent requires InteractAction, but it's not set."); yield break; }
			}
		}

		if (_ctx.explicitTarget && commentator) commentator.CommentOnAction("Walk", _ctx.explicitTarget);
		if (talkOnStartMove) SafeBegin(talkOnStartMove, "TalkOnStartMove");
		if (talkDuringMove && talkDuringMove.mode == TalkAction.TalkMode.GenerateLoop) SafeBegin(talkDuringMove, "TalkDuringMove loop");

		if (verboseLogs) Debug.Log("NPCAI: WalkAction...");
		{
			bool ok = false;
			yield return RunStep(walkStep, r => ok = r);
			if (talkDuringMove && talkDuringMove.mode == TalkAction.TalkMode.GenerateLoop) talkDuringMove.Cancel(_ctx);
			if (!ok && stopOnFirstFail) { if (verboseLogs) Debug.LogWarning("NPCAI: failed to reach the target."); yield break; }
		}

		if (_shouldInteract)
		{
			if (_ctx.explicitTarget && commentator) commentator.CommentOnAction("Interact", _ctx.explicitTarget);
			if (verboseLogs) Debug.Log("NPCAI: InteractAction...");
			bool ok = false;
			yield return RunStep(interactStep, r => ok = r);
			if (!ok && stopOnFirstFail) { if (verboseLogs) Debug.LogWarning("NPCAI: interaction failed."); yield break; }
			if (talkOnFinish) { bool finOk = false; yield return RunStep(talkOnFinish, r => finOk = r); }
		}
		else
		{
			if (verboseLogs) Debug.Log("NPCAI: intent = MoveOnly — skipping interaction.");
		}

		if (verboseLogs) Debug.Log("NPCAI: pipeline finished.");
	}

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
		catch { Debug.LogWarning($"NPCAI: error while starting {label}."); }
	}

	private bool DecideFromText(string userCommand)
	{
		if (taskMode == TaskMode.MoveOnly) return false;
		if (taskMode == TaskMode.MoveAndInteract) return true;

		var text = userCommand ?? string.Empty;
		bool hasMove = movementKeywords != null && movementKeywords.Matches(text);
		bool hasAct = interactionKeywords != null && interactionKeywords.Matches(text);

		if (hasAct && hasMove) return true;
		if (hasAct) return true;
		if (hasMove) return false;

		// default: if command looks like an imperative without movement words, assume interaction
		var norm = KeywordMatcher.Normalize(text);
		if (norm.Length > 0 && (norm.StartsWith("open ") || norm.StartsWith("use ") || norm.StartsWith("press "))) return true;

		return false;
	}

	private bool? DecideFromBlackboard(object blackboard, string fallbackText)
	{
		if (blackboard == null) return null;
		string a = GetFieldString(blackboard, "actionText");
		string c = GetFieldString(blackboard, "commandText");
		string combo = string.Join(" ", new[] { a, c, fallbackText }.Where(s => !string.IsNullOrWhiteSpace(s)));

		if (string.IsNullOrWhiteSpace(combo)) return null;

		bool hasMove = movementKeywords != null && movementKeywords.Matches(combo);
		bool hasAct = interactionKeywords != null && interactionKeywords.Matches(combo);

		if (hasAct && hasMove) return true;
		if (hasAct) return true;
		if (hasMove) return false;
		return null;
	}

	private IEnumerator InferIntentLLM(string userCommand, object blackboard, Action<bool?> onDone)
	{
		if (!intentClient) { onDone?.Invoke(null); yield break; }

		string a = GetFieldString(blackboard, "actionText");
		string c = GetFieldString(blackboard, "commandText");
		string payload = string.Join(" | ", new[] { userCommand, a, c }.Where(s => !string.IsNullOrWhiteSpace(s)));

		string systemPrompt =
			"You are an intent classifier for NPC commands. " +
			"Given a player's command in ANY language, return JSON ONLY with this schema: " +
			"{\"intent\":\"move\"|\"interact\"|\"both\"}. " +
			"Use \"move\" for pure navigation (e.g., 'go to the door'), " +
			"\"interact\" when the user requests an action on the target (e.g., 'open the door', 'press the button'), " +
			"and \"both\" if both are implied. Output JSON only.";
		string userPrompt = "Command: \"" + payload + "\"";

		bool done = false;
		bool? result = null;
		intentClient.Ask(systemPrompt, userPrompt, reply =>
		{
			try
			{
				var parsed = MiniParseIntent(reply);
				if (parsed == "interact" || parsed == "both") result = true;
				else if (parsed == "move") result = false;
			}
			catch { result = null; }
			done = true;
		});

		float t = 0f;
		while (!done && t < llmTimeoutSeconds) { t += Time.deltaTime; yield return null; }
		onDone?.Invoke(result);
	}

	private static string MiniParseIntent(string json)
	{
		if (string.IsNullOrEmpty(json)) return null;
		var s = json.ToLowerInvariant();
		int k = s.IndexOf("\"intent\"");
		if (k < 0) return null;
		int colon = s.IndexOf(':', k);
		if (colon < 0) return null;
		int q1 = s.IndexOf('"', colon + 1);
		if (q1 < 0) return null;
		int q2 = s.IndexOf('"', q1 + 1);
		if (q2 < 0) return null;
		return s.Substring(q1 + 1, q2 - q1 - 1).Trim();
	}

	private static string GetFieldString(object obj, string fieldName)
	{
		if (obj == null) return string.Empty;
		try
		{
			var f = obj.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (f == null) return string.Empty;
			var v = f.GetValue(obj);
			return v as string ?? v?.ToString() ?? string.Empty;
		}
		catch { return string.Empty; }
	}
}
