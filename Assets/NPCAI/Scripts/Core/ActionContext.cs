using UnityEngine;

public class ActionContext
{
	public GameObject actor;
	public GameObject explicitTarget;
	public string userCommand;
	public string mappedTargetName;
	public object blackboard;

	public float waitSeconds;    
	public string dialogueText;  

	public ActionContext(GameObject actor)
	{
		this.actor = actor;
	}
}
