using UnityEngine;
using System.Collections;

public class LocalizationTest : MonoBehaviour {
	Localizer localizer;
	LocalizedText mLocalizedText = null;
	QuickBrownFox mTestLabelText = null;

	void Awake() {
		localizer = new Localizer(Localizer.Lanugages.En);
		mTestLabelText = new QuickBrownFox();
		GameObject go = new GameObject();
		mLocalizedText = go.AddComponent<LocalizedText>();
		localizer.Localize(mLocalizedText);
		localizer.Localize(mTestLabelText);
	}

	void OnGUI() {
		GUILayout.BeginVertical();
		if (GUILayout.Button("Relocalize to En"))
			localizer.Relocalize(Localizer.Lanugages.En);
		if (GUILayout.Button("Relocalize to Hu"))
			localizer.Relocalize(Localizer.Lanugages.Hu);
		GUILayout.EndVertical();
		GUILayout.Label(mTestLabelText.Display);
		mLocalizedText.DoGUI();
	}
}
