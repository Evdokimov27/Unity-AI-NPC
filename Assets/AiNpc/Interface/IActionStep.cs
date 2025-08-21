using System;

public interface IActionStep
{
	string StepName { get; }

	/// <summary>
	/// Called once to begin this step.
	/// Implementations must call onComplete(success) when finished.
	/// If the step is long-running, it should update via Unity Update/Coroutine.
	/// </summary>
	void Begin(ActionContext context, Action<bool> onComplete);

	/// <summary>
	/// Called every frame by the runner while this step is active (optional).
	/// </summary>
	void Tick(ActionContext context);

	/// <summary>
	/// Called if the pipeline is cancelled (optional).
	/// </summary>
	void Cancel(ActionContext context);
}
