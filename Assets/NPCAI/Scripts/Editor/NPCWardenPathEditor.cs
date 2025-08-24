#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

[CustomEditor(typeof(NPCWander))]
public class NPCWanderPathEditor : Editor
{
	const float SELECT_RADIUS = 1.0f;
	const float NODE_RADIUS = 0.18f;
	static readonly Color NODE_COLOR = new Color(0.25f, 1f, 0.6f, 1f);
	static readonly Color LINK_COLOR = Color.white;
	static readonly Color SELECTED_RING = Color.yellow;
	static readonly Color DOTTED_PREVIEW = new Color(1f, 1f, 0f, 0.7f);
	static readonly Color PATH_COLOR = new Color(0.2f, 0.9f, 1f, 1f);

	NPCWander npc;
	bool editingActive;
	int lastSelectedIndex = -1;
	bool isDraggingPoint;

	void OnEnable()
	{
		npc = target as NPCWander;

		if (npc == null)
		{
			if (target is Component comp)
				npc = comp.GetComponent<NPCWander>();
			else if (target is GameObject go)
				npc = go.GetComponent<NPCWander>();
		}

		if (npc == null) return;

		Selection.selectionChanged += OnSelectionChanged;
		SceneView.duringSceneGui += DuringSceneGUI;
	}

	void OnDisable()
	{
		SceneView.duringSceneGui -= DuringSceneGUI;
		Selection.selectionChanged -= OnSelectionChanged;
		editingActive = false;
		lastSelectedIndex = -1;
		isDraggingPoint = false;
		npc = null;
	}
	public override void OnInspectorGUI()
	{
		if (npc == null)
		{
			EditorGUILayout.HelpBox("NPCWander not found on target.", MessageType.Info);
			return;
		}
		DrawDefaultInspector();
		EditorGUILayout.Space();
		if (GUILayout.Button(editingActive ? "Stop Editing" : "Edit Path (Shift=create/connect, Ctrl=delete, LMB=select/drag)", GUILayout.Height(26)))
		{
			if (editingActive) StopEditing(); else StartEditing();
		}
		if (editingActive)
			EditorGUILayout.HelpBox("Shift: create/connect\nCtrl: delete\nLMB: select/drag", MessageType.Info);
	}

	void StartEditing()
	{
		editingActive = true;
		lastSelectedIndex = -1;
		isDraggingPoint = false;
		if (npc.graphNodes == null) npc.graphNodes = new List<Vector3>();
		if (npc.graphEdges == null) npc.graphEdges = new List<NPCWander.Edge>();
		RepaintAllSceneViews();
	}

	void StopEditing()
	{
		editingActive = false;
		lastSelectedIndex = -1;
		isDraggingPoint = false;
		RepaintAllSceneViews();
	}

	void OnSelectionChanged()
	{
		if (!editingActive || npc == null) return;
		if (Selection.activeGameObject != npc.gameObject) { editingActive = false; Repaint(); }
	}

	void DuringSceneGUI(SceneView sv)
	{
		if (!editingActive) return;
		if (!npc) return;

		var e = Event.current;
		if (e == null) return;
		if (e.alt) return;

		bool ctrlDown = e.control && e.button == 0 && e.type == EventType.MouseDown;
		bool shiftDown = e.shift && e.button == 0;
		bool lmbDown = !e.shift && !e.control && e.button == 0 && e.type == EventType.MouseDown;
		bool lmbDrag = !e.shift && !e.control && e.button == 0 && e.type == EventType.MouseDrag;
		bool lmbUp = !e.shift && !e.control && e.button == 0 && e.type == EventType.MouseUp;

		if (ctrlDown)
		{
			if (MouseToWorld(out var wp))
			{
				int near = FindNearestNode(npc.graphNodes, wp, SELECT_RADIUS);
				if (near >= 0)
				{
					Undo.RecordObject(npc, "Delete Node");
					DeleteNodeAt(near);
					EditorUtility.SetDirty(npc);
				}
			}
			e.Use();
			return;
		}

		if (lmbDown)
		{
			if (MouseToWorld(out var wp))
			{
				int hit = FindNearestNode(npc.graphNodes, wp, SELECT_RADIUS * 0.9f);
				if (hit >= 0)
				{
					lastSelectedIndex = hit;
					isDraggingPoint = true;
					e.Use();
					return;
				}
			}
		}

		if (lmbDrag && isDraggingPoint && lastSelectedIndex >= 0 && lastSelectedIndex < npc.graphNodes.Count)
		{
			if (MouseToWorld(out var wp))
			{
				Undo.RecordObject(npc, "Move Node");
				npc.graphNodes[lastSelectedIndex] = wp;
				EditorUtility.SetDirty(npc);
			}
			e.Use();
			return;
		}

		if (lmbUp && isDraggingPoint)
		{
			isDraggingPoint = false;
			e.Use();
			return;
		}

		if (shiftDown && e.type == EventType.MouseDown)
		{
			if (MouseToWorld(out var wp))
			{
				int hit = FindNearestNode(npc.graphNodes, wp, SELECT_RADIUS * 0.9f);
				if (hit >= 0)
				{
					if (lastSelectedIndex >= 0 && lastSelectedIndex != hit)
					{
						Undo.RecordObject(npc, "Connect Nodes");
						AddUniqueEdge(lastSelectedIndex, hit);
						EditorUtility.SetDirty(npc);
					}
					lastSelectedIndex = hit;
				}
				else
				{
					Undo.RecordObject(npc, "Add Node");
					npc.graphNodes.Add(wp);
					int newIdx = npc.graphNodes.Count - 1;
					if (lastSelectedIndex >= 0) AddUniqueEdge(lastSelectedIndex, newIdx);
					lastSelectedIndex = newIdx;
					EditorUtility.SetDirty(npc);
				}
			}
			e.Use();
			return;
		}

		DrawSelectedOnly();
	}

