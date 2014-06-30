using UnityEngine;
using System.Collections;

public class Localized : System.Attribute {
	protected string	mSheetName;

	public string Sheet {
		get {
			return mSheetName;
		}
	}

	public Localized(string sheet) {
		mSheetName = sheet;
	}
}
