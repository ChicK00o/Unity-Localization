Liscence
========
This, like all other original works on my github is public domain. Do with it as you will.
About
=====
During an interview i was asked how i would design a localization system, my first tought was "I don't want to use a function like ```GetLocalizedText(id : string) : string``` instead i want localization to happen automatically. I've used [C# Attributes](http://msdn.microsoft.com/en-us/library/aa288454(v=vs.71).aspx) before, they seemed like a natural fit for this project.

I tried to build this demo with as few external dependencies as possible, the only external dependencie is [Fast JSON Parser](https://github.com/ysharplanguage/FastJsonParser)

This project was built using Unity3D version 4.5.0f6, run the ```TestScene``` scene for a demo.

API & Usage
===========
At the core of the localization system is the ```Localized``` attribute. A class needs to be marked with ```Localized``` and all text in the class will be updated to the current locale.

There is one JSON file for every locale, each json file contains one or more "sheet" objects, each "sheet" object contains one or more "variable" strings. These JSON files should be auto generated, ideally there would be one Google Spreadsheet for each language of the game. The spreadsheet would contain multiple tabs. Each tab represents a "sheet" in the JSON file. Examples of tabs would be "ErrorMessageBox", "WelcomeScreen", "TutorialDialog". Within each sheet would be a number of variable names such as "OkButtonText", "HelloWorldText". 

Variable names in a class marked ```Localized``` don't have to match the names of those in the sheet it is associated with. However, any variables with the same name will be localized.

The localizer class does keep track of objects it has localized. Calling the ```Relocalize``` method will automatically relocalize every object that is currently localized.

Sample Usage
============
Mark a class as ```Localized``` and pass the sheet name into the calss.
```
[Localized("LocalizedText")]
public class LocalizedText : MonoBehaviour {
	public string buttonTextShow;
	public string buttonTextHide;
	public string labelText;
	protected bool showLabel = false;
```
That's it. Pass this into ```Localizer.Localize``` and from here on out all the text whose variable names match those in the "LocalizedText" sheet will be kept up to date with the latest selected locale.

Future Work
===========
Right now everything relies on an object being manually added to the Localizer class. The localizer keeps an internal list of objects, this list is only pruned when the system is relocalized. Realistically, the system will not be relocalized during runtime, there needs to be an insert count (every 20 objects?) that triggers a prune of the list when it is reached.
With every obejct needing to be manually added, there is tight coupling between Localizer and localized text. Ideally the ```Localize``` function should be an [Event](https://gist.github.com/gszauer/6554489).

Ideally, localized text should be loaded from a server and cached locally. The LocalizedDocument should be responsible for this.