	void DrawSelectedOnly()
	{
		if (npc.graphNodes == null) return;

		Handles.color = LINK_COLOR;
		if (npc.graphEdges != null)
		{
			for (int i = 0; i < npc.graphEdges.Count; i++)
			{
				var ed = npc.graphEdges[i];
				if (ValidIndex(ed.a) && ValidIndex(ed.b))
					Handles.DrawLine(npc.graphNodes[ed.a], npc.graphNodes[ed.b]);
			}
		}

		Handles.color = NODE_COLOR;
		for (int i = 0; i < npc.graphNodes.Count; i++)
			Handles.SphereHandleCap(0, npc.graphNodes[i], Quaternion.identity, NODE_RADIUS * 2f, EventType.Repaint);

		if (lastSelectedIndex >= 0 && lastSelectedIndex < npc.graphNodes.Count)
		{
			Handles.color = SELECTED_RING;
			Handles.DrawWireDisc(npc.graphNodes[lastSelectedIndex], Vector3.up, NODE_RADIUS * 1.8f);
			if (Event.current.shift && MouseToWorld(out var cursor))
			{
				Handles.color = DOTTED_PREVIEW;
				Handles.DrawDottedLine(npc.graphNodes[lastSelectedIndex], cursor, 4f);
			}
		}

		var agent = npc.GetComponent<NavMeshAgent>();
		if (agent && agent.hasPath)
		{
			var c = agent.path.corners;
			Handles.color = PATH_COLOR;
			for (int i = 0; i < c.Length - 1; i++) Handles.DrawLine(c[i], c[i + 1]);
		}
	}

	void DeleteNodeAt(int idx)
	{
		if (!ValidIndex(idx)) return;
		npc.graphNodes.RemoveAt(idx);
		if (npc.graphEdges != null)
		{
			for (int i = npc.graphEdges.Count - 1; i >= 0; i--)
			{
				var e = npc.graphEdges[i];
				if (e.a == idx || e.b == idx) { npc.graphEdges.RemoveAt(i); continue; }
				if (e.a > idx) e.a--;
				if (e.b > idx) e.b--;
				npc.graphEdges[i] = e;
			}
			DedupEdges();
		}
		if (lastSelectedIndex == idx) lastSelectedIndex = -1;
		else if (lastSelectedIndex > idx) lastSelectedIndex--;
	}

	void AddUniqueEdge(int a, int b)
	{
		if (!ValidIndex(a) || !ValidIndex(b) || a == b) return;
		if (npc.graphEdges == null) npc.graphEdges = new List<NPCWander.Edge>();
		for (int i = 0; i < npc.graphEdges.Count; i++)
		{
			var e = npc.graphEdges[i];
			if ((e.a == a && e.b == b) || (e.a == b && e.b == a)) return;
		}
		npc.graphEdges.Add(new NPCWander.Edge { a = a, b = b });
	}

	void DedupEdges()
	{
		if (npc.graphEdges == null) return;
		var seen = new HashSet<(int, int)>();
		for (int i = npc.graphEdges.Count - 1; i >= 0; i--)
		{
			var e = npc.graphEdges[i];
			if (!ValidIndex(e.a) || !ValidIndex(e.b) || e.a == e.b) { npc.graphEdges.RemoveAt(i); continue; }
			int x = Mathf.Min(e.a, e.b);
			int y = Mathf.Max(e.a, e.b);
			var key = (x, y);
			if (seen.Contains(key)) npc.graphEdges.RemoveAt(i);
			else seen.Add(key);
		}
	}

	bool ValidIndex(int i) => npc.graphNodes != null && i >= 0 && i < npc.graphNodes.Count;

	static int FindNearestNode(List<Vector3> nodes, Vector3 p, float maxDist)
	{
		if (nodes == null || nodes.Count == 0) return -1;
		int best = -1; float bestD2 = maxDist * maxDist;
		for (int i = 0; i < nodes.Count; i++)
		{
			float d2 = (nodes[i] - p).sqrMagnitude;
			if (d2 <= bestD2) { bestD2 = d2; best = i; }
		}
		return best;
	}

	// Рейкаст по сцене с фоллбеком на плоскость XZ и снапом к NavMesh (если есть)
	static bool MouseToWorld(out Vector3 wp)
	{
		var ray = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);

		if (Physics.Raycast(ray, out var hit, 500f, Physics.DefaultRaycastLayers))
		{
			Vector3 p = hit.point;
			if (NavMesh.SamplePosition(p, out var navHit, 2.0f, NavMesh.AllAreas)) { wp = navHit.position; return true; }
			wp = p; return true;
		}

		var plane = new Plane(Vector3.up, Vector3.zero);
		if (plane.Raycast(ray, out var t2))
		{
			Vector3 p = ray.GetPoint(t2);
			if (NavMesh.SamplePosition(p, out var navHit, 2.0f, NavMesh.AllAreas)) { wp = navHit.position; return true; }
			wp = p; return true;
		}

		wp = Vector3.zero; return false;
	}

	static void RepaintAllSceneViews() => SceneView.RepaintAll();
}
#endif
