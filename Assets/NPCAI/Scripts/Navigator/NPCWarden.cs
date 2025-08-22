using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
[DisallowMultipleComponent]
public class NPCWander : MonoBehaviour
{
	[Header("Path Graph")]
	public List<Vector3> graphNodes = new List<Vector3>();
	[System.Serializable] public struct Edge { public int a; public int b; }
	public List<Edge> graphEdges = new List<Edge>();
	public bool startFromNearestNode = true;
	[Min(0.05f)] public float nodeArriveDistance = 0.5f;
	public Vector2 idleBetweenNodes = new Vector2(0.25f, 0.7f);

	[Header("Dialogue")]
	public bool enableProximityDialogue = true;
	[Min(0.5f)] public float chatRange = 3.0f;
	[Min(0.5f)] public float chatCooldown = 20f;
	[Range(0f, 1f)] public float chatStartChance = 0.6f;
	public LayerMask npcLayer = ~0;
	public bool agentStopDuringChat = true;
	public int minTurns = 3;
	public int maxTurns = 6;
	[Min(0.3f)] public float minTurnDuration = 1.1f;
	[Min(0.5f)] public float maxTurnDuration = 4.0f;
	[Min(0f)] public float secondsPerCharacter = 0.045f;
	[Min(0.5f)] public float askTimeout = 3.0f;
	[Min(30f)] public float faceTurnSpeed = 360f;
	public List<string> dialogTopics = new List<string> { "weather", "parts of the city", "work", "food", "music", "news", "sports", "transport" };
	public NPCDialogueManager dialogueManager;
	public string fallbackQuestionTemplate = "What do you think about \"{0}\"?";
	public string fallbackFollowUpTemplate = "And what about \"{0}\", considering you said: “{1}”?";
	public string fallbackAnswerShort = "Sounds good to me.";
	public string fallbackAnswerAlt = "I like that.";
	public List<string> farewellPhrases = new List<string> { "Alright, see you!", "Nice chat, bye!", "I’ll get going. Bye!", "Have a good day!", "See you soon!" };

	[Header("Re-engage Control")]
	public float samePartnerCooldown = 45f;
	public bool requireSeparationToReengage = true;
	public float reengageSeparationDistance = 4f;

	private NavMeshAgent agent;
	private int currentNodeIndex = -1;
	private int previousNodeIndex = -1;
	private bool waitingAtNode;
	private int queuedNodeIndex = -1;
	private float nextHopTime;
	private readonly Collider[] _overlap = new Collider[16];
	private float _lastChatTime = -999f;
	private bool _inDialogue;
	private int _selfId;
	private bool _autonomySuspended;
	private readonly Dictionary<int, float> _partnerCooldownUntil = new Dictionary<int, float>();

	void Awake()
	{
		agent = GetComponent<NavMeshAgent>();
		if (!dialogueManager) dialogueManager = GetComponent<NPCDialogueManager>();
		_selfId = GetInstanceID();
	}

	void OnEnable()
	{
		waitingAtNode = false;
		queuedNodeIndex = -1;
		if (graphNodes != null && graphNodes.Count > 0) BindStartNode();
	}

	void Update()
	{
		if (!agent || !agent.enabled) return;
		if (_autonomySuspended) return;
		UpdateFollowGraph();
		if (enableProximityDialogue && !_inDialogue) TryFindPartnerAndChat();
	}

	public void SetAutonomySuspended(bool value)
	{
		_autonomySuspended = value;
		if (agent && agent.enabled)
		{
			agent.isStopped = value || (agentStopDuringChat && _inDialogue);
			if (value) agent.ResetPath();
			else
			{
				if (graphNodes != null && graphNodes.Count > 0 && currentNodeIndex >= 0)
					agent.SetDestination(graphNodes[currentNodeIndex]);
			}
		}
	}

