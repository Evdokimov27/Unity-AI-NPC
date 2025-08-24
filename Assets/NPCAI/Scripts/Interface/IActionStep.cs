using System;

public interface IActionStep
{
	string StepName { get; }
	void Begin(ActionContext context, Action<bool> onComplete);
	void Tick(ActionContext context);
	void Cancel(ActionContext context);
}
