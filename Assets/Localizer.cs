using System.Collections;
using System.Collections.Generic;

public class Localizer {
	LocalizationDocument		mCurrentLocale;
	List<object>				mLocalizedObjects;

	public enum Lanugages { En, Hu };

	public Localizer(Localizer.Lanugages language) {
		mCurrentLocale = new LocalizationDocument(language);
		mLocalizedObjects = new List<object>();
	}

	public void Localize(object o) {
		if (!mLocalizedObjects.Contains(o))
			mLocalizedObjects.Add(o);
		Relocalize(o);
	}

	public void Relocalize(Localizer.Lanugages language) {
		if (mCurrentLocale.Language == language)
			return;

		mCurrentLocale = new LocalizationDocument(language);

		for (int i = mLocalizedObjects.Count - 1; i >= 0; --i) {
			if (mLocalizedObjects[i] == null) {
				mLocalizedObjects.RemoveAt(i);
			}
			else {
				Relocalize(mLocalizedObjects[i]);
			}
		}
	}

	protected void Relocalize(object o) {
		System.Reflection.MemberInfo info = o.GetType();
		Localized localizedAttribute = (Localized)info.GetCustomAttributes(true)[0];
		
		string sheetName = localizedAttribute.Sheet;
		List<string> variables = mCurrentLocale.GetKeys(sheetName);

		foreach (string variable in variables) {
			foreach (var field in o.GetType().GetFields()) {
				if (variables.Contains(field.Name))
					field.SetValue(o, mCurrentLocale.Retrieve(sheetName, field.Name));
				//UnityEngine.Debug.Log(field.Name + ": " + field.GetValue(o));
			}
		}
	}
}
