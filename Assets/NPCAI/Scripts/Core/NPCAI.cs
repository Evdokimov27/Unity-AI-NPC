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
	public enum HighLevelIntent { MoveOnly, MoveAndInteract, Dialogue, Wait, Follow }

	[Header("References")]
	public NavMeshAgent agent;

	public ResolveTargetAuto resolveStep;
	public WalkAction walkStep;
	public InteractAction interactStep;
	public WaitAction waitStep;
	public DialogueAction dialogueStep;
	public FollowAction followStep;

	public Animator animator;
	public string walkBool = "isWalking";
	public string talkBool = "isTalking";
	public string interactBool = "isInteracting";
	public string followBool = "isFollowing";
	public string waitBool = "isWaiting";
	public string wanderBool = "isWander";

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
	public KeywordSet followKeywords = new KeywordSet
	{
		entries = new[] {
			"follow","follow me","follow him","follow her","stick to","tail","shadow",
			"следуй","иди за","преследуй","следи за","держись рядом","иди за мной","за мной"
		},
		minSimilarity = 0.75f
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
	public NPCWander _wander;

	public HighLevelIntent CurrentIntent => _intent;
	public bool IsFollowing => _intent == HighLevelIntent.Follow;

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
		followStep = GetComponent<FollowAction>();
		_wander = GetComponent<NPCWander>();
	}

	private void Awake()
	{
		if (!_wander) _wander = GetComponent<NPCWander>();
		if (!followStep) followStep = GetComponent<FollowAction>();
	}

	private void Update()
	{
		if (_currentStep != null && _ctx != null)
			_currentStep.Tick(_ctx);

		UpdateMovementAnimation();
	}
	private void UpdateMovementAnimation()
	{
		if (!animator || agent == null || !agent.enabled || !agent.isOnNavMesh)
			return;

		bool isMoving = agent.velocity.sqrMagnitude > 0.01f && !agent.isStopped;

		SetBoolIfNotEmpty(walkBool, false);
		SetBoolIfNotEmpty(followBool, false);
		SetBoolIfNotEmpty(wanderBool, false);

		if (!isMoving)
		{
			if (verboseLogs) Debug.Log("Anim: agent NOT moving, all movement flags off.");
			return;
		}

		if (_currentStep is WalkAction)
		{
			if (verboseLogs) Debug.Log("Anim: WalkAction active → " + walkBool);
			SetBoolIfNotEmpty(walkBool, true);
		}
		else if (_currentStep is FollowAction)
		{
			if (verboseLogs) Debug.Log("Anim: FollowAction active → " + followBool);
			SetBoolIfNotEmpty(followBool, true);
		}
		else
		{
			if (_wander && _wander.isActiveAndEnabled)
			{
				if (!_wander.IsAutonomySuspended)
				{
					if (verboseLogs) Debug.Log("Anim: Wander active → " + wanderBool);
					SetBoolIfNotEmpty(wanderBool, true);
				}
				else
				{
					if (verboseLogs) Debug.Log("Anim: Wander found, but autonomy suspended.");
				}
			}
			else
			{
				if (verboseLogs) Debug.Log("Anim: No Wander running." + " " + _wander);
			}
		}
	}



	public void GoTo(string userCommand)
	{
		if (verboseLogs) Debug.Log($"NPCAI: start command — \"{userCommand}\"");

		StopAllCoroutines();
		if (_currentStep != null)
		{
			try { _currentStep.Cancel(_ctx); } catch { }
			_currentStep = null;
		}
		SuspendWander(true);
		try
		{
			if (talkDuringMove && talkDuringMove.enabled && talkDuringMove.mode == TalkAction.TalkMode.GenerateLoop)
				talkDuringMove.Cancel(_ctx);
			if (talkOnStartMove && talkOnStartMove.enabled)
				talkOnStartMove.Cancel(_ctx);
			if (talkOnFinish && talkOnFinish.enabled)
				talkOnFinish.Cancel(_ctx);
			if (dialogueManager) dialogueManager.ForceStopDialogue();
		}
		catch { }

		_ctx = new ActionContext(gameObject) { userCommand = userCommand ?? string.Empty };
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
			if (llm.HasValue) { _intent = llm.Value; gotLLM = true; if (verboseLogs) Debug.Log($"NPCAI: LLM intent -> {_intent} (wait={_ctx.waitSeconds:0.##}s)"); }
			else if (verboseLogs) Debug.Log("NPCAI: LLM intent failed/timeout. Using keyword fallback.");
		}
		if (!gotLLM)
		{
			_intent = DecideIntent(_ctx.userCommand, out float waitSec, out string dialogText);
			_ctx.waitSeconds = waitSec;
			_ctx.dialogueText = dialogText;
			if (verboseLogs) Debug.Log($"NPCAI: Fallback intent -> {_intent} (wait={_ctx.waitSeconds:0.##}s)");
		}

		if (_intent == HighLevelIntent.Dialogue && !allowDialogue)
		{ if (verboseLogs) Debug.Log("NPCAI: Dialogue blocked by Hub."); SuspendWander(false); yield break; }
		if ((_intent == HighLevelIntent.MoveOnly || _intent == HighLevelIntent.MoveAndInteract || _intent == HighLevelIntent.Follow) && !allowMovement)
		{ if (verboseLogs) Debug.Log("NPCAI: Movement blocked by Hub."); SuspendWander(false); yield break; }

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

		if (resolveStep)
		{
			bool ok = false;
			if (verboseLogs) Debug.Log("NPCAI: ResolveTargetAuto...");
			yield return RunStep(resolveStep, r => ok = r);

			if (taskMode == TaskMode.Auto)
			{
				bool? refined = DecideFromBlackboard(_ctx.blackboard, _ctx.userCommand);
				if (refined.HasValue && _intent != HighLevelIntent.Follow)
					_intent = refined.Value ? HighLevelIntent.MoveAndInteract : HighLevelIntent.MoveOnly;
			}

			if (!ok && stopOnFirstFail && _intent != HighLevelIntent.Follow)
			{
				if (verboseLogs) Debug.LogWarning("NPCAI: target not resolved — stopping.");
				SuspendWander(false); yield break;
			}
		}

		if (_intent == HighLevelIntent.Follow)
		{
			if (!followStep)
			{
				Debug.LogError("NPCAI: FollowAction missing.");
				SuspendWander(false); yield break;
			}

			bool ok = false; 
			if (verboseLogs) Debug.Log("NPCAI: FollowAction...");
			yield return RunStep(followStep, r => ok = r);
			SuspendWander(false); yield break;
		}

		if (!walkStep) { Debug.LogError("NPCAI: WalkAction not set."); SuspendWander(false); yield break; }
		bool needInteract = (_intent == HighLevelIntent.MoveAndInteract);

		if (_ctx.explicitTarget && commentator) commentator.CommentOnAction("Walk", _ctx.explicitTarget);
		if (talkOnStartMove) SafeBegin(talkOnStartMove, "TalkOnStartMove");
		if (talkDuringMove && talkDuringMove.mode == TalkAction.TalkMode.GenerateLoop) SafeBegin(talkDuringMove, "TalkDuringMove loop");

		bool walkOk = false;
		if (verboseLogs) Debug.Log("NPCAI: WalkAction...");
		yield return RunStep(walkStep, r => walkOk = r);

		if (talkDuringMove && talkDuringMove.mode == TalkAction.TalkMode.GenerateLoop) talkDuringMove.Cancel(_ctx);
		if (!walkOk && stopOnFirstFail)
		{
			if (verboseLogs) Debug.LogWarning("NPCAI: failed to reach the target.");
			SuspendWander(false); yield break;
		}

		if (needInteract)
		{
			if (!interactStep) { Debug.LogError("NPCAI: InteractAction missing."); SuspendWander(false); yield break; }
			if (_ctx.explicitTarget && commentator) commentator.CommentOnAction("Interact", _ctx.explicitTarget);
			bool interOk = false;
			if (verboseLogs) Debug.Log("NPCAI: InteractAction...");
			yield return RunStep(interactStep, r => interOk = r);
			if (!interOk && stopOnFirstFail)
			{
				if (verboseLogs) Debug.LogWarning("NPCAI: interaction failed.");
				SuspendWander(false); yield break;
			}
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
		ApplyAnimForStep(step);
		while (!finished) yield return null;
		_currentStep = null;
		if (animator) ResetAllAnimationFlags();

		onDone?.Invoke(result);
	}
	private void SafeBegin(IActionStep step, string label)
	{
		try
		{
			step.Begin(_ctx, _ => { });
			ApplyAnimForStep(step);
		}
		catch
		{
			Debug.LogWarning($"NPCAI: error while starting {label}");
		}
	}
	private void SetBoolIfNotEmpty(string param, bool value)
	{
		if (!animator) return;
		if (string.IsNullOrWhiteSpace(param)) return;
		for (int i = 0; i < animator.parameterCount; i++)
		{
			var p = animator.GetParameter(i);
			if (p.type == AnimatorControllerParameterType.Bool && p.name == param)
			{
				animator.SetBool(param, value);
				break;
			}
		}
	}

	private void ResetAllAnimationFlags()
	{
		SetBoolIfNotEmpty(walkBool, false);
		SetBoolIfNotEmpty(talkBool, false);
		SetBoolIfNotEmpty(interactBool, false);
		SetBoolIfNotEmpty(followBool, false);
		SetBoolIfNotEmpty(waitBool, false);
		SetBoolIfNotEmpty(wanderBool, false);
	}

	private void ApplyAnimForStep(IActionStep step)
	{
		if (!animator) return;

		SetBoolIfNotEmpty(talkBool, false);
		SetBoolIfNotEmpty(interactBool, false);
		SetBoolIfNotEmpty(waitBool, false);

		if (step is TalkAction) SetBoolIfNotEmpty(talkBool, true);
		else if (step is InteractAction) SetBoolIfNotEmpty(interactBool, true);
		else if (step is WaitAction) SetBoolIfNotEmpty(waitBool, true);
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

		if (followKeywords != null && followKeywords.Matches(text))
		{
			TryParseWaitSeconds(text, out waitSeconds); 
			return HighLevelIntent.Follow;
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
			"You are an intent classifier for NPC commands in a video game.\n" +
			"Return ONLY JSON with this schema:\n" +
			"{ \"intent\":\"move\"|\"interact\"|\"wait\"|\"dialogue\"|\"follow\", \"waitSeconds\":<number or 0> }\n" +
			"Rules:\n" +
			"- move = navigation only (e.g., 'иди к двери', 'go to X').\n" +
			"- interact = act on something (e.g., 'открой дверь', 'press the button').\n" +
			"- wait = pause/stand still (e.g., 'подожди 10 секунд', 'стой минуту'). Use waitSeconds.\n" +
			"- dialogue = general talk, questions, chat with the player.\n" +
			"- follow = follow a target (e.g., 'следуй за мной', 'follow the player'). If a duration is specified (e.g., 'follow me 30s'), put it into waitSeconds.\n" +
			"- Always output JSON only.";

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
					case "follow":
						_ctx.waitSeconds = Mathf.Max(0f, waitSecs);
						result = HighLevelIntent.Follow; break;
					default: result = null; break;
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