	void BindStartNode()
	{
		if (graphNodes == null || graphNodes.Count == 0) { if (agent) agent.isStopped = true; return; }
		int startIdx = 0;
		if (startFromNearestNode)
		{
			float best = float.MaxValue;
			for (int i = 0; i < graphNodes.Count; i++)
			{
				float d = (graphNodes[i] - transform.position).sqrMagnitude;
				if (d < best) { best = d; startIdx = i; }
			}
		}
		currentNodeIndex = startIdx;
		previousNodeIndex = -1;
		float arrive = Mathf.Max(0.05f, nodeArriveDistance);
		if ((transform.position - graphNodes[currentNodeIndex]).sqrMagnitude <= arrive * arrive)
		{
			var neigh = GetNeighbors(currentNodeIndex);
			if (neigh.Count > 0) currentNodeIndex = neigh[Random.Range(0, neigh.Count)];
		}
		agent.stoppingDistance = Mathf.Max(agent.stoppingDistance, arrive);
		agent.isStopped = false;
		agent.SetDestination(graphNodes[currentNodeIndex]);
	}

	void UpdateFollowGraph()
	{
		if (graphNodes == null || graphNodes.Count == 0) { agent.isStopped = true; return; }
		if (currentNodeIndex < 0 || currentNodeIndex >= graphNodes.Count) { BindStartNode(); return; }
		if (waitingAtNode)
		{
			if (Time.time >= nextHopTime)
			{
				waitingAtNode = false;
				if (queuedNodeIndex >= 0)
				{
					previousNodeIndex = currentNodeIndex;
					currentNodeIndex = queuedNodeIndex;
					queuedNodeIndex = -1;
					if (!(agentStopDuringChat && _inDialogue))
					{
						agent.isStopped = false;
						agent.SetDestination(graphNodes[currentNodeIndex]);
					}
				}
			}
			return;
		}
		if (agent.pathPending) return;
		float arrive = Mathf.Max(0.05f, nodeArriveDistance);
		bool reached = agent.remainingDistance != Mathf.Infinity &&
					   agent.remainingDistance <= arrive &&
					   agent.velocity.sqrMagnitude < 0.02f;
		if (!reached && (transform.position - graphNodes[currentNodeIndex]).sqrMagnitude <= arrive * arrive) reached = true;
		if (!reached) return;
		var neighbors = GetNeighbors(currentNodeIndex);
		if (neighbors.Count == 0) { agent.isStopped = true; return; }
		int chosen;
		if (neighbors.Count == 1) chosen = neighbors[0];
		else
		{
			var pool = new List<int>(neighbors);
			if (previousNodeIndex >= 0) pool.Remove(previousNodeIndex);
			if (pool.Count == 0) pool = neighbors;
			chosen = pool[Random.Range(0, pool.Count)];
		}
		queuedNodeIndex = chosen;
		waitingAtNode = true;
		if (!(agentStopDuringChat && _inDialogue)) agent.isStopped = true;
		nextHopTime = Time.time + Random.Range(idleBetweenNodes.x, idleBetweenNodes.y);
	}

