using System.Collections;
using System.Collections.Generic;

// JSON Format
/* [
 * 		{
 * 			"sheetName": <string>,
 * 			"vars" : [ <string, ... ],
 * 			"vals" : [ <string>, ... ]
 * 		}, ...
 * 	
 * ]*/

public class LocalizationDocument {
	protected class LocaleDocument {
		public string sheetName { get; set; }
		public string[] vars { get; set; }
		public string[] vals { get; set; }
	}

	// localizationData[sheetName][variableName] = variableValue
	protected Dictionary<string, Dictionary<string, string>>	mLocalizationData;
	protected Localizer.Lanugages								mDocumentLanguage;

	public LocalizationDocument(Localizer.Lanugages documentLanguage) {
		mDocumentLanguage = documentLanguage;
		UnityEngine.TextAsset jsonString = UnityEngine.Resources.Load<UnityEngine.TextAsset>("Localization/" + documentLanguage.ToString());
		LocaleDocument[] locale = new System.Text.Json.JsonParser().Parse<LocaleDocument[]>(jsonString.text);
		System.Diagnostics.Debug.Assert(locale != null && locale is LocaleDocument[] && ((LocaleDocument[])locale).Length > 0);

		mLocalizationData = new Dictionary<string, Dictionary<string, string>>();
		foreach(LocaleDocument document in locale) {
			System.Diagnostics.Debug.Assert(document != null && document.sheetName != null);
			mLocalizationData.Add(document.sheetName, new Dictionary<string, string>());
			System.Diagnostics.Debug.Assert(document.vars.Length == document.vals.Length);
			for (int i = 0, isize = document.vars.Length; i < isize; ++i) {
				mLocalizationData[document.sheetName].Add(document.vars[i], document.vals[i]);
			}
		}
	}

	public Localizer.Lanugages Language {
		get {
			return mDocumentLanguage;
		}
	}

	public bool ContainsSheet(string sheetName) {
		if (mLocalizationData == null)
			return false;

		if (!mLocalizationData.ContainsKey(sheetName))
			return false;

		return mLocalizationData[sheetName] != null; 
	}

	public bool ContainsVariable(string sheetName, string variableName) {
		if (!ContainsSheet(sheetName))
			return false;

		if (!mLocalizationData[sheetName].ContainsKey(variableName))
			return false;

		return mLocalizationData[sheetName][variableName] != null;
	}

	public string Retrieve(string sheetName, string variableName) {
		if (ContainsVariable(sheetName, variableName))
			return mLocalizationData[sheetName][variableName];
		return "UNKNOWN";
	}

	public List<string> GetKeys(string sheetName) {
		if (!ContainsSheet(sheetName))
			return null;

		List<string> result = new List<string>();
		Dictionary<string, string> sheet = mLocalizationData[sheetName];
		foreach(KeyValuePair<string, string> kvp in sheet) 
			result.Add(kvp.Key);
		return result;
	}
}
