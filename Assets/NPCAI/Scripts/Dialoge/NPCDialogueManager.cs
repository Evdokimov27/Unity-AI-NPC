using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class NPCDialogueManager : MonoBehaviour
{
	[Header("LLM & Presentation")]
	[SerializeField] private MultiAIClient client;
	[SerializeField] public NPCProfile npc;
	public NPCSpeechBubble _bubble;

	[Header("Proximity Chat")]
	public bool enableProximityDialogue = true;
	[Min(0.5f)] public float chatRange = 3.0f;
	[Min(0.5f)] public float chatCooldown = 20f;
	[Range(0f, 1f)] public float chatStartChance = 0.6f;
	public LayerMask npcLayer = ~0;

	[Header("Dialogue Flow")]
	public int minTurns = 3;
	public int maxTurns = 6;
	[Min(0.3f)] public float minTurnDuration = 1.1f;
	[Min(0.5f)] public float maxTurnDuration = 4.0f;
	[Min(0f)] public float secondsPerCharacter = 0.045f;
	[Min(0.5f)] public float askTimeout = 3.0f;
	[Min(30f)] public float faceTurnSpeed = 360f;

	[Header("Movement control during chat")]
	[Tooltip("If enabled, NPCs will temporarily stop moving (Wander/NavMeshAgent) for the duration of the dialogue.")]
	public bool agentStopDuringChat = true;

	[Header("Content")]
	public List<string> dialogTopics = new List<string> { "weather", "city", "work", "food", "music", "news", "sports" };
	public List<string> farewellPhrases = new List<string> { "See you!", "Bye!", "Catch you later.", "Have a good day!" };

	[HideInInspector] public Animator animator;
	[HideInInspector] public string talkBool = "isTalking";

	readonly Collider[] _overlap = new Collider[16];
	float _lastChatTime = -999f;
	bool _inDialogue;

	NPCWander _wander;
	UnityEngine.AI.NavMeshAgent _agent;

	NPCDialogueManager _currentPartner;
	NPCWander _partnerWander;
	UnityEngine.AI.NavMeshAgent _partnerAgent;

	bool _disabledWander = false;
	bool _partnerDisabledWander = false;
	bool _myPrevStopped = false;
	bool _partnerPrevStopped = false;

	bool _hadPath;
	Vector3 _savedDestination;

	bool _partnerHadPath;
	Vector3 _partnerSavedDestination;

	bool _wanderAutonomyHeldByDialogue = false;
	bool _partnerAutonomyHeldByDialogue = false;

	public bool IsInDialogue => _inDialogue;

	public event System.Action<NPCDialogueManager> DialogueStarted;
	public event System.Action<NPCDialogueManager> DialogueFinished;

	void Awake()
	{
		var root = transform.root;
		_wander = root.GetComponentInChildren<NPCWander>(true);
		_agent = root.GetComponentInChildren<UnityEngine.AI.NavMeshAgent>(true);
	}

	void Update()
	{
		if (enableProximityDialogue && !_inDialogue)
			TryFindPartnerAndChat();
	}

	public void ShowBubble(string line)
	{
		if (_bubble != null) _bubble.ShowText(line);
	}


	string BuildPersona(NPCProfile p)
	{
		if (p == null) return "Name: Unknown\nMood: neutral\nBackstory: ";
		return
			$"Name: {p.npcName}\n" +
			$"Mood: {p.mood}\n" +
			$"Backstory: {(string.IsNullOrEmpty(p.backstory) ? "<empty>" : p.backstory)}";
	}

	string BuildPairSystem(NPCProfile a, NPCProfile b)
	{
		return
			"You are two NPCs in a video game having a short, natural conversation. " +
			"Always stay in character based on the provided persona. " +
			"Keep replies concise (1–2 sentences).\n\n" +
			$"Persona A:\n{BuildPersona(a)}\n\n" +
			$"Persona B:\n{BuildPersona(b)}\n";
	}

	void AskWithPersona(MultiAIClient cli, NPCProfile persona, string systemBase, string userPrompt, System.Action<string> onReply)
	{
		if (cli == null) { onReply?.Invoke(null); return; }
		string user = $"Persona:\n{BuildPersona(persona)}\n\n{userPrompt}";
		cli.Ask(systemBase, user, onReply);
	}


	public void SendDialogue(string playerQuestion)
	{
		if (!client || !npc)
		{
			Debug.LogError("NPCDialogueManager: Missing MultiAIClient or NPCProfile.");
			return;
		}

		if (animator && !string.IsNullOrWhiteSpace(talkBool))
			animator.SetBool(talkBool, true);

		string systemPrompt =
			"You are roleplaying as an NPC in a video game. " +
			"Always stay in character and answer like a person would. " +
			"No more than 3 sentences.";

		string userPrompt = $"Persona:\n{BuildPersona(npc)}\n\nPlayer: {playerQuestion}";

		client.Ask(systemPrompt, userPrompt, (reply) =>
		{
			ShowBubble(reply);
			if (animator && !string.IsNullOrWhiteSpace(talkBool))
				animator.SetBool(talkBool, false);
		});
	}

	public void ClientAsk(string systemPrompt, string userPrompt, System.Action<string> onReply)
	{
		if (!client) { onReply?.Invoke(null); return; }

		if (animator && !string.IsNullOrWhiteSpace(talkBool))
			animator.SetBool(talkBool, true);

		client.Ask(systemPrompt, userPrompt, reply =>
		{
			onReply?.Invoke(reply);
			if (animator && !string.IsNullOrWhiteSpace(talkBool))
				animator.SetBool(talkBool, false);
		});
	}

	public void ForceStopDialogue()
	{
		if (!_inDialogue) return;

		StopAllCoroutines();
		_inDialogue = false;
		_lastChatTime = Time.time;
		if (_bubble) _bubble.ShowText("");

		if (animator && !string.IsNullOrWhiteSpace(talkBool))
			animator.SetBool(talkBool, false);

		ResumeMovementSelf();
		ResumeMovementPartner();

		DialogueFinished?.Invoke(this);

		_currentPartner = null;
		_partnerWander = null;
		_partnerAgent = null;
	}


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

			if (!client || npc == null) continue;
			if (!other.client || other.npc == null) continue;

			if (chatStartChance < 1f && Random.value > Mathf.Clamp01(chatStartChance)) continue;

			StartCoroutine(RunDialogueWith(other));
			return;
		}
	}


	IEnumerator RunDialogueWith(NPCDialogueManager partner)
	{
		_inDialogue = true;
		partner._inDialogue = true;
		_lastChatTime = Time.time;
		partner._lastChatTime = Time.time;

		DialogueStarted?.Invoke(this);
		partner.DialogueStarted?.Invoke(partner);

		_currentPartner = partner;
		EnsureSelfRefs();
		EnsurePartnerRefs();

		if (agentStopDuringChat)
		{
			PauseMovementSelf();
			PauseMovementPartner();
		}

		if (animator && !string.IsNullOrWhiteSpace(talkBool))
			animator.SetBool(talkBool, true);
		if (partner.animator && !string.IsNullOrWhiteSpace(partner.talkBool))
			partner.animator.SetBool(partner.talkBool, true);

		string topic = (dialogTopics != null && dialogTopics.Count > 0)
			? dialogTopics[Random.Range(0, dialogTopics.Count)]
			: "life";

		int turns = Mathf.Max(2, Random.Range(Mathf.Min(minTurns, maxTurns), Mathf.Max(minTurns, maxTurns) + 1));

		string sysPair = BuildPairSystem(this.npc, partner.npc);

		string lastA = null;
		string lastB = null;

		// A=this, B=partner
		for (int turn = 0; turn < turns; turn++)
		{
			bool speakerIsA = (turn % 2 == 0);
			if (speakerIsA)
			{
				string userPrompt = (turn == 0)
					? $"As Persona A, greet Persona B and ask a short, specific question about {topic}. End with a question."
					: $"As Persona A, ask a brief follow-up about {topic}, referencing Persona B's last line: \"{TrimQuote(lastB, 80)}\". End with a question.";

				bool done = false; lastA = null;
				AskWithPersona(this.client, this.npc, sysPair, userPrompt, r => { lastA = string.IsNullOrWhiteSpace(r) ? "…" : r; done = true; });
				while (!done) yield return null;
				ShowBubble(lastA);
			}
			else
			{
				string userPrompt =
					$"As Persona B, briefly answer about {topic} to Persona A's line: \"{TrimQuote(lastA, 80)}\". One or two sentences.";
				bool done = false; lastB = null;
				AskWithPersona(partner.client, partner.npc, sysPair, userPrompt, r => { lastB = string.IsNullOrWhiteSpace(r) ? "…" : r; done = true; });
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

		{
			bool lastIsA = ((turns - 1) % 2 == 0);
			var speaker = lastIsA ? this : partner;
			var persona = lastIsA ? this.npc : partner.npc;
			var cli = lastIsA ? this.client : partner.client;

			string sysBye = BuildPairSystem(this.npc, partner.npc);
			bool done = false; string bye = null;
			AskWithPersona(cli, persona, sysBye, "Say a short natural goodbye.", r => { bye = string.IsNullOrWhiteSpace(r) ? PickFarewell() : r; done = true; });
			while (!done) yield return null;

			if (lastIsA) ShowBubble(bye); else partner.ShowBubble(bye);
		}

		if (agentStopDuringChat)
		{
			ResumeMovementSelf();
			ResumeMovementPartner();
		}

		if (animator && !string.IsNullOrWhiteSpace(talkBool))
			animator.SetBool(talkBool, false);
		if (partner.animator && !string.IsNullOrWhiteSpace(partner.talkBool))
			partner.animator.SetBool(partner.talkBool, false);

		_inDialogue = false;
		partner._inDialogue = false;

		DialogueFinished?.Invoke(this);
		partner.DialogueFinished?.Invoke(partner);

		_currentPartner = null;
		_partnerWander = null;
		_partnerAgent = null;
	}

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
			_wanderAutonomyHeldByDialogue = true; 
			if (_wander.enabled) { _wander.enabled = false; _disabledWander = true; }
		}

		if (_agent != null && _agent.enabled && _agent.isOnNavMesh)
		{
			_myPrevStopped = _agent.isStopped;

			_hadPath = _agent.hasPath;
			_savedDestination = _hadPath ? _agent.destination : _agent.transform.position;

			_agent.isStopped = true;
			_agent.updateRotation = false;
		}
	}

	void ResumeMovementSelf()
	{
		if (_wander != null)
		{
			if (_disabledWander && !_wander.enabled) { _wander.enabled = true; _disabledWander = false; }
			if (_wanderAutonomyHeldByDialogue)
			{
				_wander.SetAutonomySuspended(false);
				_wanderAutonomyHeldByDialogue = false;
			}
		}

		if (_agent != null && _agent.enabled && _agent.isOnNavMesh)
		{
			if (_hadPath && !_agent.hasPath)
				_agent.SetDestination(_savedDestination);

			_agent.isStopped = _myPrevStopped == false ? false : _myPrevStopped;
			_agent.updateRotation = true;
		}
	}

	void PauseMovementPartner()
	{
		if (_currentPartner == null) return;

		if (_partnerWander != null)
		{
			_partnerWander.SetAutonomySuspended(true);
			_partnerAutonomyHeldByDialogue = true;
			if (_partnerWander.enabled) { _partnerWander.enabled = false; _partnerDisabledWander = true; }
		}

		if (_partnerAgent != null && _partnerAgent.enabled && _partnerAgent.isOnNavMesh)
		{
			_partnerPrevStopped = _partnerAgent.isStopped;

			_partnerHadPath = _partnerAgent.hasPath;
			_partnerSavedDestination = _partnerHadPath ? _partnerAgent.destination : _partnerAgent.transform.position;

			_partnerAgent.isStopped = true;
			_partnerAgent.updateRotation = false;
		}
	}

	void ResumeMovementPartner()
	{
		if (_currentPartner == null) return;

		if (_partnerWander != null)
		{
			if (_partnerDisabledWander && !_partnerWander.enabled) { _partnerWander.enabled = true; _partnerDisabledWander = false; }
			if (_partnerAutonomyHeldByDialogue)
			{
				_partnerWander.SetAutonomySuspended(false);
				_partnerAutonomyHeldByDialogue = false;
			}
		}

		if (_partnerAgent != null && _partnerAgent.enabled && _partnerAgent.isOnNavMesh)
		{
			if (_partnerHadPath && !_partnerAgent.hasPath)
				_partnerAgent.SetDestination(_partnerSavedDestination);

			_partnerAgent.isStopped = _partnerPrevStopped == false ? false : _partnerPrevStopped;
			_partnerAgent.updateRotation = true;
		}
	}


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
