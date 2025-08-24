using System;
using System.Linq;
using System.Text;
using UnityEngine;

[DisallowMultipleComponent]
public class ResolveTargetAuto : MonoBehaviour, IActionStep
{
	public string StepName => "ResolveTargetAuto";

	public ChatGPTClient client;
	public GameObject[] candidates;
	public bool failOnNone = true;
	public bool autoCollectByTag = true;

	private Action<bool> _onComplete;

	[Serializable]
	private class PlanResult
	{
		public string targetId;
		public string actionText;
		public string commandText;
	}

	public void Begin(ActionContext context, Action<bool> onComplete)
	{
		_onComplete = onComplete;

		if (context == null || client == null)
		{
			Finish(false);
			return;
		}

		if ((candidates == null || candidates.Length == 0) && autoCollectByTag)
			candidates = GameObject.FindGameObjectsWithTag("Target");

		var activeCandidates = (candidates ?? Array.Empty<GameObject>())
			.Where(go => go && go.activeInHierarchy)
			.ToArray();

		if (activeCandidates.Length == 0)
		{
			Finish(false);
			return;
		}

		if (context.explicitTarget != null || !string.IsNullOrEmpty(context.mappedTargetName))
		{
			EnsurePlanIfMissing(context);
			Finish(true);
			return;
		}

		string userText = context.userCommand ?? string.Empty;

		string systemPrompt =
			"You are a game assistant. Given a user's command (any language), do the following IN YOUR HEAD, then output JSON ONLY:\n" +
			"1) Infer the most relevant place/tool to fulfill the command using general world knowledge.\n" +
			"2) Compare that inferred place/tool with the provided list of existing scene objects (IDs are their names). " +
			"   Choose EXACTLY ONE id from the list that best fits. If none is suitable, use \"NONE\".\n" +
			"3) Produce two short texts in the user's language:\n" +
			"   - actionText: a short human phrase.\n" +
			"   - commandText: a short UI-like string.\n" +
			"Rules:\n" +
			"- Output JSON only, no extra words.\n" +
			"- Schema: {\"targetId\":\"<one id or NONE>\", \"actionText\":\"...\", \"commandText\":\"...\"}\n" +
			"- The targetId MUST be copied verbatim from the provided ids.\n" +
			"- Handle inflected languages.\n" +
			"- If nothing fits, set targetId to \"NONE\" and keep texts short.";

		var sb = new StringBuilder();
		sb.AppendLine("Existing object ids (choose exactly one verbatim or NONE):");
		foreach (var go in activeCandidates)
			sb.AppendLine("- " + go.name);

		string userPrompt =
			sb.ToString() + "\n\n" +
			$"User command: \"{userText}\"\n" +
			"Return JSON only for this user command.";

		client.Ask(systemPrompt, userPrompt, reply =>
		{
			if (string.IsNullOrWhiteSpace(reply))
			{
				Finish(false);
				return;
			}

			var plan = TryParse(reply);
			if (plan == null || string.IsNullOrEmpty(plan.targetId))
			{
				Finish(false);
				return;
			}

			if (string.Equals(plan.targetId, "NONE", StringComparison.OrdinalIgnoreCase))
			{
				context.blackboard = plan;
				Finish(!failOnNone);
				return;
			}

			var match = activeCandidates.FirstOrDefault(c =>
				c && string.Equals(c.name, plan.targetId, StringComparison.OrdinalIgnoreCase));

			if (!match)
			{
				Finish(false);
				return;
			}

			context.explicitTarget = match;
			context.mappedTargetName = match.name;
			context.blackboard = plan;
			Finish(true);
		});
	}

	public void Tick(ActionContext context) { }
	public void Cancel(ActionContext context) { _onComplete = null; }

	private void Finish(bool ok)
	{
		_onComplete?.Invoke(ok);
		_onComplete = null;
	}

	private void EnsurePlanIfMissing(ActionContext ctx)
	{
		if (ctx.blackboard is PlanResult) return;
		var name = ctx.explicitTarget ? ctx.explicitTarget.name :
				   (!string.IsNullOrEmpty(ctx.mappedTargetName) ? ctx.mappedTargetName : "Target");

		ctx.blackboard = new PlanResult
		{
			targetId = name,
			actionText = $"go to {name.ToLowerInvariant()}",
			commandText = $"Go {name}"
		};
	}

	private PlanResult TryParse(string json)
	{
		try { return JsonUtility.FromJson<PlanResult>(json); }
		catch { }

		try
		{
			string tid = Extract(json, "\"targetId\"", "\"");
			string at = Extract(json, "\"actionText\"", "\"");
			string ct = Extract(json, "\"commandText\"", "\"");
			if (string.IsNullOrEmpty(tid) && string.IsNullOrEmpty(at) && string.IsNullOrEmpty(ct))
				return null;
			return new PlanResult { targetId = tid ?? "NONE", actionText = at ?? "", commandText = ct ?? "" };
		}
		catch { return null; }
	}

	private string Extract(string text, string key, string quote)
	{
		int k = text.IndexOf(key, StringComparison.OrdinalIgnoreCase);
		if (k < 0) return null;
		int colon = text.IndexOf(':', k);
		if (colon < 0) return null;
		int first = text.IndexOf(quote, colon + 1);
		if (first < 0) return null;
		int second = text.IndexOf(quote, first + 1);
		if (second < 0) return null;
		return text.Substring(first + 1, second - first - 1);
	}
}
