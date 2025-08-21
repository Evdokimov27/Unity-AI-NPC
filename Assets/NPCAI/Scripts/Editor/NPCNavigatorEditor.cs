using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(NPCAI))]
public class NPCNavigatorEditor : Editor
{
	private string cmd = "";

	public override void OnInspectorGUI()
	{
		DrawDefaultInspector();

		NPCAI npc = (NPCAI)target;

		GUILayout.Space(10);
		GUILayout.Label("Test Command:");
		cmd = GUILayout.TextField(cmd);

		if (GUILayout.Button("Send Command"))
		{
			npc.GoTo(cmd);
		}
	}
}
