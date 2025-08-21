using System;
using UnityEngine;

[DisallowMultipleComponent]
public class TalkAction : MonoBehaviour, IActionStep
{
	public string StepName => "Talk";

	public enum TalkMode
	{
		FixedText,       // say fixed line once
		GenerateOnce,    // ask LLM once from prompt
		GenerateLoop     // ask LLM periodically while running (until externally completed)
	}

	[Header("Dialogue")]
	public TalkMode mode = TalkMode.FixedText;

	[Tooltip("Fixed line to say (used in FixedText).")]
	[TextArea] public string fixedLine = "On my way.";

	[Tooltip("Prompt for LLM (used in GenerateOnce/GenerateLoop).")]
	[TextArea] public string llmPrompt = "Say one short in-character sentence about walking.";

	[Tooltip("Dialogue manager for LLM calls (optional if using FixedText).")]
	public NPCDialogueManager dialogueManager;

	[Tooltip("Interval (seconds) for GenerateLoop mode.")]
	public float loopInterval = 3f;

	private float _timer;
	private Action<bool> _onComplete;
	private bool _active;

	public void Begin(ActionContext context, Action<bool> onComplete)
	{
		_onComplete = onComplete;
		_active = true;

		switch (mode)
		{
			case TalkMode.FixedText:
				SayFixed();
				Finish(true);
				break;

			case TalkMode.GenerateOnce:
				if (!dialogueManager) { Debug.LogWarning("TalkAction: DialogueManager missing."); Finish(false); return; }
				dialogueManager.ClientAsk(
					"You are an NPC. One short sentence.",
					BuildUserPrompt(context),
					_ => Finish(true)
				);
				break;

			case TalkMode.GenerateLoop:
				if (!dialogueManager) { Debug.LogWarning("TalkAction: DialogueManager missing."); Finish(false); return; }
				_timer = 0f;
				// do not finish here; Tick() will keep asking until someone cancels or a parent composite completes
				break;
		}
	}

	public void Tick(ActionContext context)
	{
		if (!_active) return;

		if (mode == TalkMode.GenerateLoop)
		{
			_timer += Time.deltaTime;
			if (_timer >= loopInterval)
			{
				_timer = 0f;
				dialogueManager.ClientAsk(
					"You are an NPC. One short sentence about the ongoing action.",
					BuildUserPrompt(context),
					_ => { /* fire-and-forget */ }
				);
			}
		}
	}

	public void Cancel(ActionContext context)
	{
		_active = false;
		_onComplete = null;
	}

	private void Finish(bool ok)
	{
		_active = false;
		_onComplete?.Invoke(ok);
		_onComplete = null;
	}

	private void SayFixed()
	{
		Debug.Log($"NPC says: {fixedLine}");
	}

	private string BuildUserPrompt(ActionContext ctx)
	{
		string targetName = ctx?.explicitTarget ? ctx.explicitTarget.name :
							(!string.IsNullOrEmpty(ctx?.mappedTargetName) ? ctx.mappedTargetName : "unknown target");

		string userCmd = string.IsNullOrEmpty(ctx?.userCommand) ? "" : $"User command: \"{ctx.userCommand}\"\n";
		return $"{userCmd}Context: moving towards {targetName}. NPC response:";
	}

}
