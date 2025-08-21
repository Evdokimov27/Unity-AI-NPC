using UnityEngine;

[DisallowMultipleComponent]
public class NPCProfile : MonoBehaviour
{
	[Header("Basic Info")]
	public string npcName;      // e.g. "Olaf"
	public string mood;         // e.g. "Happy", "Sad"
	[TextArea] public string backstory; // e.g. "A blacksmith from the north"
}
