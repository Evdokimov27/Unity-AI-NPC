using UnityEngine;

[DisallowMultipleComponent]
public class NPCProfile : MonoBehaviour
{
	[Header("Basic Info")]
	public string npcName;      
	public string mood;         
	[TextArea] public string backstory;
}
