using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(NPCDialogueManager))]
public class NPCDialogueManagerEditor : Editor
{
	public override void OnInspectorGUI()
	{
		DrawDefaultInspector();

		NPCDialogueManager dialogue = (NPCDialogueManager)target;

		if (GUILayout.Button("Ask NPC"))
		{
			dialogue.SendDialogue();
		}
	}
}
