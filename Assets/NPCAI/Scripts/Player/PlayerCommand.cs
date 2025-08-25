using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

[DisallowMultipleComponent]
public class CommandPlayer : MonoBehaviour
{
	[Header("NPC Search")]
	[Min(0.5f)] public float radius = 8f;
	public LayerMask npcLayer = ~0;
	public bool requireLineOfSight = false;
	public float losHeightOffset = 1.6f;

	[Header("Behavior")]
	[Tooltip("Max NPCs per command to avoid overload.")]
	[Min(1)] public int maxNPCsPerCall = 8;
	[Tooltip("Delay after reply before starting the action.")]
	[Min(0f)] public float postReplyDelay = 0.15f;
	[Tooltip("Timeout waiting for ChatGPT reply (sec).")]
	[Min(0.5f)] public float replyTimeout = 3.0f;

	[Header("Dialogue (confirmation before action)")]
	[TextArea]
	public string systemPrompt =
		"You are an NPC in a video game. Reply with a short confirmation that you understood the order, with no extra info. " +
		"If the order is incomplete, say you will clarify on the spot. Do not ask the player questions. Max 12 words.";
	[TextArea]
	public string confirmationTemplate =
		"Player command: \"{0}\". Confirm that you understood and will proceed.";

	[Header("LLM Safety")]
	[Tooltip("Min gap between ChatGPT requests per NPC (seconds).")]
	[Min(0f)] public float perNpcAskGap = 0.35f;

	[Header("Hotkey (optional)")]
	public bool enableHotkey = false;
	public KeyCode hotkey = KeyCode.Y;
	[TextArea] public string hotkeyCommand = "go to the door and open it";

	private readonly Collider[] _overlap = new Collider[64];
	private static readonly Dictionary<int, float> _npcLastAskAt = new Dictionary<int, float>();

	void Update()
	{
		if (enableHotkey && Input.GetKeyDown(hotkey))
			OrderNearby(hotkeyCommand);
	}

	public void OrderNearby(string command)
	{
		if (string.IsNullOrWhiteSpace(command)) return;

		int count = Physics.OverlapSphereNonAlloc(transform.position, radius, _overlap, npcLayer, QueryTriggerInteraction.Ignore);
		if (count <= 0) return;

		var picked = new List<NPCAI>(maxNPCsPerCall);
		var seen = new HashSet<NPCAI>();

		for (int i = 0; i < count && picked.Count < maxNPCsPerCall; i++)
		{
			var col = _overlap[i];
			if (!col) continue;

			var ai = col.GetComponent<NPCAI>() ?? col.GetComponentInParent<NPCAI>() ?? col.GetComponentInChildren<NPCAI>();
			if (!ai || !ai.enabled || !ai.gameObject.activeInHierarchy) continue;

			if (!seen.Add(ai)) continue;

			if (requireLineOfSight && !HasLineOfSight(ai.transform)) continue;

			Vector3 npcPos = ai.agent ? ai.agent.transform.position : ai.transform.position;
			if ((npcPos - transform.position).sqrMagnitude > radius * radius) continue;

			picked.Add(ai);
		}

		foreach (var ai in picked)
			StartCoroutine(ConfirmThenRun(ai, command));
	}

	IEnumerator ConfirmThenRun(NPCAI ai, string command)
	{
		FaceTowards(ai.transform, transform);

		var dm = ai.dialogueManager;
		EnsureDialogueClient(dm);

		int id = ai.GetInstanceID();
		float lastAt = _npcLastAskAt.TryGetValue(id, out var t0) ? t0 : -999f;
		float gap = Mathf.Max(0f, perNpcAskGap);
		while (Time.time - lastAt < gap)
			yield return null;

		bool replied = false;
		bool done = false;

		if (dm && ai.talkOnStartMove == null)
		{
			string userPrompt = string.Format(confirmationTemplate, command);

			dm.ClientAsk(systemPrompt, userPrompt, reply =>
			{
				string finalReply = string.IsNullOrWhiteSpace(reply) ? "Okay, proceeding." : reply;
				dm.ShowBubble(finalReply);
				replied = !string.IsNullOrWhiteSpace(reply);
				done = true;
			});

			_npcLastAskAt[id] = Time.time;

			float t = 0f;
			while (!done && t < replyTimeout)
			{
				t += Time.deltaTime;
				yield return null;
			}
		}

		if (postReplyDelay > 0f) yield return new WaitForSeconds(postReplyDelay);

		ai.GoTo(command);
	}

	void EnsureDialogueClient(NPCDialogueManager dm)
	{
		if (!dm) return;
		var global = NPCAI_GlobalClient.GetOrCreateClientInScene(); 
																	 
		var f = typeof(NPCDialogueManager).GetField("client", BindingFlags.Instance | BindingFlags.NonPublic);
		if (f != null)
		{
			var current = f.GetValue(dm) as MultiAIClient;
			if (current == null && global != null)
				f.SetValue(dm, global);
		}
	}

	void FaceTowards(Transform npc, Transform target)
	{
		if (!npc || !target) return;
		Vector3 dir = target.position - npc.position;
		dir.y = 0f;
		if (dir.sqrMagnitude < 0.001f) return;
		npc.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
	}

	bool HasLineOfSight(Transform target)
	{
		Vector3 from = transform.position + Vector3.up * losHeightOffset;
		Vector3 to = target.position + Vector3.up * losHeightOffset;
		Vector3 dir = (to - from);
		float dist = dir.magnitude;
		if (dist <= 0.001f) return true;
		dir /= dist;

		if (Physics.Raycast(from, dir, out RaycastHit hit, dist, ~0, QueryTriggerInteraction.Ignore))
		{
			return hit.transform.IsChildOf(target) || target.IsChildOf(hit.transform);
		}
		return true;
	}

	void OnDrawGizmosSelected()
	{
		Gizmos.color = new Color(0.1f, 0.7f, 1f, 0.35f);
		Gizmos.DrawWireSphere(transform.position, Mathf.Max(0.1f, radius));
	}
}
