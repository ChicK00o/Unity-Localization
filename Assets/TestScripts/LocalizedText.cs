using UnityEngine;
using System.Collections;

[Localized("LocalizedText")]
public class LocalizedText : MonoBehaviour {
	public string buttonTextShow;
	public string buttonTextHide;
	public string labelText;
	protected bool showLabel = false;

	public void DoGUI() {
		if (!showLabel) {
			if (GUILayout.Button(buttonTextShow)) {
				showLabel = true;
			}
		}
		else {
			if (GUILayout.Button(buttonTextHide)) {
				showLabel = false;
			}
			GUILayout.Label(labelText);
		}
	}
}
