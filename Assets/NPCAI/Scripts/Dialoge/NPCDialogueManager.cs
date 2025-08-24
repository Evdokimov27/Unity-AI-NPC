using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class NPCDialogueManager : MonoBehaviour
{
	// ===== LLM / Presentation =====
	[Header("LLM & Presentation")]
	[SerializeField] private ChatGPTClient client;
	[SerializeField] public NPCProfile npc;
	public NPCSpeechBubble _bubble;

	// ===== Proximity Chat =====
	[Header("Proximity Chat")]
	public bool enableProximityDialogue = true;
	[Min(0.5f)] public float chatRange = 3.0f;
	[Min(0.5f)] public float chatCooldown = 20f;
	[Range(0f, 1f)] public float chatStartChance = 0.6f; // при 1.0f разговор стартует всегда
	public LayerMask npcLayer = ~0;

	// ===== Dialogue Flow =====
	[Header("Dialogue Flow")]
	public int minTurns = 3;
	public int maxTurns = 6;
	[Min(0.3f)] public float minTurnDuration = 1.1f;
	[Min(0.5f)] public float maxTurnDuration = 4.0f;
	[Min(0f)] public float secondsPerCharacter = 0.045f;
	[Min(0.5f)] public float askTimeout = 3.0f;
	[Min(30f)] public float faceTurnSpeed = 360f;

	// ===== Movement control during chat =====
	[Header("Movement control during chat")]
	[Tooltip("Если включено, NPC временно перестанут двигаться (Wander/NavMeshAgent) на время диалога.")]
	public bool agentStopDuringChat = true;

	// ===== Content =====
	[Header("Content")]
	public List<string> dialogTopics = new List<string> { "weather", "city", "work", "food", "music", "news", "sports" };
	public List<string> farewellPhrases = new List<string> { "See you!", "Bye!", "Catch you later.", "Have a good day!" };

	// ===== Runtime state =====
	readonly Collider[] _overlap = new Collider[16];
	float _lastChatTime = -999f;
	bool _inDialogue;

	// own movement refs
	NPCWander _wander;
	UnityEngine.AI.NavMeshAgent _agent;

	// partner refs
	NPCDialogueManager _currentPartner;
	NPCWander _partnerWander;
	UnityEngine.AI.NavMeshAgent _partnerAgent;

	// movement pause state
	bool _disabledWander = false;
	bool _partnerDisabledWander = false;
	bool _myPrevStopped = false;
	bool _partnerPrevStopped = false;

	void Awake()
	{
		// Ищем по всему корню NPC, чтобы достать соседние компоненты
		var root = transform.root;
		_wander = root.GetComponentInChildren<NPCWander>(true);
		_agent = root.GetComponentInChildren<UnityEngine.AI.NavMeshAgent>(true);
	}

	void Update()
	{
		if (enableProximityDialogue && !_inDialogue)
			TryFindPartnerAndChat();
	}

	// ===== Public API =====

	public void ShowBubble(string line)
	{
		if (_bubble != null) _bubble.ShowText(line);
	}

	public void SendDialogue(string playerQuestion)
	{
		if (!client || !npc)
		{
			Debug.LogError("NPCDialogueManager: Missing ChatGPTClient or NPCProfile.");
			return;
		}

		string description =
			$"Name: {npc.npcName}\n" +
			$"Mood: {npc.mood}\n" +
			$"Backstory: {(string.IsNullOrEmpty(npc.backstory) ? "<empty>" : npc.backstory)}";

		string systemPrompt =
			"You are roleplaying as an NPC in a video game. " +
			"Always stay in character and answer like a person would. " +
			"No more than 3 sentences.";

		string userPrompt = description + "\n\nPlayer: " + playerQuestion;

		client.Ask(systemPrompt, userPrompt, (reply) =>
		{
			ShowBubble(reply);
		});
	}

	public void ClientAsk(string systemPrompt, string userPrompt, System.Action<string> onReply)
	{
		if (!client || !npc) { onReply?.Invoke(null); return; }
		client.Ask(systemPrompt, userPrompt, reply => onReply?.Invoke(reply));
	}

	/// <summary>Принудительно завершает текущий proximity‑диалог и возобновляет движение у обоих собеседников.</summary>
	public void ForceStopDialogue()
	{
		if (!_inDialogue) return;

		StopAllCoroutines();
		_inDialogue = false;
		_lastChatTime = Time.time;
		if (_bubble) _bubble.ShowText("");

		// резюмим движение
		ResumeMovementSelf();
		ResumeMovementPartner();

		_currentPartner = null;
		_partnerWander = null;
		_partnerAgent = null;
	}

	// ===== Proximity logic (перенесено из Wander) =====

	void TryFindPartnerAndChat()
	{
		if (Time.time - _lastChatTime < chatCooldown) return;

		int count = Physics.OverlapSphereNonAlloc(transform.position, chatRange, _overlap, npcLayer, QueryTriggerInteraction.Ignore);
		if (count <= 0) return;

		for (int i = 0; i < count; i++)
		{
			var col = _overlap[i];
			if (!col) continue;

			var other = col.GetComponent<NPCDialogueManager>()
						?? col.GetComponentInParent<NPCDialogueManager>()
						?? col.GetComponentInChildren<NPCDialogueManager>();
			if (!other || other == this) continue;

			if (!other.enableProximityDialogue) continue;
			if (other._inDialogue) continue;
			if (Time.time - other._lastChatTime < other.chatCooldown) continue;

			// Если шанс = 1, всегда стартуем. Иначе — обычная проверка.
			if (chatStartChance < 1f && Random.value > Mathf.Clamp01(chatStartChance)) continue;

			StartCoroutine(RunDialogueWith(other));
			return;
		}
	}

	IEnumerator RunDialogueWith(NPCDialogueManager partner)
	{
		// пометим состояние
		_inDialogue = true;
		partner._inDialogue = true;
		_lastChatTime = Time.time;
		partner._lastChatTime = Time.time;

		// сохраним ссылки (по корням), чтобы уметь замораживать движение
		_currentPartner = partner;
		EnsureSelfRefs();
		EnsurePartnerRefs();

		// остановим движение на время разговора, если надо
		if (agentStopDuringChat)
		{
			PauseMovementSelf();
			PauseMovementPartner();
		}

		string topic = (dialogTopics != null && dialogTopics.Count > 0)
			? dialogTopics[Random.Range(0, dialogTopics.Count)]
			: "life";

		int turns = Mathf.Max(2, Random.Range(Mathf.Min(minTurns, maxTurns), Mathf.Max(minTurns, maxTurns) + 1));
		string sys = "You are NPCs chatting. Keep replies short (1–2 sentences), casual, and coherent with the partner's last line.";

		string lastA = null;
		string lastB = null;

		// A=this, B=partner
		for (int turn = 0; turn < turns; turn++)
		{
			bool speakerIsA = (turn % 2 == 0);
			if (speakerIsA)
			{
				string userPrompt = (turn == 0)
					? $"Greet the other NPC and ask a short, specific question about {topic}. End with a question."
					: $"Ask a short follow-up question about {topic} referencing the partner's last line: \"{TrimQuote(lastB, 80)}\". End with a question.";

				bool done = false; lastA = null;
				ClientAsk(sys, userPrompt, r => { lastA = string.IsNullOrWhiteSpace(r) ? "…" : r; done = true; });
				while (!done) yield return null;
				ShowBubble(lastA);
			}
			else
			{
				string userPrompt = $"Briefly answer about {topic} to \"{TrimQuote(lastA, 80)}\". One or two sentences.";
				bool done = false; lastB = null;
				partner.ClientAsk(sys, userPrompt, r => { lastB = string.IsNullOrWhiteSpace(r) ? "…" : r; done = true; });
				while (!done) yield return null;
				partner.ShowBubble(lastB);
			}

			

			float dur = Mathf.Clamp(((speakerIsA ? lastA : lastB)?.Length ?? 0) * secondsPerCharacter, minTurnDuration, maxTurnDuration);
			float end = Time.time + dur;
			while (Time.time < end)
			{
				FaceTowards(transform, partner.transform);
				FaceTowards(partner.transform, transform);
				yield return null;
			}
		}

		// прощальная реплика
		{
			bool lastIsA = ((turns - 1) % 2 == 0);
			var speaker = lastIsA ? this : partner;
			string sysBye = "You are an NPC. Say a short natural goodbye.";
			bool done = false; string bye = null;
			speaker.ClientAsk(sysBye, "Goodbye.", r => { bye = string.IsNullOrWhiteSpace(r) ? PickFarewell() : r; done = true; });
			while (!done) yield return null;
			speaker.ShowBubble(bye);
		}

		// резюмим движение
		if (agentStopDuringChat)
		{
			ResumeMovementSelf();
			ResumeMovementPartner();
		}

		_inDialogue = false;
		partner._inDialogue = false;

		_currentPartner = null;
		_partnerWander = null;
		_partnerAgent = null;
	}

	// ===== Movement helpers =====

	void EnsureSelfRefs()
	{
		if (_wander == null || _agent == null)
		{
			var root = transform.root;
			if (_wander == null) _wander = root.GetComponentInChildren<NPCWander>(true);
			if (_agent == null) _agent = root.GetComponentInChildren<UnityEngine.AI.NavMeshAgent>(true);
		}
	}

	void EnsurePartnerRefs()
	{
		if (_currentPartner == null) return;
		if (_partnerWander == null || _partnerAgent == null)
		{
			var proot = _currentPartner.transform.root;
			if (_partnerWander == null) _partnerWander = proot.GetComponentInChildren<NPCWander>(true);
			if (_partnerAgent == null) _partnerAgent = proot.GetComponentInChildren<UnityEngine.AI.NavMeshAgent>(true);
		}
	}

	void PauseMovementSelf()
	{
		if (_wander != null)
		{
			_wander.SetAutonomySuspended(true);
			if (_wander.enabled) { _wander.enabled = false; _disabledWander = true; }
		}

		if (_agent != null && _agent.enabled && _agent.isOnNavMesh)
		{
			_myPrevStopped = _agent.isStopped;
			_agent.isStopped = true;
			_agent.updateRotation = false;
			_agent.ResetPath();
		}
	}

	void ResumeMovementSelf()
	{
		if (_wander != null)
		{
			if (_disabledWander && !_wander.enabled) { _wander.enabled = true; _disabledWander = false; }
			_wander.SetAutonomySuspended(false);
		}

		if (_agent != null && _agent.enabled && _agent.isOnNavMesh)
		{
			_agent.isStopped = _myPrevStopped;
			_agent.updateRotation = true;

		}
	}

	void PauseMovementPartner()
	{
		if (_currentPartner == null) return;

		if (_partnerWander != null)
		{
			_partnerWander.SetAutonomySuspended(true);
			if (_partnerWander.enabled) { _partnerWander.enabled = false; _partnerDisabledWander = true; }
		}

		if (_partnerAgent != null && _partnerAgent.enabled && _partnerAgent.isOnNavMesh)
		{
			_partnerPrevStopped = _partnerAgent.isStopped;
			_partnerAgent.isStopped = true;
			_partnerAgent.updateRotation = false;

			_partnerAgent.ResetPath();
		}
	}

	void ResumeMovementPartner()
	{
		if (_currentPartner == null) return;

		if (_partnerWander != null)
		{
			if (_partnerDisabledWander && !_partnerWander.enabled) { _partnerWander.enabled = true; _partnerDisabledWander = false; }
			_partnerWander.SetAutonomySuspended(false);
		}

		if (_partnerAgent != null && _partnerAgent.enabled && _partnerAgent.isOnNavMesh)
		{
			_partnerAgent.isStopped = _partnerPrevStopped;
			_partnerAgent.updateRotation = true;

		}
	}

	// ===== Utils =====

	string TrimQuote(string s, int max)
	{
		if (string.IsNullOrEmpty(s)) return "";
		s = s.Replace("\n", " ").Trim();
		return s.Length <= max ? s : s.Substring(0, max) + "...";
	}

	string PickFarewell()
	{
		if (farewellPhrases != null && farewellPhrases.Count > 0)
			return farewellPhrases[Random.Range(0, farewellPhrases.Count)];
		return "Bye!";
	}

	void FaceTowards(Transform a, Transform b)
	{
		if (!a || !b) return;
		Vector3 d = b.position - a.position; d.y = 0f;
		if (d.sqrMagnitude < 0.0001f) return;
		Quaternion look = Quaternion.LookRotation(d.normalized, Vector3.up);
		a.rotation = Quaternion.RotateTowards(a.rotation, look, faceTurnSpeed * Time.deltaTime);
	}
}
