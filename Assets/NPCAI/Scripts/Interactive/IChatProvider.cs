using System;
using System.Collections;
using UnityEngine;

public interface IChatProvider
{
	IEnumerator SendChat(string systemPrompt, string userPrompt, Action<string> onReply);
}
