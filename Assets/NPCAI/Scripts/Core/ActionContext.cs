using UnityEngine;

public class ActionContext
{
	public GameObject actor;          // NPC game object
	public GameObject explicitTarget; // optional fixed target
	public string userCommand;        // raw user command (any language)
	public string mappedTargetName;   // resolved target name (if already mapped)
	public object blackboard;         // any extra data you want to pass

	public ActionContext(GameObject actor)
	{
		this.actor = actor;
	}
}