	List<int> GetNeighbors(int idx)
	{
		var list = new List<int>();
		if (graphEdges == null) return list;
		for (int i = 0; i < graphEdges.Count; i++)
		{
			var e = graphEdges[i];
			if (e.a == idx && e.b >= 0 && e.b < graphNodes.Count) { if (!list.Contains(e.b)) list.Add(e.b); }
			else if (e.b == idx && e.a >= 0 && e.a < graphNodes.Count) { if (!list.Contains(e.a)) list.Add(e.a); }
		}
		return list;
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

			var otherGo = col.attachedRigidbody ? col.attachedRigidbody.gameObject : col.gameObject;
			if (!otherGo || otherGo == gameObject) continue;

			var other = otherGo.GetComponent<NPCWander>();
			if (!other || !other.enabled) continue;
			if (other._inDialogue) continue;
			if (Time.time - other._lastChatTime < other.chatCooldown) continue;

			int otherId = other._selfId == 0 ? other.GetInstanceID() : other._selfId;

			float until;
			bool hasEntry = _partnerCooldownUntil.TryGetValue(otherId, out until);

			if (hasEntry)
			{
				if (Time.time < until) continue;
				if (requireSeparationToReengage && Vector3.Distance(transform.position, other.transform.position) < reengageSeparationDistance) continue;
			}

			if (_selfId <= other._selfId) continue;
			if (Random.value > chatStartChance) continue;

			StartCoroutine(RunDialogueWith(other));
			return;
		}
	}


	IEnumerator RunDialogueWith(NPCWander partner)
	{
		_inDialogue = true;
		partner._inDialogue = true;
		_lastChatTime = Time.time;
		partner._lastChatTime = Time.time;
		string topic = (dialogTopics != null && dialogTopics.Count > 0) ? dialogTopics[Random.Range(0, dialogTopics.Count)] : "misc";
		int turns = Mathf.Max(2, Random.Range(Mathf.Min(minTurns, maxTurns), Mathf.Max(minTurns, maxTurns) + 1));
		bool myPrevStopped = false, hisPrevStopped = false;
		if (agent && agent.enabled && agentStopDuringChat) { myPrevStopped = agent.isStopped; agent.isStopped = true; agent.ResetPath(); }
		var otherAgent = partner.agent;
		if (otherAgent && otherAgent.enabled && agentStopDuringChat) { hisPrevStopped = otherAgent.isStopped; otherAgent.isStopped = true; otherAgent.ResetPath(); }
		NPCWander A = this;
		NPCWander B = partner;
		string sys = "You are roleplaying as an NPC in a video game. Keep replies short (1–2 sentences), casual, and coherent with the partner's last line.";
		string lastA = null;
		string lastB = null;
		for (int turn = 0; turn < turns; turn++)
		{
			bool speakerIsA = (turn % 2 == 0);
			if (speakerIsA)
			{
				string userPrompt = (turn == 0) ? BuildAskPrompt(topic) : BuildFollowUpAskPrompt(topic, lastB);
				lastA = null;
				yield return SpeakOneTurn(A, B, sys, userPrompt, r => lastA = r);
			}
			else
			{
				string userPrompt = BuildAnswerPrompt(topic, lastA);
				lastB = null;
				yield return SpeakOneTurn(B, A, sys, userPrompt, r => lastB = r);
			}
			FaceTowards(speakerIsA ? A.transform : B.transform, speakerIsA ? B.transform : A.transform);
		}
		{
			bool lastSpeakerIsA = ((turns - 1) % 2 == 0);
			NPCWander speaker = lastSpeakerIsA ? A : B;
			NPCWander listener = lastSpeakerIsA ? B : A;
			string farewellUser = "Wrap up the conversation politely, say goodbye naturally.";
			yield return SpeakOneTurn(speaker, listener, sys, farewellUser, null);
		}
		_inDialogue = false;
		partner._inDialogue = false;
		if (agent && agent.enabled && agentStopDuringChat) agent.isStopped = myPrevStopped;
		if (otherAgent && otherAgent.enabled && agentStopDuringChat) otherAgent.isStopped = hisPrevStopped;
		_lastChatTime = Time.time;
		partner._lastChatTime = Time.time;
		int pid = partner._selfId == 0 ? partner.GetInstanceID() : partner._selfId;
		_partnerCooldownUntil[pid] = Time.time + Mathf.Max(samePartnerCooldown, chatCooldown);
		if (!(agentStopDuringChat && agent && agent.enabled && agent.isStopped) && graphNodes != null && graphNodes.Count > 0 && currentNodeIndex >= 0)
			agent.SetDestination(graphNodes[currentNodeIndex]);
		if (!(agentStopDuringChat && otherAgent && otherAgent.enabled && otherAgent.isStopped) && partner.graphNodes != null && partner.graphNodes.Count > 0 && partner.currentNodeIndex >= 0)
			otherAgent.SetDestination(partner.graphNodes[partner.currentNodeIndex]);
	}

	IEnumerator SpeakOneTurn(NPCWander speaker, NPCWander listener, string sys, string user, System.Action<string> onDone)
	{
		float started = Time.time;
		bool done = false;
		string reply = null;
		if (speaker.dialogueManager != null)
		{
			speaker.dialogueManager.ClientAsk(sys, user, r => { reply = string.IsNullOrWhiteSpace(r) ? null : r; done = true; });
		}
		else
		{
			string u = user.ToLowerInvariant();
			if (u.Contains("wrap up") || u.Contains("goodbye"))
			{
				reply = PickFarewell(speaker);
			}
			else if (u.Contains("follow-up") || u.Contains("ask") || u.Contains("question"))
			{
				if (u.Contains("partner's last line"))
				{
					string t = ExtractTopicFromUser(user);
					string q = ExtractQuoteFromUser(user);
					reply = string.Format(fallbackFollowUpTemplate, string.IsNullOrEmpty(t) ? "the topic" : t, string.IsNullOrEmpty(q) ? "…" : q);
				}
				else
				{
					string t = ExtractTopicFromUser(user);
					reply = string.Format(fallbackQuestionTemplate, string.IsNullOrEmpty(t) ? "the topic" : t);
				}
			}
			else
			{
				reply = (Random.value < 0.5f) ? fallbackAnswerShort : fallbackAnswerAlt;
			}
			done = true;
		}
		while (!done && Time.time - started < askTimeout)
		{
			FaceTowards(speaker.transform, listener.transform);
			yield return null;
		}
		if (reply == null)
		{
			string u = user.ToLowerInvariant();
			reply = (u.Contains("wrap up") || u.Contains("goodbye")) ? PickFarewell(speaker) : (u.Contains("question") ? string.Format(fallbackQuestionTemplate, "this") : fallbackAnswerShort);
		}
		if (speaker.dialogueManager != null) speaker.dialogueManager.ShowBubble(reply);
		int len = reply?.Length ?? 0;
		float dynamicDuration = Mathf.Clamp(len * secondsPerCharacter, minTurnDuration, maxTurnDuration);
		float endAt = Time.time + dynamicDuration;
		while (Time.time < endAt)
		{
			FaceTowards(speaker.transform, listener.transform);
			yield return null;
		}
		onDone?.Invoke(reply);
	}

	string BuildAskPrompt(string topic) => $"Greet the other NPC and ask a short, specific question about the topic: {topic}. End with a question.";
	string BuildFollowUpAskPrompt(string topic, string partnersLastLine)
	{
		string quoted = TrimQuote(partnersLastLine, 80);
		return $"Ask a short follow-up question referencing the partner's last line: \"{quoted}\". Keep it on the topic: {topic}. End with a question.";
	}
	string BuildAnswerPrompt(string topic, string partnersQuestion)
	{
		string quoted = TrimQuote(partnersQuestion, 80);
		return $"Briefly answer the partner's question: \"{quoted}\". Stay on the topic: {topic}. One or two sentences.";
	}
	string TrimQuote(string s, int max)
	{
		if (string.IsNullOrEmpty(s)) return "";
		s = s.Replace("\n", " ").Trim();
		return s.Length <= max ? s : s.Substring(0, max) + "...";
	}
	string ExtractTopicFromUser(string user)
	{
		int i = user.IndexOf("topic:");
		if (i >= 0)
		{
			string t = user.Substring(i + 6).Trim();
			int dot = t.IndexOf('.');
			if (dot >= 0) t = t.Substring(0, dot).Trim();
			return t.Trim('"');
		}
		return null;
	}
	string ExtractQuoteFromUser(string user)
	{
		int i1 = user.IndexOf('\"');
		int i2 = user.LastIndexOf('\"');
		if (i1 >= 0 && i2 > i1) return user.Substring(i1 + 1, i2 - i1 - 1);
		return null;
	}
	string PickFarewell(NPCWander w)
	{
		if (w.farewellPhrases != null && w.farewellPhrases.Count > 0)
			return w.farewellPhrases[Random.Range(0, w.farewellPhrases.Count)];
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
