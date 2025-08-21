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

	private Coroutine hideRoutine;

	/// <summary>
	/// Call this when the NPC says something.
	/// </summary>
	public void ShowText(string line)
	{
		if (!textUI) return;

		textUI.gameObject.SetActive(true);
		textUI.text = line;

		// Restart countdown
		if (hideRoutine != null)
			StopCoroutine(hideRoutine);
		hideRoutine = StartCoroutine(HideLater());
	}

	private IEnumerator HideLater()
	{
		yield return new WaitForSeconds(visibleTime);
		if (textUI) textUI.gameObject.SetActive(false);
		hideRoutine = null;
	}
}
