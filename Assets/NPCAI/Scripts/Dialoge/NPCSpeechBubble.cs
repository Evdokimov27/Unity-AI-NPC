using System.Collections;
using UnityEngine;
using TMPro;

[DisallowMultipleComponent]
public class NPCSpeechBubble : MonoBehaviour
{
	[Header("References")]
	[Tooltip("Child TMP_Text used to display NPC lines.")]
	public TMP_Text textUI;

	[Header("Settings")]
	[Tooltip("Seconds to keep text visible after last change.")]
	public float visibleTime = 4f;

	public bool IsVisible { get; private set; }

	private Coroutine hideRoutine;

	public void ShowText(string line)
	{
		if (!textUI) return;
		textUI.transform.parent.gameObject.SetActive(true);
		textUI.text = line;
		IsVisible = true;
		if (hideRoutine != null) StopCoroutine(hideRoutine);
		hideRoutine = StartCoroutine(HideLater());
	}

	private IEnumerator HideLater()
	{
		yield return new WaitForSeconds(visibleTime);
		if (textUI) textUI.transform.parent.gameObject.SetActive(false);
		IsVisible = false;
		hideRoutine = null;
	}
}
